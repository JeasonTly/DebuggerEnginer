using System.Globalization;
using System.Text.RegularExpressions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb.Parsing;

/// <summary>getprop 全量输出 <c>[key]: [value]</c>。</summary>
public static partial class GetPropParser
{
    [GeneratedRegex(@"^\[(?<k>[^\]]+)\]:\s*\[(?<v>.*)\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex LineRegex();

    public static IReadOnlyDictionary<string, string> Parse(string? output)
    {
        var props = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in (output ?? string.Empty).Split('\n'))
        {
            var match = LineRegex().Match(line.TrimEnd('\r'));
            if (match.Success)
            {
                props[match.Groups["k"].Value] = match.Groups["v"].Value;
            }
        }

        return props;
    }

    public static string? Get(IReadOnlyDictionary<string, string> props, string key) =>
        props.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

public sealed record BatteryInfo(
    int? Level,
    string? Status,
    bool? IsCharging,
    double? TemperatureC,
    int? VoltageMv,
    string? PowerSource);

/// <summary><c>dumpsys battery</c>。</summary>
public static class BatteryParser
{
    public static BatteryInfo Parse(string? output)
    {
        var fields = KeyValueLines(output);
        int? level = TryInt(fields, "level");
        int? statusCode = TryInt(fields, "status");
        var status = statusCode switch
        {
            1 => "未知",
            2 => "充电中",
            3 => "放电中",
            4 => "未充电",
            5 => "已充满",
            _ => null,
        };

        double? temperature = TryInt(fields, "temperature") is { } t ? t / 10d : null;
        var sources = new List<string>();
        if (IsTrue(fields, "AC powered"))
        {
            sources.Add("AC");
        }

        if (IsTrue(fields, "USB powered"))
        {
            sources.Add("USB");
        }

        if (IsTrue(fields, "Wireless powered"))
        {
            sources.Add("无线");
        }

        bool? charging = statusCode is null ? null : statusCode is 2 or 5;
        return new BatteryInfo(
            level,
            status,
            charging,
            temperature,
            TryInt(fields, "voltage"),
            sources.Count == 0 ? (fields.Count == 0 ? null : "电池") : string.Join('+', sources));
    }

    private static Dictionary<string, string> KeyValueLines(string? output)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in (output ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                map.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
            }
        }

        return map;
    }

    private static int? TryInt(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static bool IsTrue(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var v) && v.Equals("true", StringComparison.OrdinalIgnoreCase);
}

public sealed record StorageInfo(long TotalBytes, long UsedBytes, long FreeBytes);

/// <summary><c>df -k</c>（单位 1K）。取第一条数据行。</summary>
public static class DfParser
{
    public static StorageInfo? Parse(string? output)
    {
        foreach (var raw in (output ?? string.Empty).Split('\n'))
        {
            var parts = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            // Filesystem 1K-blocks Used Available Use% Mounted on
            if (parts.Length >= 4
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var total)
                && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var used)
                && long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var free))
            {
                return new StorageInfo(total * 1024, used * 1024, free * 1024);
            }
        }

        return null;
    }
}

public sealed record MemoryInfo(long TotalBytes, long AvailableBytes);

/// <summary><c>/proc/meminfo</c>。</summary>
public static partial class MemInfoParser
{
    [GeneratedRegex(@"^(?<k>\w+):\s+(?<v>\d+)\s*kB", RegexOptions.CultureInvariant)]
    private static partial Regex LineRegex();

    public static MemoryInfo? Parse(string? output)
    {
        long? total = null, available = null, free = null;
        foreach (var raw in (output ?? string.Empty).Split('\n'))
        {
            var match = LineRegex().Match(raw.Trim());
            if (!match.Success)
            {
                continue;
            }

            var value = long.Parse(match.Groups["v"].Value, CultureInfo.InvariantCulture) * 1024;
            switch (match.Groups["k"].Value)
            {
                case "MemTotal":
                    total = value;
                    break;
                case "MemAvailable":
                    available = value;
                    break;
                case "MemFree":
                    free = value;
                    break;
            }
        }

        return total is null ? null : new MemoryInfo(total.Value, available ?? free ?? 0);
    }
}

