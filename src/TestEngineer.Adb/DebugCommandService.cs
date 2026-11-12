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

    [GeneratedRegex(@"^[A-Za-z][\w]*(\.[\w]+)+/((\.[\w]+)+|[\w]+(\.[\w]+)*)(\$[\w]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex LauncherComponentRegex();

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
            "export-logs" => await ExportLogcatZipAsync(
                serial,
                Path.Combine(_exportDirectory, $"{SanitizeFileToken(await ReadPropAsync(serial, "ro.product.model", cancellationToken).ConfigureAwait(false))}_{DateTime.Now:yyyyMMdd}.zip"),
                progress: null,
                cancellationToken).ConfigureAwait(false),
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

        if (options.Package is not null)
        {
            progress?.Report(state.Update(0, "正在打开 " + options.Package));
            onOutput?.Invoke("打开 " + options.Package);
            var opened = await OpenMonkeyTargetAsync(serial, options.Package, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(opened.CombinedOutput))
            {
                onOutput?.Invoke(opened.CombinedOutput.TrimEnd());
            }

            if (!opened.Succeeded)
            {
                var message = string.IsNullOrWhiteSpace(opened.FailureMessage)
                    ? "未能打开应用，Monkey 未开始。"
                    : "未能打开应用：" + opened.FailureMessage;
                progress?.Report(state.Finish(RunningTaskStatus.Failed, message));
                return new MonkeyRunResult(AdbCommandResult.Failure(message), TimeSpan.Zero, false, 0, 0);
            }

            progress?.Report(state.Update(0, "等待 " + options.Package + " 到前台"));
            await WaitUntilPackageForegroundAsync(serial, options.Package, cancellationToken).ConfigureAwait(false);
            progress?.Report(state.Update(0, "Monkey 压测"));
        }

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

    public async Task<AdbCommandResult> StopRunningMonkeyAsync(string serial, CancellationToken cancellationToken = default)
    {
        var before = await _runner.ShellAsync(
            serial,
            "pidof com.android.commands.monkey monkey",
            TimeSpan.FromSeconds(5),
            cancellationToken).ConfigureAwait(false);
        var hadProcess = !string.IsNullOrWhiteSpace(before.StandardOutput);
        await KillMonkeyOnDeviceAsync(serial).ConfigureAwait(false);
        var after = await _runner.ShellAsync(
            serial,
            "pidof com.android.commands.monkey monkey",
            TimeSpan.FromSeconds(5),
            cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(after.StandardOutput))
        {
            return AdbCommandResult.Failure("已发送停止，设备上仍有 Monkey 进程：" + after.StandardOutput.Trim());
        }

        return hadProcess
            ? new AdbCommandResult(0, "已停止设备上的 Monkey。", string.Empty)
            : new AdbCommandResult(0, "设备上没有正在运行的 Monkey。", string.Empty);
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

    /// <summary>
    /// 导出诊断日志包：logcat + bugreport + 平台日志目录（MTK/展讯/高通），打成 zip。
    /// </summary>
    public async Task<AdbCommandResult> ExportLogcatZipAsync(
        string serial,
        string zipPath,
        IProgress<RunningTaskState>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);

        var state = RunningTaskState.Begin("导出日志包", "准备中…");
        progress?.Report(state);

        var model = await ReadPropAsync(serial, "ro.product.model", cancellationToken).ConfigureAwait(false)
                    ?? await ReadPropAsync(serial, "ro.product.device", cancellationToken).ConfigureAwait(false)
                    ?? "device";
        var modelToken = SanitizeFileToken(model);
        var report = new StringBuilder()
            .AppendLine("TestEngineer 诊断日志包")
            .AppendLine($"设备序列号: {serial}")
            .AppendLine($"型号: {model}")
            .AppendLine($"导出时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:sszzz}");

        var staging = Path.Combine(Path.GetTempPath(), "TestEngineer", "log-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var platformDir = Path.Combine(staging, "platform-logs");
        Directory.CreateDirectory(platformDir);

        try
        {
            // 1) logcat — 约 0→15%
            state = state.Update(0.05, "正在采集 logcat…");
            progress?.Report(state);
            var logcat = await _runner.RunAsync(
                ["logcat", "-d", "-b", "all", "-v", "threadtime"],
                serial,
                TimeSpan.FromMinutes(2),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!logcat.Succeeded || string.IsNullOrWhiteSpace(logcat.StandardOutput))
            {
                logcat = await _runner.RunAsync(
                    ["logcat", "-d", "-v", "threadtime"],
                    serial,
                    TimeSpan.FromMinutes(2),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            if (logcat.Succeeded && !string.IsNullOrWhiteSpace(logcat.StandardOutput))
            {
                await File.WriteAllTextAsync(
                    Path.Combine(staging, "logcat.txt"),
                    logcat.StandardOutput,
                    cancellationToken).ConfigureAwait(false);
                report.AppendLine($"logcat: OK（{logcat.StandardOutput.Count(c => c == '\n')} 行）");
                state = state.Update(0.15, $"logcat 完成（{logcat.StandardOutput.Count(c => c == '\n')} 行）");
            }
            else
            {
                report.AppendLine("logcat: 失败 — " + (logcat.FailureMessage ?? "无输出"));
                state = state.Update(0.15, "logcat 采集失败，继续后续步骤…");
            }

            progress?.Report(state);

            // 2) bugreport — 约 15%→55%（通常最久）
            state = state.Update(0.18, "正在导出 bugreport（可能较久）…");
            progress?.Report(state);
            var bugreportPath = Path.Combine(staging, "bugreport.zip");
            var bugProgress = new Progress<RunningTaskState>(s =>
            {
                // bugreport 子进度映射到 18%–55%
                var p = 0.18 + (s.Progress ?? 0.5) * 0.37;
                progress?.Report(state.Update(p, s.Message is { Length: > 0 } m ? $"bugreport：{m}" : "正在导出 bugreport…"));
            });
            var bug = await CaptureBugreportAsync(serial, bugreportPath, bugProgress, cancellationToken)
                .ConfigureAwait(false);
            report.AppendLine(bug.Succeeded && File.Exists(bugreportPath)
                ? "bugreport: OK"
                : "bugreport: 失败 — " + (bug.FailureMessage ?? "未生成文件"));
            state = state.Update(0.55, bug.Succeeded ? "bugreport 完成" : "bugreport 失败，继续拉取平台日志…");
            progress?.Report(state);

            // 3) 平台日志目录：MTK / 展讯 / 高通 — 约 55%→88%
            var fixedDirs = new[]
            {
                "/data/debuglogger",   // MTK
                "/sdcard/debuglogger", // MTK
                "/data/ylog",          // 展讯 Unisoc
                "/sdcard/ylog",        // 展讯
            };
            var qcomDirs = await ListQualcommDebugDirsAsync(serial, model, modelToken, cancellationToken)
                .ConfigureAwait(false);
            var platformTargets = new List<string>();
            foreach (var remote in fixedDirs)
            {
                if (await RemoteDirExistsAsync(serial, remote, cancellationToken).ConfigureAwait(false))
                {
                    platformTargets.Add(remote);
                }
                else
                {
                    report.AppendLine($"平台目录跳过（不存在）: {remote}");
                }
            }

            platformTargets.AddRange(qcomDirs);
            var platformTotal = Math.Max(1, platformTargets.Count);
            for (var i = 0; i < platformTargets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remote = platformTargets[i];
                var pct = 0.55 + 0.33 * ((i + 0.5) / platformTotal);
                state = state.Update(pct, $"正在拉取平台日志：{remote}");
                progress?.Report(state);

                var localName = remote.Trim('/').Replace('/', '_');
                var local = Path.Combine(platformDir, localName);
                Directory.CreateDirectory(local);
                var pull = await _runner.RunAsync(
                    ["pull", remote, local],
                    serial,
                    TimeSpan.FromMinutes(10),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var label = remote.Contains("Debug_Log", StringComparison.OrdinalIgnoreCase) ? "高通 Debug_Log" : "平台";
                report.AppendLine(pull.Succeeded
                    ? $"{label} 目录已拉取: {remote}"
                    : $"{label} 目录拉取失败: {remote} — {pull.FailureMessage}");
            }

            state = state.Update(0.88, "正在写入元数据并压缩…");
            progress?.Report(state);

            await File.WriteAllTextAsync(
                Path.Combine(staging, "meta.txt"),
                report.ToString(),
                cancellationToken).ConfigureAwait(false);

            ZipPackHelper.CreateFromDirectory(staging, zipPath);
            var full = Path.GetFullPath(zipPath);
            state = state.Finish(RunningTaskStatus.Succeeded, $"已导出 {Path.GetFileName(full)}");
            progress?.Report(state);
            return new AdbCommandResult(0, $"已导出 {full}\n{report}", "", false, false, TimeSpan.Zero);
        }
        catch (OperationCanceledException)
        {
            progress?.Report(state.Finish(RunningTaskStatus.Cancelled, "已取消"));
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report(state.Finish(RunningTaskStatus.Failed, ex.Message));
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch
            {
                // 临时目录清理失败可忽略
            }
        }
    }

    private async Task<string?> ReadPropAsync(string serial, string key, CancellationToken cancellationToken)
    {
        var r = await _runner.ShellAsync(serial, $"getprop {key}", TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);
        var v = (r.StandardOutput ?? "").Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private async Task<bool> RemoteDirExistsAsync(string serial, string remotePath, CancellationToken cancellationToken)
    {
        var escaped = remotePath.Replace("\"", "\\\"", StringComparison.Ordinal);
        var r = await _runner.ShellAsync(
                serial,
                $"[ -d \"{escaped}\" ] && echo EXISTS",
                TimeSpan.FromSeconds(15),
                cancellationToken)
            .ConfigureAwait(false);
        return (r.StandardOutput ?? "").Contains("EXISTS", StringComparison.Ordinal);
    }

    private async Task<IReadOnlyList<string>> ListQualcommDebugDirsAsync(
        string serial,
        string model,
        string modelToken,
        CancellationToken cancellationToken)
    {
        // 日志目录名固定为 Debug_Log_型号（优先精确路径，再扫 /sdcard）
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[]
                 {
                     "Debug_Log_" + model.Trim(),
                     "Debug_Log_" + modelToken,
                 })
        {
            if (string.IsNullOrWhiteSpace(name) || name is "Debug_Log_" or "Debug_Log_device")
            {
                continue;
            }

            var full = "/sdcard/" + name;
            if (await RemoteDirExistsAsync(serial, full, cancellationToken).ConfigureAwait(false))
            {
                candidates.Add(full);
            }
        }

        var r = await _runner.ShellAsync(serial, "ls -1 /sdcard", TimeSpan.FromSeconds(20), cancellationToken)
            .ConfigureAwait(false);
        if (r.Succeeded || !string.IsNullOrWhiteSpace(r.StandardOutput))
        {
            var prefixes = new[]
            {
                "Debug_Log_" + model.Trim(),
                "Debug_Log_" + modelToken,
                "Debug_Log_",
            };
            foreach (var raw in (r.StandardOutput ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!prefixes.Any(p => raw.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var full = "/sdcard/" + raw.Trim();
                if (await RemoteDirExistsAsync(serial, full, cancellationToken).ConfigureAwait(false))
                {
                    candidates.Add(full);
                }
            }
        }

        return candidates.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>生成安全文件名片段（型号）。</summary>
    public static string SanitizeFileToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "device";
        }

        var chars = value.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' or ' ')
            {
                chars[i] = '_';
            }
        }

        var s = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(s) ? "device" : s;
    }

    /// <summary>
    /// 先解析 LAUNCHER 组件，再用 <c>am start -n</c> 打开。
    /// <c>am start -p</c> 在主入口不是默认 Activity（isDefault=false）时会报 unable to resolve Intent。
    /// 不用 monkey 打开：它会等这次输入被处理完，窗口还没起来时会卡在 30 秒注入超时上。
    /// </summary>
    private async Task<AdbCommandResult> OpenMonkeyTargetAsync(string serial, string package, CancellationToken cancellationToken)
    {
        var resolved = await _runner.ShellAsync(
            serial,
            "cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER " + package,
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);
        if (!TryParseLauncherComponent(resolved.StandardOutput, out var component))
        {
            var detail = string.IsNullOrWhiteSpace(resolved.CombinedOutput) ? "没有输出。" : resolved.CombinedOutput.Trim();
            return AdbCommandResult.Failure($"未找到 {package} 的 LAUNCHER 主入口。{detail}");
        }

        var launch = await _runner.ShellAsync(
            serial,
            "am start -n " + ShellQuote.Quote(component),
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);
        if (launch.Succeeded
            && !launch.CombinedOutput.Contains("Error", StringComparison.Ordinal)
            && !launch.CombinedOutput.Contains("Exception", StringComparison.Ordinal)
            && !launch.CombinedOutput.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            return launch;
        }

        return launch.Succeeded ? launch with { ExitCode = 1 } : launch;
    }

    private static bool TryParseLauncherComponent(string? output, out string component)
    {
        component = string.Empty;
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.Contains(' ', StringComparison.Ordinal) || !LauncherComponentRegex().IsMatch(line))
            {
                continue;
            }

            component = line;
        }

        return component.Length > 0;
    }

    private async Task WaitUntilPackageForegroundAsync(string serial, string package, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var probe = await _runner.ShellAsync(
                serial,
                "dumpsys activity top",
                TimeSpan.FromSeconds(4),
                cancellationToken).ConfigureAwait(false);
            if (probe.CombinedOutput.Contains(package + "/", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
        }
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
            await _runner.ShellAsync(
                serial,
                "kill -9 $(pidof com.android.commands.monkey) $(pidof monkey)",
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
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
