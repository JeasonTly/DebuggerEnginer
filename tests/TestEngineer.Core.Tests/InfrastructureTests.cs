using System.Text.Json;
using TestEngineer.Core.Settings;
using TestEngineer.Infrastructure.History;
using TestEngineer.Infrastructure.Logging;
using TestEngineer.Infrastructure.Paths;
using TestEngineer.Infrastructure.Settings;
using TestEngineer.Infrastructure.Tools;

namespace TestEngineer.Core.Tests;

/// <summary>每个测试一个独立临时目录，结束后清理。</summary>
public abstract class TempDirectoryTestBase : IDisposable
{
    protected TempDirectoryTestBase()
    {
        Root = Path.Combine(Path.GetTempPath(), "TestEngineer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    protected string Root { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响结果。
        }
    }
}

public sealed class SettingsRoundtripTests : TempDirectoryTestBase
{
    private string SettingsPath => Path.Combine(Root, "cfg", "settings.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = new JsonSettingsStore(SettingsPath).Load();

        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Equal(AppThemeMode.System, settings.Theme);
        Assert.False(settings.NavCollapsed);
        Assert.Equal(1024, settings.MirrorWidth);
        Assert.Equal(500, settings.ConsoleHistoryLimit);
        Assert.NotNull(settings.ToolPaths);
        Assert.Null(settings.ToolPaths.Adb);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var store = new JsonSettingsStore(SettingsPath);
        var original = new AppSettings
        {
            Theme = AppThemeMode.Dark,
            NavCollapsed = true,
            MirrorWidth = 720,
            ConsoleHistoryLimit = 123,
            LastDeviceSerial = "99990000888800",
            ToolPaths = new ToolPathSettings { Adb = @"D:\tools\adb.exe", Scrcpy = @"D:\tools\测试 目录\scrcpy.exe" },
        };

        store.Save(original);
        var loaded = new JsonSettingsStore(SettingsPath).Load();

        Assert.Equal(AppSettings.CurrentVersion, loaded.Version);
        Assert.Equal(AppThemeMode.Dark, loaded.Theme);
        Assert.True(loaded.NavCollapsed);
        Assert.Equal(720, loaded.MirrorWidth);
        Assert.Equal(123, loaded.ConsoleHistoryLimit);
        Assert.Equal("99990000888800", loaded.LastDeviceSerial);
        Assert.Equal(@"D:\tools\adb.exe", loaded.ToolPaths.Adb);
        Assert.Equal(@"D:\tools\测试 目录\scrcpy.exe", loaded.ToolPaths.Scrcpy);
    }

    [Fact]
    public void Save_WritesVersionAndReadableEnumNames()
    {
        new JsonSettingsStore(SettingsPath).Save(new AppSettings { Theme = AppThemeMode.Light });

        using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("light", document.RootElement.GetProperty("theme").GetString());
        Assert.True(document.RootElement.TryGetProperty("toolPaths", out _));
    }

    [Fact]
    public void Save_IsAtomic_LeavesNoTempFilesAndOverwritesExisting()
    {
        var store = new JsonSettingsStore(SettingsPath);

        store.Save(new AppSettings { MirrorWidth = 100 });
        store.Save(new AppSettings { MirrorWidth = 200 });

        Assert.Equal(200, store.Load().MirrorWidth);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.tmp"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!));
    }

    [Fact]
    public void Load_CorruptFile_BacksUpAndFallsBackToDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ this is not json");

        var settings = new JsonSettingsStore(SettingsPath).Load();

