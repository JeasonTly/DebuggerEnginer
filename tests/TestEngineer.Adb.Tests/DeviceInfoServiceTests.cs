using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TestEngineer.Core.Models;
using TestEngineer.Infrastructure.Tools;

namespace TestEngineer.Adb.Tests;

public sealed class DeviceInfoServiceTests
{
    private static FakeAdbRunner RunnerFor(string device)
    {
        string Read(string name) => Fixture.Read($"{device}-{name}.txt");
        return new FakeAdbRunner()
            .When(a => a[0] == "shell" && a[1] == "getprop", _ => Ok(Read("getprop")))
            .WhenShellContains("dumpsys battery", Read("battery"))
            .WhenShellContains("df -k /data", Read("df-data"))
            .WhenShellContains("/proc/meminfo", Read("meminfo"))
            .WhenShellContains("wm size", Read("wm-size"))
            .WhenShellContains("wm density", Read("wm-density"))
            .WhenShellContains("uname", Read("uname"))
            .WhenShellContains("dumpsys connectivity", Read("connectivity"))
            .WhenShellContains("dumpsys wifi", string.Empty, exitCode: 1) // grep 无匹配
            .WhenShellContains("ip addr", Read("ip-addr"))
            .WhenShellContains("/proc/uptime", Read("uptime"))
            .WhenShellContains("/proc/cpuinfo", Fixture.Read("DSJ-cpuinfo.txt"));
    }

    private static AdbCommandResult Ok(string text) => new(0, text, string.Empty);

    [Fact]
    public async Task GetSnapshot_H29T_FromCapturedOutputs()
    {
        var service = new DeviceInfoService(RunnerFor("H29T"), NullLogger<DeviceInfoService>.Instance);

        var snapshot = await service.GetSnapshotAsync("99990000888800");

        Assert.Equal("H29T", snapshot.Manufacturer);
        Assert.Equal("H29T", snapshot.Model);
        Assert.Equal("13", snapshot.AndroidVersion);
        Assert.Equal(33, snapshot.SdkLevel);
        Assert.Equal("user", snapshot.BuildType);
        Assert.False(snapshot.Debuggable);
        Assert.Equal("2023-08-05", snapshot.SecurityPatch);
        Assert.Equal("5.4.210", snapshot.Kernel);
        Assert.Equal("5G_MODEM_V2_23A_W24.02.3|ums9620_modem", snapshot.Baseband);
        Assert.Equal(89, snapshot.BatteryLevel);
        Assert.Equal(720, snapshot.ScreenWidth);
        Assert.Equal(1440, snapshot.ScreenHeight);
        Assert.Equal("720x1440", snapshot.Resolution);
        Assert.Equal(320, snapshot.Density);
        Assert.Equal(235226112L * 1024, snapshot.StorageTotalBytes);
        Assert.True(snapshot.MemoryTotalBytes > 0);
        Assert.Equal(8, snapshot.CpuCores);
        Assert.Equal("arm64-v8a", snapshot.CpuAbi);
        Assert.Equal(177700, snapshot.UptimeSec);
        Assert.Equal("d4:f9:21:6d:66:4e", snapshot.WifiMac);
        Assert.Null(snapshot.WifiSsid);
        Assert.Equal("无连接", snapshot.NetworkType);
    }

    [Fact]
    public async Task GetSnapshot_OneCommandDenied_OnlyRecordsWarning()
    {
        var runner = new FakeAdbRunner()
            .When(a => a[0] == "shell" && a[1] == "getprop", _ => Ok(Fixture.Read("DSJ-getprop.txt")))
            .WhenShellContains("dumpsys battery", string.Empty, exitCode: 1)
            .When(_ => true, _ => new AdbCommandResult(1, string.Empty, "permission denied"));
        var service = new DeviceInfoService(runner);

        var snapshot = await service.GetSnapshotAsync("KKSTBE045026");

        Assert.Equal("Hisense", snapshot.Manufacturer);
        Assert.Equal("DSJ-TOPX1A1", snapshot.Model);
        Assert.Null(snapshot.BatteryLevel);
        Assert.Equal(0, snapshot.ScreenWidth);
        Assert.NotEmpty(snapshot.Warnings);
        Assert.Contains(snapshot.Warnings, w => w.StartsWith("dumpsys battery", StringComparison.Ordinal));
    }

