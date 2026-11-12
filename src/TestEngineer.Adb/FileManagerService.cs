using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using TestEngineer.Adb.Parsing;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Files;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb;

/// <summary>文件管理：ls 解析、批量 test -r/-w 探测、push/pull、rm/mv/cp/mkdir 与 zip 证据包。</summary>
public sealed partial class FileManagerService : IFileManagerService
{
    private const int ProbeChunkSize = 40;
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(30);

    /// <summary>禁止删除/覆盖的关键路径。</summary>
    private static readonly HashSet<string> ProtectedPaths = new(StringComparer.Ordinal)
    {
        "/", "/sdcard", "/storage", "/storage/emulated", "/storage/emulated/0", "/storage/self", "/storage/self/primary",
        "/data", "/system", "/vendor", "/product", "/proc", "/sys", "/dev", "/mnt", "/etc", "/cache", "/apex",
    };

    private readonly IAdbProcessRunner _runner;

    public FileManagerService(IAdbProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    [GeneratedRegex(@"^\[\s*(?<p>\d+)%\]", RegexOptions.CultureInvariant)]
    private static partial Regex ProgressRegex();

    public async Task<IReadOnlyList<FileEntry>> ListAsync(string serial, string directory, bool probeAccess = true, CancellationToken cancellationToken = default)
    {
        var dir = RemotePath.Normalize(directory);
        // 末尾加 '/' 使 ls 跟随目录符号链接（如 /sdcard）。
        var listTarget = dir == RemotePath.Root ? "/" : dir + "/";
        var result = await _runner.ShellAsync(serial, $"ls -lA {ShellQuote.Quote(listTarget)}", ShortTimeout, cancellationToken).ConfigureAwait(false);

        var entries = LsOutputParser.Parse(result.StandardOutput, dir);
        if (entries.Count == 0 && !result.Succeeded)
        {
            throw new DeviceCommandException($"无法列出 {dir}：{result.FailureMessage}", result);
        }

        var sorted = Sort(entries);
        return probeAccess ? await ProbeAccessAsync(serial, sorted, cancellationToken).ConfigureAwait(false) : sorted;
    }

    public async Task<IReadOnlyList<FileEntry>> ProbeAccessAsync(string serial, IReadOnlyList<FileEntry> entries, CancellationToken cancellationToken = default)
    {
        var probed = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
        foreach (var chunk in entries.Chunk(ProbeChunkSize))
        {
            var quoted = string.Join(' ', chunk.Select(e => ShellQuote.Quote(e.AbsolutePath)));
            // 每行输出 "<r><w><d> <path>"，r/w/d 分别为 test -r/-w/-d 的结果。
            var script =
                $"for f in {quoted}; do r=0; w=0; d=0; [ -r \"$f\" ] && r=1; [ -w \"$f\" ] && w=1; [ -d \"$f\" ] && d=1; echo \"$r$w$d $f\"; done";
            var result = await _runner.ShellAsync(serial, script, ShortTimeout, cancellationToken).ConfigureAwait(false);
            foreach (var line in result.StandardOutput.Split('\n'))
            {
                var text = line.TrimEnd('\r');
                if (text.Length < 5 || text[3] != ' ')
                {
                    continue;
                }

                var path = text[4..];
                var source = chunk.FirstOrDefault(e => e.AbsolutePath == path);
                if (source is null)
                {
                    continue;
                }

                probed[path] = source with
                {
                    CanRead = text[0] == '1',
                    CanWrite = text[1] == '1',
                    IsDirectory = source.IsSymlink ? text[2] == '1' : source.IsDirectory,
                };
            }
        }

        return [.. entries.Select(e => probed.GetValueOrDefault(e.AbsolutePath, e))];
    }

    public async Task<AdbCommandResult> PullAsync(string serial, string remotePath, string localPath, IProgress<RunningTaskState>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        var remote = RemotePath.Normalize(remotePath);
        var localDirectory = Directory.Exists(localPath) ? localPath : Path.GetDirectoryName(Path.GetFullPath(localPath));
        if (!string.IsNullOrEmpty(localDirectory))
        {
            Directory.CreateDirectory(localDirectory);
        }

        if (await IsRemoteDirectoryAsync(serial, remote, cancellationToken).ConfigureAwait(false))
        {
            var localRoot = Directory.Exists(localPath)
                ? Path.Combine(localPath, RemotePath.GetName(remote))
                : localPath;
            var directoryState = RunningTaskState.Begin($"拉取 {RemotePath.GetName(remote)}");
            progress?.Report(directoryState);
            return await PullDirectoryAsync(serial, remote, localRoot, progress, directoryState, cancellationToken).ConfigureAwait(false);
        }

        var localTarget = Directory.Exists(localPath)
            ? Path.Combine(localPath, RemotePath.GetName(remote))
            : localPath;
        var state = RunningTaskState.Begin($"拉取 {RemotePath.GetName(remote)}");
        progress?.Report(state);
        var result = await PullOneFileAsync(serial, remote, localTarget, progress, state, cancellationToken).ConfigureAwait(false);
        progress?.Report(state.Finish(Status(result), result.Succeeded ? null : result.FailureMessage));
        return result;
    }

    public async Task<AdbCommandResult> PushAsync(string serial, string localPath, string remoteDirectory, IProgress<RunningTaskState>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        if (!File.Exists(localPath) && !Directory.Exists(localPath))
        {
            return AdbCommandResult.Failure($"本地路径不存在：{localPath}");
        }

        var remote = RemotePath.Normalize(remoteDirectory);
        EnsureNotProtectedForWrite(remote, allowExactDirectory: true);

        var localName = Path.GetFileName(localPath.TrimEnd('\\', '/'));
        var state = RunningTaskState.Begin($"推送 {localName}");
        progress?.Report(state);

        if (Directory.Exists(localPath))
        {
            return await PushDirectoryOneShotAsync(serial, localPath, remote, localName, progress, state, cancellationToken).ConfigureAwait(false);
        }

        // 显式给出完整远端路径。若只推到目录/，adb 37 会截断中文文件名（如「样本.txt」变成「样本.」）。
        var remoteTarget = RemotePath.Combine(remote, localName);
        var result = await _runner.RunAsync(
            ["push", localPath, remoteTarget],
            serial,
            Timeout.InfiniteTimeSpan,
            onStdout: line => ReportTransferProgress(progress, ref state, line),
            onStderr: line => ReportTransferProgress(progress, ref state, line),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        progress?.Report(state.Finish(Status(result), result.Succeeded ? null : result.FailureMessage));
        return result;
    }

    public Task<AdbCommandResult> DeleteAsync(string serial, string remotePath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        var path = RemotePath.Normalize(remotePath);
        EnsureNotProtectedForWrite(path);
        var flags = recursive ? "-rf" : "-f";
        var timeout = recursive ? TimeSpan.FromMinutes(10) : ShortTimeout;
        return _runner.ShellAsync(serial, $"rm {flags} {ShellQuote.Quote(path)}", timeout, cancellationToken);
    }

    public Task<AdbCommandResult> MoveAsync(string serial, string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var source = RemotePath.Normalize(sourcePath);
        var destination = RemotePath.Normalize(destinationPath);
        EnsureNotProtectedForWrite(source);
        EnsureNotProtectedForWrite(destination, allowExactDirectory: true);
        return _runner.ShellAsync(serial, $"mv -f {ShellQuote.Quote(source)} {ShellQuote.Quote(destination)}", ShortTimeout, cancellationToken);
    }

    public Task<AdbCommandResult> CopyAsync(string serial, string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var source = RemotePath.Normalize(sourcePath);
        var destination = RemotePath.Normalize(destinationPath);
        EnsureNotProtectedForWrite(destination, allowExactDirectory: true);
        return _runner.ShellAsync(serial, $"cp -r {ShellQuote.Quote(source)} {ShellQuote.Quote(destination)}", TimeSpan.FromMinutes(5), cancellationToken);
    }

    public Task<AdbCommandResult> MakeDirectoryAsync(string serial, string remoteDirectory, CancellationToken cancellationToken = default)
    {
        var directory = RemotePath.Normalize(remoteDirectory);
        return _runner.ShellAsync(serial, $"mkdir -p {ShellQuote.Quote(directory)}", ShortTimeout, cancellationToken);
    }

    public async Task<AdbCommandResult> PullToEvidenceZipAsync(string serial, IReadOnlyList<string> remotePaths, string zipPath, IProgress<RunningTaskState>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remotePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        if (remotePaths.Count == 0)
        {
            return AdbCommandResult.Failure("没有选择任何文件。");
        }

        var staging = Path.Combine(Path.GetTempPath(), "TestEngineer", "evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var state = RunningTaskState.Begin("生成证据包");
        progress?.Report(state);
        var manifest = new StringBuilder();
        manifest.AppendLine($"TestEngineer 证据包");
        manifest.AppendLine($"设备序列号: {serial}");
        manifest.AppendLine($"生成时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:sszzz}");
        manifest.AppendLine();

        var failures = new List<string>();
        try
        {
            for (var i = 0; i < remotePaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remote = RemotePath.Normalize(remotePaths[i]);
                var target = Path.Combine(staging, $"{i + 1:D3}_{SanitizeFileName(RemotePath.GetName(remote))}");
                Directory.CreateDirectory(target);
                progress?.Report(state.Update((double)i / remotePaths.Count, remote));
                var pulled = await PullAsync(serial, remote, target, null, cancellationToken).ConfigureAwait(false);
                manifest.AppendLine($"{(pulled.Succeeded ? "OK  " : "FAIL")} {remote}");
                if (!pulled.Succeeded)
                {
                    failures.Add($"{remote}: {pulled.FailureMessage}");
                    manifest.AppendLine($"     {pulled.FailureMessage.ReplaceLineEndings(" ")}");
                }
            }

            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.txt"), manifest.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            ZipPackHelper.CreateFromDirectory(staging, zipPath);
        }
        catch (OperationCanceledException)
        {
            progress?.Report(state.Finish(RunningTaskStatus.Cancelled));
            throw;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }

        var summary = failures.Count == 0
            ? $"已生成 {zipPath}（{remotePaths.Count} 项）"
            : $"已生成 {zipPath}，其中 {failures.Count} 项失败";
        progress?.Report(state.Finish(failures.Count == 0 ? RunningTaskStatus.Succeeded : RunningTaskStatus.Failed, summary));
        return new AdbCommandResult(failures.Count == 0 ? 0 : 1, summary, string.Join(Environment.NewLine, failures));
    }

    private async Task<AdbCommandResult> PullDirectoryAsync(
        string serial,
        string remoteDirectory,
        string localRoot,
        IProgress<RunningTaskState>? progress,
        RunningTaskState state,
        CancellationToken cancellationToken)
    {
        var pullRemote = remoteDirectory;
        string? deviceStage = null;
        if (ContainsNonAscii(remoteDirectory))
        {
            deviceStage = "/data/local/tmp/te-pull-" + Guid.NewGuid().ToString("N");
            var copied = await _runner.ShellAsync(
                serial,
                $"mkdir -p {ShellQuote.Quote(deviceStage)} && cp -a {ShellQuote.Quote(remoteDirectory)} {ShellQuote.Quote(deviceStage + "/dir")}",
                TimeSpan.FromMinutes(10),
                cancellationToken).ConfigureAwait(false);
            if (!copied.Succeeded)
            {
                progress?.Report(state.Finish(Status(copied), copied.FailureMessage));
                return copied;
            }

            pullRemote = deviceStage + "/dir";
        }

        var pullTarget = localRoot;
        string? stageDirectory = null;
        if (ContainsNonAscii(localRoot))
        {
            stageDirectory = Path.Combine(AsciiStageRoot(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stageDirectory);
            pullTarget = Path.Combine(stageDirectory, "dir");
        }
        else
        {
            var parent = Path.GetDirectoryName(localRoot);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }
        }

        try
        {
            var transferred = await TransferFileAsync(
                ["pull", pullRemote, pullTarget],
                serial,
                progress,
                state,
                cancellationToken).ConfigureAwait(false);
            if (!transferred.Succeeded)
            {
                if (IsAdbPathLimitFailure(transferred.FailureMessage))
                {
                    TryDeleteDirectory(localRoot);
                    return await PullDirectoryViaTarAsync(serial, remoteDirectory, localRoot, progress, state, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(state.Finish(Status(transferred), transferred.FailureMessage));
                return transferred;
            }

            if (stageDirectory is null)
            {
                progress?.Report(state.Finish(RunningTaskStatus.Succeeded));
                return transferred;
            }

            if (!Directory.Exists(pullTarget) && !File.Exists(pullTarget))
            {
                return AdbCommandResult.Failure($"已从设备读取，但没有得到本地目录：{localRoot}");
            }

            try
            {
                if (Directory.Exists(pullTarget))
                {
                    MoveDirectory(pullTarget, localRoot);
                }
                else
                {
                    MoveReplacing(pullTarget, localRoot);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var failed = AdbCommandResult.Failure($"无法写入 {localRoot}：{ex.Message}");
                progress?.Report(state.Finish(RunningTaskStatus.Failed, failed.FailureMessage));
                return failed;
            }

            progress?.Report(state.Finish(RunningTaskStatus.Succeeded));
            return transferred;
        }
        finally
        {
            if (stageDirectory is not null)
            {
                TryDeleteDirectory(stageDirectory);
            }

            if (deviceStage is not null)
            {
                await DeleteDeviceStageAsync(serial, deviceStage).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 整个文件夹一次 push。远端路径含非 ASCII 时 adb 会在算路径长度时崩溃，
    /// 先推到 /data/local/tmp 的英文目录，再由设备端挪到目标位置。
    /// </summary>
    private async Task<AdbCommandResult> PushDirectoryOneShotAsync(
        string serial,
        string localDirectory,
        string remoteDirectory,
        string folderName,
        IProgress<RunningTaskState>? progress,
        RunningTaskState state,
        CancellationToken cancellationToken)
    {
        var remoteRoot = RemotePath.Combine(remoteDirectory, folderName);
        var pushTarget = remoteRoot;
        string? deviceStage = null;
        if (ContainsNonAscii(remoteRoot))
        {
            deviceStage = "/data/local/tmp/te-push-" + Guid.NewGuid().ToString("N");
            pushTarget = deviceStage;
        }

        var source = localDirectory;
        string? junction = null;
        if (LocalTreeNeedsShortPath(localDirectory, out var tooLongForJunction))
        {
            if (tooLongForJunction)
            {
                return await PushDirectoryViaTarAsync(serial, localDirectory, remoteDirectory, folderName, progress, state, cancellationToken).ConfigureAwait(false);
            }

            junction = TryCreateShortJunction(localDirectory);
            if (junction is null)
            {
                return await PushDirectoryViaTarAsync(serial, localDirectory, remoteDirectory, folderName, progress, state, cancellationToken).ConfigureAwait(false);
            }

            source = junction;
        }

        try
        {
            var pushed = await TransferFileAsync(
                ["push", source, pushTarget],
                serial,
                progress,
                state,
                cancellationToken).ConfigureAwait(false);
            if (!pushed.Succeeded || deviceStage is null)
            {
                if (!pushed.Succeeded)
                {
                    await DeleteDeviceStageAsync(serial, deviceStage).ConfigureAwait(false);
                }

                progress?.Report(state.Finish(Status(pushed), pushed.Succeeded ? null : pushed.FailureMessage));
                return pushed;
            }

            var parent = remoteRoot[..^folderName.Length].TrimEnd('/');
            if (parent.Length == 0)
            {
                parent = "/";
            }

            var placed = await _runner.ShellAsync(
                serial,
                $"mkdir -p {ShellQuote.Quote(parent)} && rm -rf {ShellQuote.Quote(remoteRoot)} && cp -a {ShellQuote.Quote(deviceStage)} {ShellQuote.Quote(remoteRoot)} && rm -rf {ShellQuote.Quote(deviceStage)}",
                TimeSpan.FromMinutes(10),
                cancellationToken).ConfigureAwait(false);
            if (!placed.Succeeded)
            {
                await DeleteDeviceStageAsync(serial, deviceStage).ConfigureAwait(false);
            }

            progress?.Report(state.Finish(Status(placed), placed.Succeeded ? null : placed.FailureMessage));
            return placed;
        }
        finally
        {
            if (junction is not null)
            {
                DeleteJunction(junction);
            }
        }
    }

    /// <summary>
    /// 路径长过 adb 能 lstat 的限度时，打成一个 tar 再推一次，避免按文件拆开。
    /// </summary>
    private async Task<AdbCommandResult> PushDirectoryViaTarAsync(
        string serial,
        string localDirectory,
        string remoteDirectory,
        string folderName,
        IProgress<RunningTaskState>? progress,
        RunningTaskState state,
        CancellationToken cancellationToken)
    {
        var tarExe = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!File.Exists(tarExe))
        {
            var missing = AdbCommandResult.Failure("路径过长，且系统没有 tar.exe，无法整夹推送。");
            progress?.Report(state.Finish(RunningTaskStatus.Failed, missing.FailureMessage));
            return missing;
        }

        var archive = Path.Combine(Path.GetTempPath(), "te-push-" + Guid.NewGuid().ToString("N") + ".tar");
        var remoteTar = "/data/local/tmp/" + Path.GetFileName(archive);
        var parent = Path.GetDirectoryName(localDirectory.TrimEnd('\\', '/')) ?? localDirectory;
        try
        {
            progress?.Report(state.Update(0, "正在打包 " + folderName));
            var packed = await RunLocalAsync(tarExe, ["-cf", archive, "-C", parent, folderName], cancellationToken).ConfigureAwait(false);
            if (!packed.Succeeded)
            {
                progress?.Report(state.Finish(RunningTaskStatus.Failed, packed.FailureMessage));
                return packed;
            }

            var pushed = await TransferFileAsync(
                ["push", archive, remoteTar],
                serial,
                progress,
                state,
                cancellationToken).ConfigureAwait(false);
            if (!pushed.Succeeded)
            {
                progress?.Report(state.Finish(Status(pushed), pushed.FailureMessage));
                return pushed;
            }

            var placed = await _runner.ShellAsync(
                serial,
                $"mkdir -p {ShellQuote.Quote(remoteDirectory)} && rm -rf {ShellQuote.Quote(RemotePath.Combine(remoteDirectory, folderName))} && tar -xf {ShellQuote.Quote(remoteTar)} -C {ShellQuote.Quote(remoteDirectory)} && rm -f {ShellQuote.Quote(remoteTar)}",
                TimeSpan.FromMinutes(10),
                cancellationToken).ConfigureAwait(false);
            if (!placed.Succeeded)
            {
                await _runner.ShellAsync(serial, "rm -f " + ShellQuote.Quote(remoteTar), TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
            }

            progress?.Report(state.Finish(Status(placed), placed.Succeeded ? null : placed.FailureMessage));
            return placed;
        }
        finally
        {
            TryDeleteFile(archive);
        }
    }

    private async Task DeleteDeviceStageAsync(string serial, string? deviceStage)
    {
        if (string.IsNullOrEmpty(deviceStage))
        {
            return;
        }

        try
        {
            await _runner.ShellAsync(serial, "rm -rf " + ShellQuote.Quote(deviceStage), TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 临时目录清不掉不影响结果。
        }
    }

    private async Task<AdbCommandResult> PullDirectoryViaTarAsync(
        string serial,
        string remoteDirectory,
        string localRoot,
        IProgress<RunningTaskState>? progress,
        RunningTaskState state,
        CancellationToken cancellationToken)
    {
        var tarExe = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!File.Exists(tarExe))
        {
            var missing = AdbCommandResult.Failure("本地路径过长，且系统没有 tar.exe，无法整夹导出。");
            progress?.Report(state.Finish(RunningTaskStatus.Failed, missing.FailureMessage));
            return missing;
        }

        var name = RemotePath.GetName(remoteDirectory);
        var parent = remoteDirectory[..^name.Length].TrimEnd('/');
        if (parent.Length == 0)
        {
            parent = "/";
        }

        var remoteTar = "/data/local/tmp/te-pull-" + Guid.NewGuid().ToString("N") + ".tar";
        var localTar = Path.Combine(Path.GetTempPath(), Path.GetFileName(remoteTar));
        var destParent = Path.GetDirectoryName(localRoot);
        try
        {
            progress?.Report(state.Update(0, "正在打包 " + name));
            var packed = await _runner.ShellAsync(
                serial,
                $"tar -cf {ShellQuote.Quote(remoteTar)} -C {ShellQuote.Quote(parent)} {ShellQuote.Quote(name)}",
                TimeSpan.FromMinutes(10),
                cancellationToken).ConfigureAwait(false);
            if (!packed.Succeeded)
            {
                progress?.Report(state.Finish(Status(packed), packed.FailureMessage));
                return packed;
            }

            var pulled = await TransferFileAsync(
                ["pull", remoteTar, localTar],
                serial,
                progress,
                state,
                cancellationToken).ConfigureAwait(false);
            if (!pulled.Succeeded)
            {
                progress?.Report(state.Finish(Status(pulled), pulled.FailureMessage));
                return pulled;
            }

            if (!string.IsNullOrEmpty(destParent))
            {
                Directory.CreateDirectory(destParent);
            }

            progress?.Report(state.Update(null, "正在解包 " + name));
            var extracted = await RunLocalAsync(tarExe, ["-xf", localTar, "-C", destParent ?? "."], cancellationToken).ConfigureAwait(false);
            progress?.Report(state.Finish(Status(extracted), extracted.Succeeded ? null : extracted.FailureMessage));
            return extracted;
        }
        finally
        {
            TryDeleteFile(localTar);
            await _runner.ShellAsync(serial, "rm -f " + ShellQuote.Quote(remoteTar), TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static bool IsAdbPathLimitFailure(string message) =>
        message.Contains("cannot lstat", StringComparison.OrdinalIgnoreCase)
        || message.Contains("cannot create", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Not a directory", StringComparison.OrdinalIgnoreCase)
        || message.Contains("File name too long", StringComparison.OrdinalIgnoreCase);

    private static bool LocalTreeNeedsShortPath(string directory, out bool tooLongForJunction)
    {
        tooLongForJunction = false;
        var root = Path.GetFullPath(directory).TrimEnd('\\');
        var rootLen = root.Length + 1;
        var max = rootLen;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(@"\\?\" + root, "*", SearchOption.AllDirectories))
            {
                var logical = entry.StartsWith(@"\\?\", StringComparison.Ordinal) ? entry[4..] : entry;
                if (logical.Length > max)
                {
                    max = logical.Length;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            tooLongForJunction = true;
            return true;
        }

        var relative = Math.Max(0, max - rootLen);
        tooLongForJunction = relative + 16 >= 259;
        return max >= 250;
    }

    private static string? TryCreateShortJunction(string target)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
            {
                continue;
            }

            var link = Path.Combine(drive.RootDirectory.FullName, "te" + Guid.NewGuid().ToString("N")[..4]);
            if (File.Exists(link) || Directory.Exists(link))
            {
                continue;
            }

            if (RunAndWait("cmd.exe", ["/c", "mklink", "/J", link, target]) == 0 && Directory.Exists(link))
            {
                return link;
            }
        }

        return null;
    }

    private static void DeleteJunction(string link) => _ = RunAndWait("cmd.exe", ["/c", "rmdir", link]);

    private static int RunAndWait(string file, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        process?.WaitForExit(10000);
        return process?.ExitCode ?? -1;
    }

    private static async Task<AdbCommandResult> RunLocalAsync(string file, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return AdbCommandResult.Failure("无法启动 " + file);
        }

        await using var _ = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 进程已退出。
            }
        });

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0
            ? new AdbCommandResult(0, stdout, stderr)
            : new AdbCommandResult(process.ExitCode, stdout, string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时文件清不掉不影响结果。
        }
    }

    /// <summary>
    /// 拉取单个文件。路径里只要有非 ASCII 目录名，adb 37 在 Windows 上就会报
    /// cannot create file/directory / No such file or directory，即使目录已经建好。
    /// 先落到纯 ASCII 临时文件，再由本机 API 挪到目标位置。
    /// </summary>
    private async Task<AdbCommandResult> PullOneFileAsync(
        string serial,
        string remoteFile,
        string localFile,
        IProgress<RunningTaskState>? progress,
        RunningTaskState state,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(localFile);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var pullTarget = localFile;
        string? stageDirectory = null;
        if (ContainsNonAscii(localFile))
        {
            stageDirectory = Path.Combine(AsciiStageRoot(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stageDirectory);
            pullTarget = Path.Combine(stageDirectory, "file");
        }

        try
        {
            var transferred = await TransferFileAsync(
                ["pull", remoteFile, pullTarget],
                serial,
                progress,
                state,
                cancellationToken).ConfigureAwait(false);
            if (!transferred.Succeeded || stageDirectory is null)
            {
                return transferred;
            }

            if (!File.Exists(pullTarget))
            {
                return AdbCommandResult.Failure($"已从设备读取，但没有得到本地文件：{localFile}");
            }

            try
            {
                MoveReplacing(pullTarget, localFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return AdbCommandResult.Failure($"无法写入 {localFile}：{ex.Message}");
            }

            return transferred;
        }
        finally
        {
            if (stageDirectory is not null)
            {
                TryDeleteDirectory(stageDirectory);
            }
        }
    }

    private static string AsciiStageRoot()
    {
        var temp = Path.Combine(Path.GetTempPath(), "TestEngineer", "pull");
        if (!ContainsNonAscii(temp))
        {
            return temp;
        }

        var system = Environment.GetEnvironmentVariable("SystemDrive");
        if (string.IsNullOrWhiteSpace(system) || ContainsNonAscii(system))
        {
            system = "C:";
        }

        return Path.Combine(system + "\\", "ProgramData", "TestEngineer", "pull");
    }

    private static bool ContainsNonAscii(string path)
    {
        foreach (var ch in path)
        {
            if (ch > 127)
            {
                return true;
            }
        }

        return false;
    }

    private static void MoveReplacing(string source, string destination)
    {
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        try
        {
            File.Move(source, destination);
        }
        catch (IOException) when (!File.Exists(destination))
        {
            File.Copy(source, destination, overwrite: true);
            File.Delete(source);
        }
    }

    private static void MoveDirectory(string source, string destination)
    {
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (!Directory.Exists(destination))
        {
            try
            {
                Directory.Move(source, destination);
                return;
            }
            catch (IOException) when (!Directory.Exists(destination))
            {
                // 跨盘符时 Move 失败，改为复制。
            }
        }

        CopyDirectory(source, destination);
        Directory.Delete(source, recursive: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private async Task<AdbCommandResult> TransferFileAsync(
        IReadOnlyList<string> arguments,
        string serial,
        IProgress<RunningTaskState>? progress,
        RunningTaskState state,
        CancellationToken cancellationToken)
    {
        return await _runner.RunAsync(
            arguments,
            serial,
            Timeout.InfiniteTimeSpan,
            onStdout: line => ReportTransferProgress(progress, ref state, line),
            onStderr: line => ReportTransferProgress(progress, ref state, line),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsRemoteDirectoryAsync(string serial, string remotePath, CancellationToken cancellationToken)
    {
        var result = await _runner.ShellAsync(serial, $"[ -d {ShellQuote.Quote(remotePath)} ] && echo dir || echo notdir", ShortTimeout, cancellationToken).ConfigureAwait(false);
        // 探测失败时按目录处理（保持 adb 原有行为）。
        return !result.Succeeded || result.StandardOutput.Trim() != "notdir";
    }

    private static IReadOnlyList<FileEntry> Sort(IReadOnlyList<FileEntry> entries) =>
        [.. entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)];

    private static RunningTaskStatus Status(AdbCommandResult result) =>
        result.Cancelled ? RunningTaskStatus.Cancelled : result.Succeeded ? RunningTaskStatus.Succeeded : RunningTaskStatus.Failed;

    private static void ReportTransferProgress(IProgress<RunningTaskState>? progress, ref RunningTaskState state, string line)
    {
        if (progress is null)
        {
            return;
        }

        var match = ProgressRegex().Match(line);
        double? fraction = match.Success && int.TryParse(match.Groups["p"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var percent)
            ? percent / 100d
            : null;
        state = state.Update(fraction ?? state.Progress, line);
        progress.Report(state);
    }

    private static void EnsureNotProtectedForWrite(string path, bool allowExactDirectory = false)
    {
        if (!allowExactDirectory && ProtectedPaths.Contains(path))
        {
            throw new InvalidOperationException($"拒绝对受保护路径执行写操作：{path}");
        }

        if (allowExactDirectory && path == RemotePath.Root)
        {
            return;
        }

        // 系统分区之下的内容同样不允许改动（user 版本本就只读，这里提前给出明确提示）。
        foreach (var system in new[] { "/system", "/vendor", "/product", "/proc", "/sys", "/dev", "/apex" })
        {
            if (path == system || RemotePath.IsUnder(path, system))
            {
                throw new InvalidOperationException($"拒绝对系统路径执行写操作：{path}");
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string([.. name.Select(c => invalid.Contains(c) ? '_' : c)]);
        return string.IsNullOrWhiteSpace(sanitized) || sanitized == "/" ? "root" : sanitized;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时目录清理失败不影响结果。
        }
    }
}

/// <summary>zip 打包辅助。</summary>
public static class ZipPackHelper
{
    /// <summary>把目录打成 zip（覆盖已有文件，文件名使用 UTF-8 以保留中文）。</summary>
    public static void CreateFromDirectory(string sourceDirectory, string zipPath)
    {
        var full = Path.GetFullPath(zipPath);
        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (File.Exists(full))
        {
            File.Delete(full);
        }

        ZipFile.CreateFromDirectory(sourceDirectory, full, CompressionLevel.Optimal, includeBaseDirectory: false, Encoding.UTF8);
    }

    /// <summary>把若干文本条目写入 zip。</summary>
    public static void CreateFromTextEntries(string zipPath, IEnumerable<KeyValuePair<string, string>> entries)
    {
        var full = Path.GetFullPath(zipPath);
        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using var stream = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}