        Assert.Equal(AppThemeMode.System, settings.Theme);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "settings.json.corrupt-*"));
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Load_TolerantOfMissingFieldsAndNullToolPaths()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "theme": "dark", "toolPaths": null }""");

        var settings = new JsonSettingsStore(SettingsPath).Load();

        Assert.Equal(AppThemeMode.Dark, settings.Theme);
        Assert.NotNull(settings.ToolPaths);
        Assert.Equal(1024, settings.MirrorWidth);
        Assert.Equal(1, settings.Version);
    }

    [Fact]
    public void AppSettingsService_SavePersistsAndRaisesChanged()
    {
        var service = new AppSettingsService(new JsonSettingsStore(SettingsPath));
        var raised = 0;
        service.Changed += (_, _) => raised++;

        service.Current.NavCollapsed = true;
        service.Save();

        Assert.Equal(1, raised);
        Assert.True(new AppSettingsService(new JsonSettingsStore(SettingsPath)).Current.NavCollapsed);
    }
}

public sealed class AppPathsTests : TempDirectoryTestBase
{
    [Fact]
    public void InstalledMode_UsesLocalAppDataVendorApp()
    {
        var exeDir = Directory.CreateDirectory(Path.Combine(Root, "exe")).FullName;
        var localAppData = Path.Combine(Root, "local");

        var paths = new AppPaths(exeDir, localAppData, Path.Combine(Root, "tmp"));

        Assert.Equal(AppDataMode.Installed, paths.Mode);
        Assert.Equal(Path.Combine(localAppData, "TestEngineer", "TestEngineer"), paths.DataDirectory);
        Assert.Equal(Path.Combine(paths.DataDirectory, "settings.json"), paths.SettingsFilePath);
    }

    [Fact]
    public void PortableFlag_SwitchesToProgramDataDirectory()
    {
        var exeDir = Directory.CreateDirectory(Path.Combine(Root, "exe")).FullName;
        File.WriteAllText(Path.Combine(exeDir, "portable.flag"), string.Empty);

        var paths = new AppPaths(exeDir, Path.Combine(Root, "local"), Path.Combine(Root, "tmp"));

        Assert.True(paths.IsPortable);
        Assert.Equal(Path.Combine(exeDir, "Data"), paths.DataDirectory);
        Assert.Equal(Path.Combine(exeDir, "Data", "Config", "settings.json"), paths.SettingsFilePath);
        Assert.Equal(Path.Combine(exeDir, "Data", "Logs"), paths.LogsDirectory);
    }

    [Fact]
    public void EnsureDirectories_CreatesWritableFolders()
    {
        var paths = new AppPaths(Path.Combine(Root, "exe"), Path.Combine(Root, "local"), Path.Combine(Root, "tmp"));

        paths.EnsureDirectories();

        Assert.True(Directory.Exists(paths.ConfigDirectory));
        Assert.True(Directory.Exists(paths.LogsDirectory));
    }
}

public sealed class ToolLocatorTests : TempDirectoryTestBase
{
    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    private ToolLocator Locator(ToolPathSettings? overrides = null, IEnumerable<string>? fallback = null, string? path = null, string? sdk = null) =>
        new(
            [Path.Combine(Root, "repo", "bin", "Debug")],
            () => overrides,
            fallback ?? [],
            name => name switch
            {
                "PATH" => path,
                "ANDROID_HOME" => sdk,
                _ => null,
            });

    [Fact]
    public void Adb_PrefersSettingsOverride()
    {
        var custom = Path.Combine(Root, "custom", "adb.exe");
        Touch(custom);
        Touch(Path.Combine(Root, "repo", "tools", "third-party", "platform-tools", "adb.exe"));

        var found = Locator(new ToolPathSettings { Adb = custom }).LocateAdb();

        Assert.Equal(ToolSource.SettingsOverride, found?.Source);
        Assert.Equal(custom, found?.FullPath);
    }

    [Fact]
    public void Adb_StaleOverrideIsIgnored_AndBundledFoundByWalkingUp()
    {
        var bundled = Path.Combine(Root, "repo", "tools", "third-party", "platform-tools", "adb.exe");
        Touch(bundled);

        var found = Locator(new ToolPathSettings { Adb = Path.Combine(Root, "gone", "adb.exe") }).LocateAdb();

        Assert.Equal(ToolSource.Bundled, found?.Source);
        Assert.Equal(bundled, found?.FullPath);
    }

    [Fact]
    public void Adb_OrderIsBundledThenSdkEnvThenFallbackThenPath()
    {
        var pathDir = Path.Combine(Root, "pathdir");
        var fallbackDir = Path.Combine(Root, "fallback");
        var sdk = Path.Combine(Root, "sdk");
        Touch(Path.Combine(pathDir, "adb.exe"));
        Assert.Equal(ToolSource.Path, Locator(path: pathDir).LocateAdb()?.Source);

        Touch(Path.Combine(fallbackDir, "adb.exe"));
        Assert.Equal(ToolSource.Fallback, Locator(fallback: [fallbackDir], path: pathDir).LocateAdb()?.Source);

        Touch(Path.Combine(sdk, "platform-tools", "adb.exe"));
        Assert.Equal(ToolSource.EnvironmentSdk, Locator(fallback: [fallbackDir], path: pathDir, sdk: sdk).LocateAdb()?.Source);

        Touch(Path.Combine(Root, "repo", "tools", "third-party", "platform-tools", "adb.exe"));
        Assert.Equal(ToolSource.Bundled, Locator(fallback: [fallbackDir], path: pathDir, sdk: sdk).LocateAdb()?.Source);
    }

    [Fact]
    public void Scrcpy_FoundInNestedBundledFolder()
    {
        var exe = Path.Combine(Root, "repo", "tools", "third-party", "scrcpy-win64-v3.3.3", "scrcpy-win64-v3.3.3", "scrcpy.exe");
        Touch(exe);

        var found = Locator().LocateScrcpy();

        Assert.Equal(exe, found?.FullPath);
        Assert.Equal(ToolSource.Bundled, found?.Source);
    }

    [Fact]
    public void NothingAvailable_ReturnsNull()
    {
        var locator = Locator();

        Assert.Null(locator.FindAdb());
        Assert.Null(locator.FindScrcpy());
    }
}

public sealed class ConsoleHistoryStoreTests : TempDirectoryTestBase
{
    private string FilePath => Path.Combine(Root, "data", "console-history.txt");

    [Fact]
    public void Add_PersistsAcrossInstances_AndCollapsesAdjacentDuplicates()
    {
        var store = new ConsoleHistoryStore(FilePath);

        store.Add("shell getprop ro.build.type");
        store.Add("shell getprop ro.build.type");
        store.Add("devices -l");
        store.Add("   ");
        store.Add("shell ls /sdcard/相机");

        var reloaded = new ConsoleHistoryStore(FilePath);
        Assert.Equal(["shell getprop ro.build.type", "devices -l", "shell ls /sdcard/相机"], reloaded.Items);
    }

    [Fact]
    public void Limit_DropsOldestAndRewritesFile()
    {
        var limit = 3;
        var store = new ConsoleHistoryStore(FilePath, () => limit);

        for (var i = 1; i <= 5; i++)
        {
            store.Add($"cmd {i}");
        }

        Assert.Equal(["cmd 3", "cmd 4", "cmd 5"], store.Items);
        Assert.Equal(["cmd 3", "cmd 4", "cmd 5"], File.ReadAllLines(FilePath));

        limit = 2; // 下次加载按新上限裁剪
        Assert.Equal(["cmd 4", "cmd 5"], new ConsoleHistoryStore(FilePath, () => limit).Items);
    }

    [Fact]
    public void Clear_EmptiesMemoryAndFile()
    {
        var store = new ConsoleHistoryStore(FilePath);
        store.Add("devices");

        store.Clear();

        Assert.Empty(store.Items);
        Assert.Empty(new ConsoleHistoryStore(FilePath).Items);
    }

    [Fact]
    public void MultilineInput_IsFlattenedToOneEntry()
    {
        var store = new ConsoleHistoryStore(FilePath);

        store.Add("shell echo a\r\nshell echo b");

        Assert.Equal(["shell echo a  shell echo b"], store.Items);
    }
}

public sealed class BoundedLogBufferTests
{
    [Fact]
    public void Append_BeyondCapacity_DropsOldestButCountsTotal()
    {
        var buffer = new BoundedLogBuffer(3);

        for (var i = 1; i <= 5; i++)
        {
            buffer.Append($"line {i}");
        }

        Assert.Equal(["line 3", "line 4", "line 5"], buffer.Snapshot());
        Assert.Equal(3, buffer.Count);
        Assert.Equal(5, buffer.TotalAppended);
    }

    [Fact]
    public void Append_RaisesEventAndClearWorks()
    {
        var buffer = new BoundedLogBuffer(10);
        var seen = new List<string>();
        buffer.LineAppended += seen.Add;

        buffer.Append("a");
        buffer.Clear();

        Assert.Equal(["a"], seen);
        Assert.Empty(buffer.Snapshot());
    }
}
