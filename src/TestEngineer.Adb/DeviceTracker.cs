using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TestEngineer.Adb.Parsing;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb;

/// <summary>
/// 设备热插拔跟踪。优先使用 <c>adb track-devices</c> 长连接作为"变化信号"，信号到达后
/// 防抖并执行一次 <c>adb devices -l</c> 取得权威列表（track-devices 的帧是长度前缀格式且不含 model 等信息）。
/// 若长连接反复快速退出，则降级为每 2 秒轮询 <c>adb devices -l</c>，之后再尝试恢复长连接。
/// </summary>
public sealed class DeviceTracker : IDeviceTracker
{
    private static readonly TimeSpan StableStreamThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SafetyResyncInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollingPhaseDuration = TimeSpan.FromSeconds(60);

    private readonly IAdbProcessRunner _runner;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _debounce;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();

    private IReadOnlyList<AndroidDevice> _devices = [];
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private string _mode = "stopped";
    private string? _lastError;

    public DeviceTracker(
        IAdbProcessRunner runner,
        ILogger<DeviceTracker>? logger = null,
        TimeSpan? pollInterval = null,
        TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _logger = logger ?? NullLogger<DeviceTracker>.Instance;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
        _debounce = debounce ?? TimeSpan.FromMilliseconds(150);
    }

    public event EventHandler<DevicesChangedEventArgs>? DevicesChanged;

    public IReadOnlyList<AndroidDevice> Devices
    {
        get
        {
            lock (_stateGate)
            {
                return _devices;
            }
        }
    }

    public string Mode
    {
        get
        {
            lock (_stateGate)
            {
                return _mode;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_stateGate)
            {
                return _lastError;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateGate)
        {
            if (_loopTask is not null)
            {
                return;
            }

            _loopCts = new CancellationTokenSource();
            _mode = "track-devices";
        }

        try
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 首次枚举失败（如未找到 adb）不阻止后台循环，错误已记录到 LastError。
            _logger.LogWarning(ex, "首次设备枚举失败");
        }

        lock (_stateGate)
        {
            _loopTask ??= Task.Run(() => LoopAsync(_loopCts!.Token));
        }
    }

    public async Task StopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_stateGate)
        {
            loop = _loopTask;
            cts = _loopCts;
            _loopTask = null;
            _loopCts = null;
            _mode = "stopped";
        }

        if (cts is null)
        {
            return;
        }

        await cts.CancelAsync().ConfigureAwait(false);
        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                // 循环退出超时不再阻塞停止流程；adb 子进程已被取消终止。
            }
        }

        cts.Dispose();
    }

    public async Task<IReadOnlyList<AndroidDevice>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AdbCommandResult result;
            try
            {
                result = await _runner.RunAsync(["devices", "-l"], null, TimeSpan.FromSeconds(10), cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (AdbNotFoundException ex)
            {
                SetError(ex.Message);
                throw;
            }

            if (!result.Succeeded)
            {
                SetError(result.FailureMessage);
                return Devices;
            }

            SetError(null);
            var parsed = DevicesListParser.Parse(result.StandardOutput);
            IReadOnlyList<AndroidDevice>? changedTo = null;
            lock (_stateGate)
            {
                if (!_devices.SequenceEqual(parsed))
                {
                    _devices = parsed;
                    changedTo = parsed;
                }
            }

            if (changedTo is not null)
            {
                RaiseDevicesChanged(changedTo);
            }

            return parsed;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _refreshGate.Dispose();
    }

    private async Task LoopAsync(CancellationToken token)
    {
        var quickFailures = 0;
        while (!token.IsCancellationRequested)
        {
            var started = Stopwatch.StartNew();
            try
            {
                SetMode("track-devices");
                await RunTrackStreamAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "track-devices 异常退出");
                SetError(ex.Message);
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            quickFailures = started.Elapsed < StableStreamThreshold ? quickFailures + 1 : 0;
            if (quickFailures >= 2)
            {
                _logger.LogInformation("track-devices 不稳定，降级为轮询");
                await PollingPhaseAsync(token).ConfigureAwait(false);
                quickFailures = 0;
            }
            else
            {
                await DelayQuietlyAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                await RefreshQuietlyAsync(token).ConfigureAwait(false);
            }
        }
    }

    private async Task RunTrackStreamAsync(CancellationToken token)
    {
        var signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var consumer = Task.Run(() => ConsumeSignalsAsync(signals.Reader, streamCts.Token), CancellationToken.None);
        try
        {
            await _runner.RunAsync(
                ["track-devices"],
                null,
                Timeout.InfiniteTimeSpan,
                onStdout: _ => signals.Writer.TryWrite(true),
                cancellationToken: streamCts.Token).ConfigureAwait(false);
        }
        finally
        {
            await streamCts.CancelAsync().ConfigureAwait(false);
            signals.Writer.TryComplete();
            try
            {
                await consumer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 预期的取消。
            }
        }
    }

    private async Task ConsumeSignalsAsync(ChannelReader<bool> reader, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            // 变化信号到达或每 10 秒做一次安全重同步（覆盖不带换行的空列表帧等边界情况）。
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            wait.CancelAfter(SafetyResyncInterval);
            try
            {
                if (!await reader.WaitToReadAsync(wait.Token).ConfigureAwait(false))
                {
                    return;
                }

                await Task.Delay(_debounce, token).ConfigureAwait(false);
                while (reader.TryRead(out _))
                {
                    // 合并防抖窗口内的多次信号。
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // 仅安全重同步超时。
            }

            await RefreshQuietlyAsync(token).ConfigureAwait(false);
        }
    }

    private async Task PollingPhaseAsync(CancellationToken token)
    {
        SetMode("polling");
        var phase = Stopwatch.StartNew();
        while (!token.IsCancellationRequested && phase.Elapsed < PollingPhaseDuration)
        {
            await RefreshQuietlyAsync(token).ConfigureAwait(false);
            await DelayQuietlyAsync(_pollInterval, token).ConfigureAwait(false);
        }
    }

    private async Task RefreshQuietlyAsync(CancellationToken token)
    {
        try
        {
            await RefreshAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 停止中。
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "设备枚举失败");
            SetError(ex.Message);
        }
    }

    private static async Task DelayQuietlyAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 停止中。
        }
    }

    private void SetMode(string mode)
    {
        lock (_stateGate)
        {
            _mode = mode;
        }
    }

    private void SetError(string? error)
    {
        lock (_stateGate)
        {
            _lastError = error;
        }
    }

    private void RaiseDevicesChanged(IReadOnlyList<AndroidDevice> devices)
    {
        try
        {
            DevicesChanged?.Invoke(this, new DevicesChangedEventArgs(devices));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DevicesChanged 订阅者抛出异常");
        }
    }
}
