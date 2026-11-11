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

        // 同 PushAsync：adb 37 在"拉取到目录"时对含中文的文件名按字符数计长而截断。
        // 拉取单个文件时显式给出完整本地路径规避；远端是目录则无法规避。
        var localTarget = localPath;
        if (Directory.Exists(localPath) && !await IsRemoteDirectoryAsync(serial, remote, cancellationToken).ConfigureAwait(false))
        {
            localTarget = Path.Combine(localPath, RemotePath.GetName(remote));
        }

        var state = RunningTaskState.Begin($"拉取 {RemotePath.GetName(remote)}");
        progress?.Report(state);
        var result = await _runner.RunAsync(
            ["pull", remote, localTarget],
            serial,
            Timeout.InfiniteTimeSpan,
            onStdout: line => ReportTransferProgress(progress, ref state, line),
            onStderr: line => ReportTransferProgress(progress, ref state, line),
            cancellationToken: cancellationToken).ConfigureAwait(false);
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

        // adb 37 在"推送文件到目录/"时按字符数而非字节数计算远端路径长度，含中文的文件名会被截断
        // （如 "样本.txt" 变成 "样本."）。单文件改为显式给出完整远端路径可规避；目录推送无法规避，仅能保持原样。
        var remoteTarget = File.Exists(localPath)
            ? RemotePath.Combine(remote, localName)
            : (remote == RemotePath.Root ? "/" : remote + "/");
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
        return _runner.ShellAsync(serial, $"rm {flags} {ShellQuote.Quote(path)}", ShortTimeout, cancellationToken);
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