/// <summary><c>wm size</c> / <c>wm density</c>：优先 Override，其次 Physical。</summary>
public static partial class WmParser
{
    [GeneratedRegex(@"(?<label>Physical|Override) size:\s*(?<w>\d+)x(?<h>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"(?<label>Physical|Override) density:\s*(?<d>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex DensityRegex();

    public static (int Width, int Height)? ParseSize(string? output)
    {
        (int, int)? physical = null, overridden = null;
        foreach (Match match in SizeRegex().Matches(output ?? string.Empty))
        {
            var value = (int.Parse(match.Groups["w"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture));
            if (match.Groups["label"].Value == "Override")
            {
                overridden = value;
            }
            else
            {
                physical = value;
            }
        }

        return overridden ?? physical;
    }

    public static int? ParseDensity(string? output)
    {
        int? physical = null, overridden = null;
        foreach (Match match in DensityRegex().Matches(output ?? string.Empty))
        {
            var value = int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture);
            if (match.Groups["label"].Value == "Override")
            {
                overridden = value;
            }
            else
            {
                physical = value;
            }
        }

        return overridden ?? physical;
    }

    /// <summary>
    /// 解析当前旋转（0/1/2/3，对应 0°/90°/180°/270°）。
    /// 兼容 dumpsys display / window 与 SurfaceOrientation。
    /// </summary>
    public static int? ParseRotation(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (Match match in RotationRegex().Matches(output))
        {
            var raw = match.Groups["r"].Value;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                && n is >= 0 and <= 3)
            {
                return n;
            }
        }

        return null;
    }

    /// <summary>
    /// 按旋转得到「当前画面」宽高：90°/270° 时交换 Physical/Override 尺寸。
    /// </summary>
    public static (int Width, int Height)? ApplyRotation((int Width, int Height) size, int? rotation)
    {
        if (rotation is 1 or 3)
        {
            return (size.Height, size.Width);
        }

        return size;
    }

    [GeneratedRegex(
        @"(?:mCurrentRotation|mDisplayRotation|mRotation|SurfaceOrientation|displayRotation)\s*[=:]\s*(?:ROTATION_)?(?<r>[0-3])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RotationRegex();
}

public sealed record CpuInfo(string? Model, int Cores);

/// <summary><c>/proc/cpuinfo</c>：核心数 + Hardware/model name。</summary>
public static partial class CpuInfoParser
{
    [GeneratedRegex(@"^processor\s*:\s*\d+", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex ProcessorRegex();

    [GeneratedRegex(@"^(?:Hardware|model name)\s*:\s*(?<v>.+)$", RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ModelRegex();

    public static CpuInfo Parse(string? output)
    {
        var text = (output ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal);
        var cores = ProcessorRegex().Matches(text).Count;
        var model = ModelRegex().Match(text);
        return new CpuInfo(model.Success ? model.Groups["v"].Value.Trim() : null, cores);
    }
}

public sealed record NetworkInterfaceInfo(string Name, string? Mac, string? Ipv4);

/// <summary><c>ip addr</c>。</summary>
public static partial class IpAddrParser
{
    [GeneratedRegex(@"^\d+:\s+(?<name>[^:@\s]+)(?:@\S+)?:", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"link/ether\s+(?<mac>[0-9a-fA-F:]{17})", RegexOptions.CultureInvariant)]
    private static partial Regex MacRegex();

    [GeneratedRegex(@"inet\s+(?<ip>\d+\.\d+\.\d+\.\d+)(?:/\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex InetRegex();

    public static IReadOnlyList<NetworkInterfaceInfo> Parse(string? output)
    {
        var result = new List<NetworkInterfaceInfo>();
        string? name = null, mac = null, ip = null;

        void Flush()
        {
            if (name is not null)
            {
                result.Add(new NetworkInterfaceInfo(name, mac, ip));
            }
        }

        foreach (var raw in (output ?? string.Empty).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var header = HeaderRegex().Match(line);
            if (header.Success)
            {
                Flush();
                name = header.Groups["name"].Value;
                mac = null;
                ip = null;
                continue;
            }

            if (name is null)
            {
                continue;
            }

            var macMatch = MacRegex().Match(line);
            if (macMatch.Success)
            {
                mac = macMatch.Groups["mac"].Value.ToLowerInvariant();
            }

            var inet = InetRegex().Match(line);
            if (inet.Success && ip is null)
            {
                ip = inet.Groups["ip"].Value;
            }
        }

        Flush();
        return result;
    }

    /// <summary>选出对外展示的接口：优先 wlan0，其次第一个非回环且有 IPv4 的接口。</summary>
    public static NetworkInterfaceInfo? PickPrimary(IReadOnlyList<NetworkInterfaceInfo> interfaces) =>
        interfaces.FirstOrDefault(i => i.Name == "wlan0")
        ?? interfaces.FirstOrDefault(i => i.Name != "lo" && i.Ipv4 is not null);
}

public sealed record WifiInfoSummary(string? Ssid, string? Mac, string? Ip, bool Connected);

/// <summary>从 <c>dumpsys wifi</c> 中的 mWifiInfo 行提取 SSID/MAC/IP。</summary>
public static partial class WifiInfoParser
{
    [GeneratedRegex(@"mWifiInfo\s+(?<body>.*)$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex LineRegex();

    [GeneratedRegex(@"SSID:\s*(?<v>""[^""]*""|<unknown ssid>|[^,]*)", RegexOptions.CultureInvariant)]
    private static partial Regex SsidRegex();

    [GeneratedRegex(@"\bMAC:\s*(?<v>[0-9a-fA-F:]{17})", RegexOptions.CultureInvariant)]
    private static partial Regex MacRegex();

    [GeneratedRegex(@"\bIP:\s*/?(?<v>\d+\.\d+\.\d+\.\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex IpRegex();

    public const string PlaceholderMac = "02:00:00:00:00:00";

    public static WifiInfoSummary? Parse(string? output)
    {
        var text = (output ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal);
        var line = LineRegex().Match(text);
        if (!line.Success)
        {
            return null;
        }

        var body = line.Groups["body"].Value;
        string? ssid = null;
        var ssidMatch = SsidRegex().Match(body);
        if (ssidMatch.Success)
        {
            var value = ssidMatch.Groups["v"].Value.Trim().Trim('"');
            ssid = value is "" or "<unknown ssid>" or "<none>" ? null : value;
        }

        var mac = MacRegex().Match(body) is { Success: true } m ? m.Groups["v"].Value.ToLowerInvariant() : null;
        var ip = IpRegex().Match(body) is { Success: true } i ? i.Groups["v"].Value : null;
        return new WifiInfoSummary(ssid, mac, ip, ssid is not null);
    }
}

/// <summary><c>dumpsys connectivity</c> 的活动网络类型。</summary>
public static partial class ConnectivityParser
{
    [GeneratedRegex(@"Active default network:\s*(?<id>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex ActiveRegex();

    [GeneratedRegex(@"\btype:\s*(?<t>\w+)", RegexOptions.CultureInvariant)]
    private static partial Regex TypeRegex();

    [GeneratedRegex(@"transports=\[?(?<t>[A-Z_]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TransportsRegex();

    /// <summary>返回如 "WIFI"、"MOBILE"、"ETHERNET"；无活动网络返回 null。</summary>
    public static string? ParseActiveNetworkType(string? output)
    {
        var text = output ?? string.Empty;
        var active = ActiveRegex().Match(text);
        if (!active.Success || active.Groups["id"].Value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // NetworkAgentInfo 在单行内同时包含 network{<id>} 与 type: XXX[...]（字段顺序随版本而异），按行匹配。
        var marker = "network{" + active.Groups["id"].Value + "}";
        foreach (var line in text.Split('\n'))
        {
            if (!line.Contains("NetworkAgentInfo{", StringComparison.Ordinal) || !line.Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            var type = TypeRegex().Match(line);
            if (type.Success)
            {
                return type.Groups["t"].Value.ToUpperInvariant();
            }

            var transports = TransportsRegex().Match(line);
            if (transports.Success)
            {
                return transports.Groups["t"].Value;
            }
        }

        return "UNKNOWN";
    }
}

/// <summary><c>pm list packages -f</c>。</summary>
public static class PackageListParser
{
    public static IReadOnlyList<AppPackage> Parse(string? output, bool isSystem)
    {
        var packages = new List<AppPackage>();
        foreach (var raw in (output ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("package:", StringComparison.Ordinal))
            {
                continue;
            }

            var body = line["package:".Length..];
            var separator = body.LastIndexOf('=');
            if (separator <= 0 || separator == body.Length - 1)
            {
                continue;
            }

            var apkPath = body[..separator];
            var packageName = body[(separator + 1)..];
            // 路径在系统分区 / priv-app 的，即使曾以「第三方」渠道安装，也视为系统应用
            var system = isSystem || AppPackage.IsSystemApkPath(apkPath);
            packages.Add(new AppPackage(packageName, apkPath, system));
        }

        return packages;
    }
}
