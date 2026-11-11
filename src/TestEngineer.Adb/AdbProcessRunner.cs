using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb;

/// <summary>
/// 通过 <see cref="ProcessStartInfo.ArgumentList"/> 启动 adb（不经 shell、不拼命令行字符串）。
/// 异步读取 stdout/stderr（UTF-8），支持流式回调、超时与取消；取消/超时会终止整个进程树。
/// </summary>
public sealed class AdbProcessRunner : IAdbProcessRunner
{
    /// <summary>未指定超时时的默认值。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PipeDrainTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PartialLineFlushDelay = TimeSpan.FromMilliseconds(80);

    private readonly IToolLocator _locator;
    private readonly ILogger _logger;

    public AdbProcessRunner(IToolLocator locator, ILogger<AdbProcessRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _locator = locator;
        _logger = logger ?? NullLogger<AdbProcessRunner>.Instance;
    }

    public async Task<AdbCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? serial = null,
        TimeSpan? timeout = null,
        Action<string>? onStdout = null,
        Action<string>? onStderr = null,
        CancellationToken cancellationToken = default)
    {
        var adbPath = _locator.FindAdb()
            ?? throw new AdbNotFoundException("未找到 adb.exe。请将 platform-tools 放入 tools/third-party/platform-tools，或在设置中指定 adb 路径。");

        var startInfo = BuildStartInfo(adbPath, arguments, serial);
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _logger.LogError(ex, "无法启动 adb：{Path}", adbPath);
            return AdbCommandResult.Failure($"无法启动 adb（{adbPath}）：{ex.Message}");
        }

        // adb 不应等待 stdin；立即关闭以防交互式子命令挂起。
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // 进程已退出。
        }

        _logger.LogDebug("adb {Args}（serial={Serial}）", string.Join(' ', arguments), serial);

        using var timeoutCts = new CancellationTokenSource();
        if (effectiveTimeout != Timeout.InfiniteTimeSpan)
        {
            timeoutCts.CancelAfter(effectiveTimeout);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutPump = PumpAsync(process.StandardOutput, stdout, onStdout);
        var stderrPump = PumpAsync(process.StandardError, stderr, onStderr);

        var interrupted = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            interrupted = true;
            KillProcessTree(process);
        }

        // 子进程（如 adb 守护进程）可能继承管道句柄，导致读取迟迟不结束，因此限时等待。
        try
        {
            await Task.WhenAll(stdoutPump, stderrPump).WaitAsync(PipeDrainTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogDebug("adb 输出管道未在 {Timeout} 内关闭，已放弃等待", PipeDrainTimeout);
        }

        stopwatch.Stop();
        var timedOut = interrupted && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
        var cancelled = interrupted && !timedOut;
        var exitCode = process.HasExited ? process.ExitCode : -1;
        return new AdbCommandResult(
            exitCode,
            Normalize(stdout),
            Normalize(stderr),
            timedOut,
            cancelled,
            stopwatch.Elapsed);
    }

    /// <summary>以参数列表构造启动信息；有序列号时在子命令前附加 -s。</summary>
    public static ProcessStartInfo BuildStartInfo(string adbPath, IReadOnlyList<string> arguments, string? serial = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adbPath);
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
        {
            throw new ArgumentException("adb 参数不能为空。", nameof(arguments));
        }

        var startInfo = new ProcessStartInfo(adbPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (!string.IsNullOrWhiteSpace(serial))
        {
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add(serial);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string Normalize(StringBuilder builder)
    {
        var text = builder.ToString();
        return text.Contains('\r', StringComparison.Ordinal) ? text.Replace("\r\n", "\n", StringComparison.Ordinal) : text;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // 进程已退出或无权限，忽略。
        }
    }

    /// <summary>
    /// 读取流：完整文本累积到 <paramref name="sink"/>；按行回调，不完整的末行在 80ms 空闲后也送出
    /// （adb track-devices 的空列表帧 "0000" 不带换行）。
    /// </summary>
    private static async Task PumpAsync(StreamReader reader, StringBuilder sink, Action<string>? callback)
    {
        var buffer = new char[4096];
        var pending = new StringBuilder();
        Task<int>? readTask = null;
        try
        {
            while (true)
            {
                readTask ??= reader.ReadAsync(buffer, 0, buffer.Length);
                if (pending.Length > 0 && callback is not null)
                {
                    var finished = await Task.WhenAny(readTask, Task.Delay(PartialLineFlushDelay)).ConfigureAwait(false);
                    if (finished != readTask)
                    {
                        Emit(callback, pending.ToString());
                        pending.Clear();
                        continue;
                    }
                }

                var count = await readTask.ConfigureAwait(false);
                readTask = null;
                if (count == 0)
                {
                    break;
                }

                sink.Append(buffer, 0, count);
                if (callback is null)
                {
                    continue;
                }

                pending.Append(buffer, 0, count);
                DrainCompleteLines(pending, callback);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // 进程被终止后流关闭，读取到此为止。
        }

        if (pending.Length > 0 && callback is not null)
        {
            Emit(callback, pending.ToString());
        }
    }

    private static void DrainCompleteLines(StringBuilder pending, Action<string> callback)
    {
        while (true)
        {
            var text = pending.ToString();
            var newline = text.IndexOf('\n', StringComparison.Ordinal);
            if (newline < 0)
            {
                return;
            }

            Emit(callback, text[..newline]);
            pending.Remove(0, newline + 1);
        }
    }

    private static void Emit(Action<string> callback, string line)
    {
        try
        {
            callback(line.TrimEnd('\r'));
        }
        catch (Exception)
        {
            // 回调异常不应影响进程读取。
        }
    }
}
