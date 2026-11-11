namespace TestEngineer.Infrastructure.Paths;

public enum AppDataMode
{
    /// <summary>安装模式：%LocalAppData%/TestEngineer/TestEngineer/。</summary>
    Installed,

    /// <summary>便携模式：程序目录旁存在 portable.flag，数据位于 &lt;程序目录&gt;/Data/。</summary>
    Portable,
}

/// <summary>
/// 解析配置、数据、日志目录。便携模式只以程序目录下的 portable.flag 判定，不做间接猜测。
/// </summary>
public sealed class AppPaths
{
    public const string VendorName = "TestEngineer";
    public const string AppName = "TestEngineer";
    public const string PortableFlagFileName = "portable.flag";
    public const string PortableDataDirectoryName = "Data";
    public const string SettingsFileName = "settings.json";
    public const string ConsoleHistoryFileName = "console-history.txt";

    public AppPaths(string baseDirectory, string localAppDataDirectory, string tempRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRoot);

        ProgramDirectory = Path.GetFullPath(baseDirectory);

        if (File.Exists(Path.Combine(ProgramDirectory, PortableFlagFileName)))
        {
            Mode = AppDataMode.Portable;
            DataDirectory = Path.Combine(ProgramDirectory, PortableDataDirectoryName);
            ConfigDirectory = Path.Combine(DataDirectory, "Config");
            LogsDirectory = Path.Combine(DataDirectory, "Logs");
        }
        else
        {
            Mode = AppDataMode.Installed;
            DataDirectory = Path.Combine(Path.GetFullPath(localAppDataDirectory), VendorName, AppName);
            ConfigDirectory = DataDirectory;
            LogsDirectory = Path.Combine(DataDirectory, "Logs");
        }

        TempDirectory = Path.Combine(Path.GetFullPath(tempRoot), AppName);
        SettingsFilePath = Path.Combine(ConfigDirectory, SettingsFileName);
        ConsoleHistoryFilePath = Path.Combine(DataDirectory, ConsoleHistoryFileName);
    }

    public AppDataMode Mode { get; }

    public bool IsPortable => Mode == AppDataMode.Portable;

    /// <summary>程序（可执行文件）所在目录，只读资源位置。</summary>
    public string ProgramDirectory { get; }

    /// <summary>历史、数据库等用户数据根目录。</summary>
    public string DataDirectory { get; }

    public string ConfigDirectory { get; }

    public string LogsDirectory { get; }

    public string TempDirectory { get; }

    public string SettingsFilePath { get; }

    public string ConsoleHistoryFilePath { get; }

    /// <summary>基于当前进程环境解析路径。</summary>
    public static AppPaths Resolve() => new(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Path.GetTempPath());

    /// <summary>
    /// 创建所需目录并验证可写。失败时抛出带可操作提示的 <see cref="InvalidOperationException"/>。
    /// </summary>
    public void EnsureDirectories()
    {
        foreach (var directory in new[] { ConfigDirectory, DataDirectory, LogsDirectory })
        {
            EnsureWritable(directory);
        }
    }

    private void EnsureWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var hint = IsPortable
                ? "便携模式要求程序目录可写，请将程序解压到可写位置，或删除 portable.flag 改用安装模式。"
                : "请检查该目录的访问权限与磁盘空间。";
            throw new InvalidOperationException($"无法写入目录 \"{directory}\"。{hint}", ex);
        }
    }
}