    /// <summary>
    /// 真机自测：设置环境变量 TESTENGINEER_DEVICE_SELFTEST=1 时运行，把 H29T 的设备信息写入 docs/selftest/device-info-H29T.txt。
    /// 未设置时直接返回，保证 CI/无设备环境下不受影响。
    /// </summary>
    [Fact]
    public async Task RealDevice_H29T_WritesSelftestReport()
    {
        if (Environment.GetEnvironmentVariable("TESTENGINEER_DEVICE_SELFTEST") != "1")
        {
            return;
        }

        const string serial = "99990000888800";
        var locator = new ToolLocator();
        var runner = new AdbProcessRunner(locator);
        var tracker = new DeviceTracker(runner);
        var devices = await tracker.RefreshAsync();
        Assert.Contains(devices, d => d.Serial == serial && d.IsOnline);

        var snapshot = await new DeviceInfoService(runner).GetSnapshotAsync(serial);
        Assert.Equal("H29T", snapshot.Model);

        var report = new StringBuilder()
            .AppendLine("TestEngineer DeviceInfoService 真机自测")
            .AppendLine($"时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:sszzz}")
            .AppendLine($"adb: {locator.LocateAdb()?.FullPath} ({locator.LocateAdb()?.Source})")
            .AppendLine($"设备列表: {string.Join("; ", devices.Select(d => $"{d.Serial}[{d.RawState}] model={d.Model} transport_id={d.TransportId}"))}")
            .AppendLine()
            .Append(FormatSnapshot(snapshot))
            .ToString();

        var docs = FindRepoDirectory("docs");
        var target = Path.Combine(docs, "selftest", "device-info-H29T.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, report, new UTF8Encoding(false));
    }

    private static string FormatSnapshot(DeviceSnapshot s)
    {
        static string Bytes(long value) => value <= 0 ? "-" : $"{value / 1024d / 1024d / 1024d:F2} GB";
        var lines = new (string Key, string? Value)[]
        {
            ("序列号", s.Serial),
            ("厂商", s.Manufacturer),
            ("品牌", s.Brand),
            ("型号", s.Model),
            ("Android 版本", $"{s.AndroidVersion} (SDK {s.SdkLevel})"),
            ("构建类型", s.BuildType),
            ("Build ID", s.BuildDisplayId),
            ("ro.debuggable", s.Debuggable?.ToString()),
            ("安全补丁", s.SecurityPatch),
            ("内核", s.Kernel),
            ("基带", s.Baseband),
            ("电量", s.BatteryLevel is null ? null : $"{s.BatteryLevel}% {s.BatteryStatus} {s.PowerSource} {s.BatteryTemperatureC:F1}°C {s.BatteryVoltageMv}mV"),
            ("存储 /data", $"总 {Bytes(s.StorageTotalBytes)} / 已用 {Bytes(s.StorageUsedBytes)} / 可用 {Bytes(s.StorageFreeBytes)}"),
            ("内存", $"总 {Bytes(s.MemoryTotalBytes)} / 可用 {Bytes(s.MemoryAvailableBytes)}"),
            ("CPU", $"{s.CpuModel} x{s.CpuCores} {s.CpuAbi}"),
            ("分辨率", s.Resolution),
            ("密度", s.Density.ToString()),
            ("网络类型", s.NetworkType),
            ("Wi-Fi SSID", s.WifiSsid),
            ("Wi-Fi MAC", s.WifiMac),
            ("IP", s.IpAddress),
            ("开机时长", TimeSpan.FromSeconds(s.UptimeSec).ToString(@"d\.hh\:mm\:ss")),
        };

        var builder = new StringBuilder();
        foreach (var (key, value) in lines)
        {
            builder.AppendLine($"{key,-14}: {(string.IsNullOrEmpty(value) ? "-" : value)}");
        }

        builder.AppendLine();
        builder.AppendLine(s.Warnings.Count == 0 ? "警告: 无" : "警告:");
        foreach (var warning in s.Warnings)
        {
            builder.AppendLine($"  - {warning}");
        }

        return builder.ToString();
    }

    private static string FindRepoDirectory(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TestEngineer.slnx")))
            {
                return Path.Combine(directory.FullName, name);
            }
        }

        throw new InvalidOperationException("未找到仓库根目录（TestEngineer.slnx）。");
    }
}

