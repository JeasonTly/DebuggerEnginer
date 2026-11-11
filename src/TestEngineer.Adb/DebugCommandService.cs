using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb;

/// <summary>调试命令：数据驱动命令、monkey（按时长）、bugreport、logcat 导出。</summary>
public sealed partial class DebugCommandService : IDebugCommandService
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MonkeyKillGrace = TimeSpan.FromSeconds(8);
    private const string MonkeyProcessMarker = "com.android.commands.monkey";
    // 方括号写法避免 pkill -f 匹配到执行它自己的 sh -c 命令行。
    private const string MonkeyProcessPattern = "com.android.commands.monk[e]y";

    private readonly IAdbProcessRunner _runner;
    private readonly IReadOnlyList<DebugCommandDefinition> _commands;
    private readonly IReadOnlyList<ConsoleChipDefinition> _chips;
    private readonly string _exportDirectory;

    public DebugCommandService(
        IAdbProcessRunner runner,
        IEnumerable<DebugCommandDefinition>? commands = null,
        IEnumerable<ConsoleChipDefinition>? chips = null,
        string? exportDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        // 注意：Microsoft DI 会把未注册的 IEnumerable<T> 解析成空集合而不是 null，因此"空"也视为未提供，改用 JSON/内置清单。
        var suppliedCommands = commands?.ToList();
        var suppliedChips = chips?.ToList();
        var catalog = suppliedCommands is { Count: > 0 } && suppliedChips is { Count: > 0 } ? null : DebugCommandCatalog.LoadDefault();
        _commands = suppliedCommands is { Count: > 0 } ? suppliedCommands : catalog!.Commands;
        _chips = suppliedChips is { Count: > 0 } ? suppliedChips : catalog?.ConsoleChips ?? DebugCommandCatalog.LoadDefault().ConsoleChips;
        _exportDirectory = string.IsNullOrWhiteSpace(exportDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TestEngineer", "Exports")
            : exportDirectory;
        Directory.CreateDirectory(_exportDirectory);
    }

    public IReadOnlyList<DebugCommandDefinition> Commands => _commands;

    public IReadOnlyList<ConsoleChipDefinition> ConsoleChips => _chips;

    public async Task<bool> IsAvailableAsync(
        string serial,
        DebugCommandDefinition command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.WhenPackageExists))
        {
            return true;
        }

        var result = await _runner.RunAsync(
            ["shell", "pm", "path", command.WhenPackageExists],
            serial,
            TimeSpan.FromSeconds(15),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.StandardOutput.Contains("package:", StringComparison.Ordinal);
    }

    public async Task<AdbCommandResult> CaptureScreenshotAsync(
        string serial,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(localPath)) ?? ".");

        var remote = $"/sdcard/Download/te-screencap-{Guid.NewGuid():N}.png";
        var capture = await _runner.RunAsync(
            ["shell", "screencap", "-p", remote],
            serial,
            TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!capture.Succeeded)
        {
            return capture;
        }

        try
        {
            return await _runner.RunAsync(
                ["pull", remote, localPath],
                serial,
                TimeSpan.FromSeconds(60),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _runner.ShellAsync(serial, $"rm -f {ShellQuote.Quote(remote)}", TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    [GeneratedRegex(@"^\s*//\s*CRASH:", RegexOptions.CultureInvariant)]
    private static partial Regex CrashRegex();

    [GeneratedRegex(@"^\s*//\s*NOT RESPONDING:", RegexOptions.CultureInvariant)]
    private static partial Regex AnrRegex();

    [GeneratedRegex(@"^\[\s*(?<p>\d+)%\]", RegexOptions.CultureInvariant)]
    private static partial Regex ProgressRegex();

    public Task<AdbCommandResult> RunAsync(
        string serial,
        string commandId,
        IReadOnlyDictionary<string, string>? parameters = null,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var definition = Commands.FirstOrDefault(c => c.Id == commandId)
            ?? throw new ArgumentException($"未知的调试命令：{commandId}", nameof(commandId));

        if (definition.AdbArgs.Count == 0)
        {
            return RunKindAsync(serial, definition, onOutput, cancellationToken);
        }

        var args = DebugCommandCatalog.Expand(definition, parameters);
        return _runner.RunAsync(args, serial, definition.Timeout ?? DefaultCommandTimeout, onOutput, onOutput, cancellationToken);
    }

    /// <summary>无 adb 参数模板的 kind 类命令：输出到默认导出目录；Monkey 需通过专用面板（时长/包名）运行。</summary>
    private async Task<AdbCommandResult> RunKindAsync(string serial, DebugCommandDefinition definition, Action<string>? onOutput, CancellationToken cancellationToken)
    {
        var stamp = $"{serial}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var result = definition.Kind switch
        {
            "screencap" => await CaptureScreenshotAsync(serial, Path.Combine(_exportDirectory, $"screenshot-{stamp}.png"), cancellationToken).ConfigureAwait(false),
            "export-logs" => await ExportLogcatZipAsync(serial, Path.Combine(_exportDirectory, $"logcat-{stamp}.zip"), cancellationToken).ConfigureAwait(false),
            "bugreport" => await CaptureBugreportAsync(serial, Path.Combine(_exportDirectory, $"bugreport-{stamp}.zip"), null, cancellationToken).ConfigureAwait(false),
            "monkey-app" or "monkey-system" => AdbCommandResult.Failure("Monkey 需要指定包名与时长，请使用 Monkey 面板。"),
            _ => AdbCommandResult.Failure($"命令 {definition.Id} 没有可执行的参数模板（kind={definition.Kind ?? "无"}）。"),
        };

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            onOutput?.Invoke(result.StandardOutput.TrimEnd());
        }

        return result;
    }

    public async Task<MonkeyRunResult> RunMonkeyAsync(
        string serial,
        MonkeyOptions options,
        IProgress<RunningTaskState>? progress = null,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.DurationSeconds, 1);
        if (options.Package is not null && !Regex.IsMatch(options.Package, @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)*$", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException($"非法包名：{options.Package}", nameof(options));
        }

        var args = BuildMonkeyArguments(options);
        var duration = TimeSpan.FromSeconds(options.DurationSeconds);
        var state = RunningTaskState.Begin("Monkey 压测", options.Package ?? "全系统");
        progress?.Report(state);

        var crashes = 0;
        var anrs = 0;
        void OnLine(string line)
        {
            if (CrashRegex().IsMatch(line))
            {
                Interlocked.Increment(ref crashes);
            }
            else if (AnrRegex().IsMatch(line))
            {
                Interlocked.Increment(ref anrs);
            }

            onOutput?.Invoke(line);
        }

        var stopwatch = Stopwatch.StartNew();
        using var localCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        var runTask = _runner.RunAsync(args, serial, Timeout.InfiniteTimeSpan, OnLine, OnLine, localCts.Token);
        var stoppedByDuration = false;

        try
        {
            while (!runTask.IsCompleted)
            {
                var remaining = duration - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    stoppedByDuration = true;
                    break;
                }

                progress?.Report(state.Update(stopwatch.Elapsed.TotalSeconds / duration.TotalSeconds, $"已运行 {stopwatch.Elapsed:mm\\:ss} / {duration:mm\\:ss}"));
                await Task.WhenAny(runTask, Task.Delay(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            await StopMonkeyAsync(serial, runTask, localCts).ConfigureAwait(false);
            progress?.Report(state.Finish(RunningTaskStatus.Cancelled, "已取消"));
            throw;
        }

        if (stoppedByDuration)
        {
            await StopMonkeyAsync(serial, runTask, localCts).ConfigureAwait(false);
        }

        var command = await runTask.ConfigureAwait(false);
        stopwatch.Stop();

        // 按时长主动结束属于正常完成，不应因被杀进程的退出码而判失败。
        if (stoppedByDuration)
        {
            command = command with { ExitCode = 0, Cancelled = false, TimedOut = false };
        }

        progress?.Report(state.Finish(command.Succeeded ? RunningTaskStatus.Succeeded : RunningTaskStatus.Failed, $"崩溃 {crashes}，ANR {anrs}"));
        return new MonkeyRunResult(command, stopwatch.Elapsed, stoppedByDuration, crashes, anrs);
    }

    public async Task<AdbCommandResult> CaptureBugreportAsync(
        string serial,
        string outputPath,
        IProgress<RunningTaskState>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var full = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var state = RunningTaskState.Begin("采集 bugreport");
        progress?.Report(state);
        void OnLine(string line)
        {
            var match = ProgressRegex().Match(line);
            if (match.Success && int.TryParse(match.Groups["p"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var percent))
            {
                state = state.Update(percent / 100d, line);
                progress?.Report(state);
            }
        }

        var result = await _runner.RunAsync(["bugreport", full], serial, TimeSpan.FromMinutes(15), OnLine, OnLine, cancellationToken).ConfigureAwait(false);
        progress?.Report(state.Finish(
            result.Cancelled ? RunningTaskStatus.Cancelled : result.Succeeded ? RunningTaskStatus.Succeeded : RunningTaskStatus.Failed,
            result.Succeeded ? full : result.FailureMessage));
        return result;
    }

    public async Task<AdbCommandResult> ExportLogcatZipAsync(string serial, string zipPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);

        // -b all 在部分 user 版本被拒绝，失败时退回默认缓冲区。
        var result = await _runner.RunAsync(["logcat", "-d", "-b", "all", "-v", "threadtime"], serial, TimeSpan.FromMinutes(2), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            result = await _runner.RunAsync(["logcat", "-d", "-v", "threadtime"], serial, TimeSpan.FromMinutes(2), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (!result.Succeeded)
        {
            return result;
        }

        var meta = new StringBuilder()
            .AppendLine("TestEngineer logcat 导出")
            .AppendLine($"设备序列号: {serial}")
            .AppendLine($"导出时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:sszzz}")
            .AppendLine($"日志行数: {result.StandardOutput.Count(c => c == '\n')}")
            .ToString();

        ZipPackHelper.CreateFromTextEntries(
            zipPath,
            [
                new KeyValuePair<string, string>("logcat.txt", result.StandardOutput),
                new KeyValuePair<string, string>("meta.txt", meta),
            ]);
        return result with { StandardOutput = $"已导出 {Path.GetFullPath(zipPath)}" };
    }

    public static IReadOnlyList<string> BuildMonkeyArguments(MonkeyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var command = new StringBuilder("monkey");
        if (options.Package is not null)
        {
            command.Append(" -p ").Append(options.Package);
        }

        command.Append(CultureInfo.InvariantCulture, $" --throttle {Math.Max(0, options.ThrottleMs)}");
        if (options.Seed is { } seed)
        {
            command.Append(CultureInfo.InvariantCulture, $" -s {seed}");
        }

        if (options.IgnoreCrashes)
        {
            command.Append(" --ignore-crashes");
        }

        if (options.IgnoreTimeouts)
        {
            command.Append(" --ignore-timeouts");
        }

        command.Append(" --ignore-security-exceptions --monitor-native-crashes -v -v");
        command.Append(CultureInfo.InvariantCulture, $" {Math.Max(1, options.EventCount)}");
        return ["shell", command.ToString()];
    }

    private async Task StopMonkeyAsync(string serial, Task<AdbCommandResult> runTask, CancellationTokenSource localCts)
    {
        await KillMonkeyOnDeviceAsync(serial).ConfigureAwait(false);
        var finished = await Task.WhenAny(runTask, Task.Delay(MonkeyKillGrace)).ConfigureAwait(false);
        if (finished != runTask)
        {
            // 设备端未能结束时，终止本地 adb 进程，避免悬挂。
            await localCts.CancelAsync().ConfigureAwait(false);
            await KillMonkeyOnDeviceAsync(serial).ConfigureAwait(false);
        }

        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 结果由调用方统一读取；此处只保证任务已结束。
        }
    }

    private async Task KillMonkeyOnDeviceAsync(string serial)
    {
        try
        {
            await _runner.ShellAsync(serial, $"pkill -f '{MonkeyProcessPattern}'", TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            var ps = await _runner.ShellAsync(serial, "ps -A -o PID,ARGS", TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            foreach (var line in ps.StandardOutput.Split('\n'))
            {
                if (!line.Contains(MonkeyProcessMarker, StringComparison.Ordinal) || line.Contains("pkill", StringComparison.Ordinal) || line.Contains("ps -A", StringComparison.Ordinal))
                {
                    continue;
                }

                var pidText = line.Trim().Split(' ', 2)[0];
                if (int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
                {
                    await _runner.ShellAsync(serial, $"kill -9 {pid}", TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 尽力而为：设备已断开等情况下无法清理。
        }
    }
}
