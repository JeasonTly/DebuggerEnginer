using TestEngineer.Core.Models;

namespace TestEngineer.Core.Abstractions;

public interface IDeviceInfoService
{
    /// <summary>采集设备快照；单项失败不会抛出，而是记录到 <see cref="DeviceSnapshot.Warnings"/>。</summary>
    Task<DeviceSnapshot> GetSnapshotAsync(string serial, CancellationToken cancellationToken = default);
}

public interface IAppManagerService
{
    Task<IReadOnlyList<AppPackage>> ListAsync(string serial, AppListFilter filter, CancellationToken cancellationToken = default);

    Task<AppPackageDetail> GetDetailAsync(string serial, string packageName, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> InstallAsync(string serial, string apkPath, bool replace = true, bool grantAllPermissions = false, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> UninstallAsync(string serial, string packageName, bool keepData = false, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> ClearDataAsync(string serial, string packageName, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> ForceStopAsync(string serial, string packageName, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> GrantPermissionAsync(string serial, string packageName, string permission, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> RevokePermissionAsync(string serial, string packageName, string permission, CancellationToken cancellationToken = default);

    /// <summary>设置 appops，如 <c>appops set pkg SYSTEM_ALERT_WINDOW allow</c>。</summary>
    Task<AdbCommandResult> SetAppOpAsync(string serial, string packageName, string operation, string mode, CancellationToken cancellationToken = default);

    /// <summary>通过 <c>monkey -p pkg -c android.intent.category.LAUNCHER 1</c> 启动应用。</summary>
    Task<AdbCommandResult> LaunchAsync(string serial, string packageName, CancellationToken cancellationToken = default);
}

public interface IFileManagerService
{
    /// <summary>列目录。<paramref name="probeAccess"/> 为 true 时批量探测可读/可写并修正符号链接的目录属性。</summary>
    Task<IReadOnlyList<FileEntry>> ListAsync(string serial, string directory, bool probeAccess = true, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileEntry>> ProbeAccessAsync(string serial, IReadOnlyList<FileEntry> entries, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> PullAsync(string serial, string remotePath, string localPath, IProgress<RunningTaskState>? progress = null, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> PushAsync(string serial, string localPath, string remoteDirectory, IProgress<RunningTaskState>? progress = null, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> DeleteAsync(string serial, string remotePath, bool recursive = false, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> MoveAsync(string serial, string sourcePath, string destinationPath, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> CopyAsync(string serial, string sourcePath, string destinationPath, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> MakeDirectoryAsync(string serial, string remoteDirectory, CancellationToken cancellationToken = default);

    /// <summary>把若干设备路径拉取后打成带清单的 zip 证据包。</summary>
    Task<AdbCommandResult> PullToEvidenceZipAsync(string serial, IReadOnlyList<string> remotePaths, string zipPath, IProgress<RunningTaskState>? progress = null, CancellationToken cancellationToken = default);
}

public interface IDebugCommandService
{
    IReadOnlyList<DebugCommandDefinition> Commands { get; }

    /// <summary>控制台快捷指令。</summary>
    IReadOnlyList<ConsoleChipDefinition> ConsoleChips { get; }

    /// <summary>命令是否适用于该设备（检查 WhenPackageExists 等前置条件）。</summary>
    Task<bool> IsAvailableAsync(string serial, DebugCommandDefinition command, CancellationToken cancellationToken = default);

    /// <summary>截屏并拉取到本机 PNG 文件（设备端临时文件会被清理）。</summary>
    Task<AdbCommandResult> CaptureScreenshotAsync(string serial, string localPath, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> RunAsync(string serial, string commandId, IReadOnlyDictionary<string, string>? parameters = null, Action<string>? onOutput = null, CancellationToken cancellationToken = default);

    /// <summary>按时长跑 monkey：以极大事件数启动，到点后在设备端杀掉 monkey 进程。</summary>
    Task<MonkeyRunResult> RunMonkeyAsync(string serial, MonkeyOptions options, IProgress<RunningTaskState>? progress = null, Action<string>? onOutput = null, CancellationToken cancellationToken = default);

    Task<AdbCommandResult> CaptureBugreportAsync(string serial, string outputPath, IProgress<RunningTaskState>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>导出 <c>logcat -d</c> 并压缩为 zip。</summary>
    Task<AdbCommandResult> ExportLogcatZipAsync(string serial, string zipPath, CancellationToken cancellationToken = default);
}

/// <summary>控制台命令完成通知。</summary>
public sealed class ConsoleCommandCompletedEventArgs(string commandLine, string? serial, AdbCommandResult result) : EventArgs
{
    public string CommandLine { get; } = commandLine;

    public string? Serial { get; } = serial;

    public AdbCommandResult Result { get; } = result;
}

public interface IConsoleService
{
    event EventHandler<ConsoleCommandCompletedEventArgs>? CommandCompleted;

    /// <summary>
    /// 执行自由格式的 adb 参数行（可省略开头的 "adb"）。行内无 <c>-s</c> 时绑定到 <paramref name="boundSerial"/>。
    /// </summary>
    Task<AdbCommandResult> ExecuteAsync(string commandLine, string? boundSerial, Action<string>? onOutput = null, CancellationToken cancellationToken = default);
}