/// <summary>真机冒烟（TESTENGINEER_DEVICE_SELFTEST=1 时运行，否则直接返回）：文件/应用/控制台服务对两台真机的基本可用性。</summary>
public sealed class RealDeviceSmokeTests
{
    [Theory]
    [InlineData("99990000888800")]
    [InlineData("KKSTBE045026")]
    public async Task Services_WorkAgainstRealDevice(string serial)
    {
        if (Environment.GetEnvironmentVariable("TESTENGINEER_DEVICE_SELFTEST") != "1")
        {
            return;
        }

        var runner = new AdbProcessRunner(new ToolLocator());

        var files = new FileManagerService(runner);
        var sdcard = await files.ListAsync(serial, "/sdcard");
        Assert.Contains(sdcard, e => e.Name == "Download" && e.IsDirectory && e.CanRead && e.CanWrite);

        var root = await files.ListAsync(serial, "/");
        var sdcardLink = Assert.Single(root, e => e.Name == "sdcard");
        Assert.True(sdcardLink.IsSymlink);
        Assert.True(sdcardLink.IsDirectory); // 符号链接经 test -d 修正

        var apps = new AppManagerService(runner);
        var thirdParty = await apps.ListAsync(serial, TestEngineer.Core.Models.AppListFilter.ThirdParty);
        var system = await apps.ListAsync(serial, TestEngineer.Core.Models.AppListFilter.System);
        Assert.NotEmpty(system);
        Assert.All(system, p => Assert.True(p.IsSystem));
        Assert.All(thirdParty, p => Assert.False(p.IsSystem));
        var detail = await apps.GetDetailAsync(serial, system[0].PackageName);
        Assert.NotEmpty(detail.PackageName);

        var console = new ConsoleService(runner);
        var lines = new List<string>();
        var result = await console.ExecuteAsync("shell getprop ro.product.model", serial, lines.Add);
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.NotEmpty(lines);

        var debug = new DebugCommandService(runner);
        var wake = await debug.RunAsync(serial, "list-users");
        Assert.True(wake.Succeeded, wake.FailureMessage);

        // 往返：mkdir → push 含中文与空格的文件 → ls 能看到 → pull → rm。
        var work = $"/sdcard/Download/TestEngineer 测试 {Guid.NewGuid():N}";
        Assert.True((await files.MakeDirectoryAsync(serial, work)).Succeeded);
        var local = Path.Combine(Path.GetTempPath(), "te 样本 file.txt");
        await File.WriteAllTextAsync(local, "hello");
        try
        {
            Assert.True((await files.PushAsync(serial, local, work)).Succeeded);
            var listed = await files.ListAsync(serial, work);
            Assert.Contains(listed, e => e.Name == "te 样本 file.txt" && e.Size == 5);

            var pulledDir = Path.Combine(Path.GetTempPath(), "te-pull-" + Guid.NewGuid().ToString("N"));
            var zip = Path.Combine(pulledDir, "evidence.zip");
            var pack = await files.PullToEvidenceZipAsync(serial, [work + "/te 样本 file.txt"], zip);
            Assert.True(pack.Succeeded, pack.FailureMessage);
            using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
            {
                Assert.Contains(archive.Entries, e => e.FullName.EndsWith("te 样本 file.txt", StringComparison.Ordinal));
                Assert.Contains(archive.Entries, e => e.FullName == "manifest.txt");
            }

            Directory.Delete(pulledDir, recursive: true);
        }
        finally
        {
            File.Delete(local);
            Assert.True((await files.DeleteAsync(serial, work, recursive: true)).Succeeded);
        }
    }
}