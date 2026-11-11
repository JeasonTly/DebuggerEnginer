using TestEngineer.Core.Models;

namespace TestEngineer.Adb.Parsing;

/// <summary>device-helper（app_process）stdout 行解析。</summary>
public static class DeviceHelperOutputParser
{
    public const string HeaderMarker = "TE_HELPER_OK";
    public const string DoneMarker = "TE_HELPER_DONE";
    public const string LinePrefix = "TE1";

    public static bool LooksSuccessful(string? output) =>
        !string.IsNullOrEmpty(output)
        && output.Contains(HeaderMarker, StringComparison.Ordinal)
        && output.Contains(DoneMarker, StringComparison.Ordinal);

    /// <summary>解析 TE1 行；非法行跳过。</summary>
    public static IReadOnlyDictionary<string, DeviceHelperAppInfo> Parse(string? output)
    {
        var map = new Dictionary<string, DeviceHelperAppInfo>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(output))
        {
            return map;
        }

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith(LinePrefix + "\t", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 8)
            {
                continue;
            }

            var packageName = parts[1];
            if (string.IsNullOrWhiteSpace(packageName))
            {
                continue;
            }

            var label = NormalizeField(parts[2]);
            var versionName = NormalizeField(parts[3]);
            long? versionCode = long.TryParse(parts[4], out var vc) ? vc : null;
            var isSystem = parts[5] == "1";
            var hasLauncher = parts[6] == "1";
            byte[]? icon = DecodeIcon(parts[7]);

            map[packageName] = new DeviceHelperAppInfo(
                packageName,
                label,
                versionName,
                versionCode,
                isSystem,
                hasLauncher,
                icon);
        }

        return map;
    }

    /// <summary>用 helper 信息富化 pm 列表；无匹配时保留原项并确保 Label 回退可用。</summary>
    public static IReadOnlyList<AppPackage> Merge(
        IReadOnlyList<AppPackage> packages,
        IReadOnlyDictionary<string, DeviceHelperAppInfo> helper)
    {
        var list = new List<AppPackage>(packages.Count);
        foreach (var pkg in packages)
        {
            if (!helper.TryGetValue(pkg.PackageName, out var info))
            {
                list.Add(pkg);
                continue;
            }

            list.Add(pkg with
            {
                Label = info.Label ?? pkg.Label,
                VersionName = info.VersionName ?? pkg.VersionName,
                VersionCode = info.VersionCode ?? pkg.VersionCode,
                HasLauncher = info.HasLauncher || pkg.HasLauncher,
                IconData = info.IconData ?? pkg.IconData,
                // helper 的系统标记更准；若 pm 已标系统则保持。
                IsSystem = pkg.IsSystem || info.IsSystem,
            });
        }

        return list;
    }

    private static string? NormalizeField(string value) =>
        string.IsNullOrWhiteSpace(value) || value == "-" ? null : value;

    private static byte[]? DecodeIcon(string field)
    {
        if (string.IsNullOrWhiteSpace(field) || field == "-")
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(field);
            return bytes.Length == 0 ? null : bytes;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>device-helper 单包信息。</summary>
public sealed record DeviceHelperAppInfo(
    string PackageName,
    string? Label,
    string? VersionName,
    long? VersionCode,
    bool IsSystem,
    bool HasLauncher,
    byte[]? IconData);
