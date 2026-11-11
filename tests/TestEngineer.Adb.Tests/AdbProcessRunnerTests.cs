using System.Diagnostics;
using TestEngineer.Core.Abstractions;

namespace TestEngineer.Adb.Tests;

/// <summary>用 Windows 自带的 cmd.exe / ping.exe 充当"adb"，验证进程执行器的参数、超时与取消行为。</summary>
public sealed class AdbProcessRunnerTests
{
    private static readonly string CmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
    private static readonly string PingPath = Path.Combine(Environment.SystemDirectory, "PING.EXE");

    [Fact]
    public void BuildStartInfo_UsesArgumentListAndPrependsSerial()
    {
        var info = AdbProcessRunner.BuildStartInfo("adb.exe", ["shell", "ls -la '/sdcard/My Files'"], "ABC 123");

        Assert.False(info.UseShellExecute);
        Assert.Equal(["-s", "ABC 123", "shell", "ls -la '/sdcard/My Files'"], info.ArgumentList);
        Assert.Equal(string.Empty, info.Arguments);
    }

    [Fact]
    public void BuildStartInfo_EmptyArguments_Throws()
    {
        Assert.Throws<ArgumentException>(() => AdbProcessRunner.BuildStartInfo("adb.exe", []));
    }

    [Fact]
    public async Task RunAsync_CapturesStdoutStderrAndExitCode()
    {
        var runner = new AdbProcessRunner(new FixedToolLocator(CmdPath));

        var result = await runner.RunAsync(["/c", "echo out& echo err 1>&2& exit /b 3"]);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.Equal("out", result.StandardOutput.Trim());
        Assert.Equal("err", result.StandardError.Trim());
        Assert.False(result.TimedOut);
        Assert.False(result.Cancelled);
    }

    [Fact]
    public async Task RunAsync_StreamsLinesToCallback()
    {
        var runner = new AdbProcessRunner(new FixedToolLocator(CmdPath));
        var lines = new List<string>();

        var result = await runner.RunAsync(["/c", "echo one& echo two"], onStdout: line => { lock (lines) { lines.Add(line); } });

        Assert.True(result.Succeeded);
        Assert.Equal(["one", "two"], lines);
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsProcessAndReportsTimedOut()
    {
        var runner = new AdbProcessRunner(new FixedToolLocator(PingPath));
        var stopwatch = Stopwatch.StartNew();

        var result = await runner.RunAsync(["-n", "30", "127.0.0.1"], timeout: TimeSpan.FromMilliseconds(600));

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.False(result.Cancelled);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"超时后应尽快返回，实际 {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task RunAsync_Cancellation_KillsProcessAndReportsCancelled()
    {
        var runner = new AdbProcessRunner(new FixedToolLocator(PingPath));
        using var cts = new CancellationTokenSource();
        var task = runner.RunAsync(["-n", "30", "127.0.0.1"], timeout: Timeout.InfiniteTimeSpan, cancellationToken: cts.Token);
        await Task.Delay(400);

        await cts.CancelAsync();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.True(result.Cancelled);
        Assert.False(result.TimedOut);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RunAsync_AdbMissing_ThrowsAdbNotFound()
    {
        var runner = new AdbProcessRunner(new FixedToolLocator(null));

        await Assert.ThrowsAsync<AdbNotFoundException>(() => runner.RunAsync(["devices"]));
    }

    [Fact]
    public async Task RunAsync_ExecutableCannotStart_ReturnsFailureResult()
    {
        var runner = new AdbProcessRunner(new FixedToolLocator(Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N") + ".exe")));

        var result = await runner.RunAsync(["devices"]);

        Assert.False(result.Succeeded);
        Assert.Contains("无法启动", result.StandardError, StringComparison.Ordinal);
    }
}
