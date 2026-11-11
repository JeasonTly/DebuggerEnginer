using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TestEngineer.Adb.Parsing;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb;

/// <summary>
/// 采集设备快照。各项命令并行执行、互相独立：user 版本上任一命令被拒绝（dumpsys 权限、/proc 受限等）
/// 只会在 <see cref="DeviceSnapshot.Warnings"/> 记录一条，不影响其余字段。
/// </summary>
public sealed class DeviceInfoService : IDeviceInfoService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    private readonly IAdbProcessRunner _runner;
    private readonly ILogger _logger;

    public DeviceInfoService(IAdbProcessRunner runner, ILogger<DeviceInfoService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _logger = logger ?? NullLogger<DeviceInfoService>.Instance;
    }

    public async Task<DeviceSnapshot> GetSnapshotAsync(string serial, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        var warnings = new ConcurrentQueue<string>();

        Task<string?> Run(string label, string command, bool quiet = false) =>
            RunShellAsync(serial, label, command, warnings, cancellationToken, quiet);

        var getprop = Run("getprop", "getprop");
        var battery = Run("dumpsys battery", "dumpsys battery");
        var df = Run("df /data", "df -k /data");
        var meminfo = Run("/proc/meminfo", "cat /proc/meminfo");
        var wmSize = Run("wm size", "wm size");
        var wmDensity = Run("wm density", "wm density");
        var uname = Run("uname", "uname -r");
        var connectivity = Run("dumpsys connectivity", "dumpsys connectivity");
        var wifi = Run("dumpsys wifi", "dumpsys wifi | grep mWifiInfo", quiet: true);
        var ipAddr = Run("ip addr", "ip addr");
        var uptime = Run("/proc/uptime", "cat /proc/uptime");
        var cpuinfo = Run("/proc/cpuinfo", "cat /proc/cpuinfo");

        await Task.WhenAll(getprop, battery, df, meminfo, wmSize, wmDensity, uname, connectivity, wifi, ipAddr, uptime, cpuinfo)
            .ConfigureAwait(false);

        var props = GetPropParser.Parse(getprop.Result);
        string? Prop(string key) => GetPropParser.Get(props, key);

        var batteryInfo = battery.Result is null ? null : BatteryParser.Parse(battery.Result);
        var storage = DfParser.Parse(df.Result);
        if (storage is null && df.Result is not null)
        {
            // /data 在部分 user 版本上输出异常，退回用户可见的共享存储。
            var fallback = await RunShellAsync(serial, "df /sdcard", "df -k /sdcard", warnings, cancellationToken).ConfigureAwait(false);
            storage = DfParser.Parse(fallback);
        }

        var memory = MemInfoParser.Parse(meminfo.Result);
        var size = WmParser.ParseSize(wmSize.Result);
        var density = WmParser.ParseDensity(wmDensity.Result);
        var cpu = CpuInfoParser.Parse(cpuinfo.Result);
        var interfaces = IpAddrParser.Parse(ipAddr.Result);
        var primary = IpAddrParser.PickPrimary(interfaces);
        var wlan = interfaces.FirstOrDefault(i => i.Name == "wlan0");
        var wifiInfo = WifiInfoParser.Parse(wifi.Result);
        var activeType = ConnectivityParser.ParseActiveNetworkType(connectivity.Result);

        var mac = wifiInfo?.Mac is { } m && m != WifiInfoParser.PlaceholderMac ? m : wlan?.Mac;
        if (mac is null)
        {
            // Wi-Fi MAC 在 Android 10+ 可能被随机化或隐藏，仍尽力读取接口地址。
            var address = await RunShellAsync(serial, "wlan0 address", "cat /sys/class/net/wlan0/address", warnings, cancellationToken, quiet: true).ConfigureAwait(false);
            mac = address?.Trim() is { Length: 17 } a ? a.ToLowerInvariant() : null;
        }

        var cpuModel = cpu.Model;
        if (string.IsNullOrWhiteSpace(cpuModel))
        {
            cpuModel = string.Join(' ', new[] { Prop("ro.soc.manufacturer"), Prop("ro.soc.model") }.Where(s => s is not null));
            cpuModel = cpuModel.Length == 0 ? Prop("ro.board.platform") : cpuModel;
        }

        // 回环地址不算对外 IP。
        var ip = wifiInfo?.Ip ?? (primary?.Name == "lo" ? null : primary?.Ipv4);
        var networkType = activeType
            ?? (connectivity.Result is not null ? (ip is not null ? "UNKNOWN" : "无连接") : null);

        return new DeviceSnapshot
        {
            Serial = serial,
            CapturedAt = DateTimeOffset.Now,
            Manufacturer = Prop("ro.product.manufacturer"),
            Brand = Prop("ro.product.brand"),
            Model = Prop("ro.product.model"),
            AndroidVersion = Prop("ro.build.version.release"),
            SdkLevel = int.TryParse(Prop("ro.build.version.sdk"), CultureInfo.InvariantCulture, out var sdk) ? sdk : 0,
            BuildType = Prop("ro.build.type"),
            BuildDisplayId = Prop("ro.build.display.id"),
            Debuggable = Prop("ro.debuggable") is { } debuggable ? debuggable == "1" : null,
            SecurityPatch = Prop("ro.build.version.security_patch"),
            Kernel = uname.Result?.Trim() is { Length: > 0 } kernel ? kernel : null,
            Baseband = Prop("gsm.version.baseband") is { } baseband ? baseband.Split(',')[0].Trim() : null,
            BatteryLevel = batteryInfo?.Level,
            BatteryStatus = batteryInfo?.Status,
            IsCharging = batteryInfo?.IsCharging,
            BatteryTemperatureC = batteryInfo?.TemperatureC,
            BatteryVoltageMv = batteryInfo?.VoltageMv,
            PowerSource = batteryInfo?.PowerSource,
            StorageTotalBytes = storage?.TotalBytes ?? 0,
            StorageUsedBytes = storage?.UsedBytes ?? 0,
            StorageFreeBytes = storage?.FreeBytes ?? 0,
            MemoryTotalBytes = memory?.TotalBytes ?? 0,
            MemoryAvailableBytes = memory?.AvailableBytes ?? 0,
            CpuModel = cpuModel,
            CpuCores = cpu.Cores,
            CpuAbi = Prop("ro.product.cpu.abi"),
            ScreenWidth = size?.Width ?? 0,
            ScreenHeight = size?.Height ?? 0,
            Density = density ?? 0,
            NetworkType = networkType,
            WifiSsid = wifiInfo?.Ssid,
            WifiMac = mac,
            IpAddress = ip,
            UptimeSec = ParseUptime(uptime.Result),
            Warnings = [.. warnings],
        };
    }

    private static long ParseUptime(string? output)
    {
        var first = output?.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? (long)seconds : 0;
    }

    private async Task<string?> RunShellAsync(
        string serial,
        string label,
        string command,
        ConcurrentQueue<string> warnings,
        CancellationToken cancellationToken,
        bool quiet = false)
    {
        try
        {
            var result = await _runner.ShellAsync(serial, command, CommandTimeout, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                return result.StandardOutput;
            }

            // 某些命令退出码非 0 但输出有效；有输出就用。
            if (!result.TimedOut && !result.Cancelled && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return result.StandardOutput;
            }

            if (result.Cancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!quiet)
            {
                warnings.Enqueue($"{label}: {result.FailureMessage}");
            }

            _logger.LogDebug("设备 {Serial} 采集 {Label} 失败：{Message}", serial, label, result.FailureMessage);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!quiet)
            {
                warnings.Enqueue($"{label}: {ex.Message}");
            }

            return null;
        }
    }
}
