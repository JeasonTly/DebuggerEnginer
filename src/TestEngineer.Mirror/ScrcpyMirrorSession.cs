using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Mirror;

/// <summary>方案 A：拉起 scrcpy.exe，轮询主窗口 HWND 供 WPF HwndHost 嵌入。</summary>
public sealed class ScrcpyMirrorSession : IMirrorSession
{
    private readonly IToolLocator _tools;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private Process? _process;
    private CancellationTokenSource? _lifetime;
    private MirrorSessionState _state = MirrorSessionState.Stopped;
    private IntPtr _hwnd = IntPtr.Zero;
    private string? _lastError;
    private string? _serial;

    public ScrcpyMirrorSession(IToolLocator tools, ILogger<ScrcpyMirrorSession>? logger = null)
    {
        _tools = tools;
        _logger = logger ?? NullLogger<ScrcpyMirrorSession>.Instance;
    }

    public MirrorSessionState State
    {
        get { lock (_gate) return _state; }
    }

    public IntPtr WindowHandle
    {
        get { lock (_gate) return _hwnd; }
    }

    public string? LastError
    {
        get { lock (_gate) return _lastError; }
    }

    public event EventHandler? StateChanged;

    public async Task StartAsync(AndroidDevice device, MirrorOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        options ??= new MirrorOptions();

        await StopAsync(cancellationToken).ConfigureAwait(false);

        var scrcpy = _tools.FindScrcpy();
        if (string.IsNullOrWhiteSpace(scrcpy) || !File.Exists(scrcpy))
        {
            SetFailed("未找到 scrcpy.exe。请确认 tools/third-party 已解压或在设置中指定路径。");
            throw new FileNotFoundException(_lastError);
        }

        SetState(MirrorSessionState.Starting);
        _serial = device.Serial;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var psi = new ProcessStartInfo
        {
            FileName = scrcpy,
            WorkingDirectory = Path.GetDirectoryName(scrcpy) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        psi.ArgumentList.Add("--serial");
        psi.ArgumentList.Add(device.Serial);
        psi.ArgumentList.Add("--window-title");
        psi.ArgumentList.Add($"TestEngineer-{device.Serial}");
        psi.ArgumentList.Add("--max-size");
        psi.ArgumentList.Add(options.MaxSize.ToString());
        psi.ArgumentList.Add("--video-bit-rate");
        psi.ArgumentList.Add(options.BitRate.ToString());
        psi.ArgumentList.Add("--max-fps");
        psi.ArgumentList.Add(options.MaxFps.ToString());
        if (options.StayAwake) psi.ArgumentList.Add("--stay-awake");
        if (options.TurnScreenOff) psi.ArgumentList.Add("--turn-screen-off");
        if (options.Borderless) psi.ArgumentList.Add("--window-borderless");
        if (!string.IsNullOrWhiteSpace(options.RecordPath))
        {
            psi.ArgumentList.Add("--record");
            psi.ArgumentList.Add(options.RecordPath);
        }

        // Prefer same-directory adb if present; otherwise rely on PATH / SCRCPY_SERVER_PATH defaults.
        var adb = _tools.FindAdb();
        if (!string.IsNullOrWhiteSpace(adb))
        {
            psi.Environment["ADB"] = adb;
        }

        try
        {
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) _logger.LogDebug("scrcpy[{Serial}] {Line}", _serial, e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) _logger.LogInformation("scrcpy[{Serial}] {Line}", _serial, e.Data);
            };
            _process.Exited += (_, _) =>
            {
                lock (_gate)
                {
                    if (_state is MirrorSessionState.Running or MirrorSessionState.Starting)
                    {
                        _state = MirrorSessionState.Failed;
                        _lastError ??= $"scrcpy 已退出（code={SafeExitCode(_process)}）";
                        _hwnd = IntPtr.Zero;
                    }
                }
                StateChanged?.Invoke(this, EventArgs.Empty);
            };

            if (!_process.Start())
            {
                SetFailed("无法启动 scrcpy 进程。");
                throw new InvalidOperationException(_lastError);
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            var hwnd = await WaitForWindowAsync($"TestEngineer-{device.Serial}", _lifetime.Token).ConfigureAwait(false);
            lock (_gate)
            {
                _hwnd = hwnd;
                _state = MirrorSessionState.Running;
                _lastError = null;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetFailed(ex.Message);
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? proc;
        lock (_gate)
        {
            proc = _process;
            _process = null;
            _hwnd = IntPtr.Zero;
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
        }

        if (proc is null)
        {
            SetState(MirrorSessionState.Stopped);
            return;
        }

        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "停止 scrcpy 失败");
        }
        finally
        {
            proc.Dispose();
            SetState(MirrorSessionState.Stopped);
        }
    }

    public Task SendKeyAsync(string keycode, CancellationToken cancellationToken = default)
    {
        // Key injection is handled by scrcpy itself when focused; host can also use adb keyevent via UI layer.
        _logger.LogDebug("SendKeyAsync({Key}) — 由嵌入窗口焦点转发或上层 adb keyevent 处理", keycode);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void SetFailed(string message)
    {
        lock (_gate)
        {
            _state = MirrorSessionState.Failed;
            _lastError = message;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetState(MirrorSessionState state)
    {
        lock (_gate)
        {
            _state = state;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static int SafeExitCode(Process? process)
    {
        try { return process?.ExitCode ?? -1; }
        catch { return -1; }
    }

    private static async Task<IntPtr> WaitForWindowAsync(string title, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hwnd = FindWindowByTitle(title);
            if (hwnd != IntPtr.Zero)
            {
                return hwnd;
            }
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"等待 scrcpy 窗口超时：{title}");
    }

    private static IntPtr FindWindowByTitle(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var sb = new System.Text.StringBuilder(512);
            _ = GetWindowText(hWnd, sb, sb.Capacity);
            if (string.Equals(sb.ToString(), title, StringComparison.Ordinal))
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}
