using System.Globalization;
using System.Text.RegularExpressions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb.Parsing;

/// <summary>解析 <c>dumpsys package &lt;pkg&gt;</c> 中的版本、标志与权限。</summary>
public static partial class PackageDumpParser
{
    [GeneratedRegex(@"\bversionCode=(?<v>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionCodeRegex();

    [GeneratedRegex(@"\bminSdk=(?<v>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex MinSdkRegex();

    [GeneratedRegex(@"\btargetSdk=(?<v>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex TargetSdkRegex();

    [GeneratedRegex(@"^\s*versionName=(?<v>.*)$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex VersionNameRegex();

    [GeneratedRegex(@"^\s*userId=(?<v>\d+)", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex UserIdRegex();

    [GeneratedRegex(@"^\s*firstInstallTime=(?<v>.*)$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex FirstInstallRegex();

    [GeneratedRegex(@"^\s*lastUpdateTime=(?<v>.*)$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex LastUpdateRegex();

    [GeneratedRegex(@"^\s*(?:pkgFlags|flags)=\[(?<v>[^\]]*)\]", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex FlagsRegex();

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_.]+)(?::\s*(?<rest>.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex PermissionLineRegex();

    [GeneratedRegex(@"granted=(?<g>true|false)", RegexOptions.CultureInvariant)]
    private static partial Regex GrantedRegex();

    public static AppPackageDetail Parse(string packageName, string? dump)
    {
        var text = (dump ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal);
        var flags = FlagsRegex().Match(text) is { Success: true } f ? f.Groups["v"].Value : string.Empty;

        var requested = new List<string>();
        var install = new Dictionary<string, bool>(StringComparer.Ordinal);
        var runtime = new Dictionary<string, bool>(StringComparer.Ordinal);
        ParseSections(text, requested, install, runtime);

        var permissions = new List<AppPermission>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in requested)
        {
            if (!emitted.Add(name))
            {
                continue;
            }

            if (runtime.TryGetValue(name, out var runtimeGranted))
            {
                permissions.Add(new AppPermission(name, runtimeGranted, true));
            }
            else if (install.TryGetValue(name, out var installGranted))
            {
                permissions.Add(new AppPermission(name, installGranted, false));
            }
            else
            {
                permissions.Add(new AppPermission(name, null, false));
            }
        }

        foreach (var (name, granted) in runtime)
        {
            if (emitted.Add(name))
            {
                permissions.Add(new AppPermission(name, granted, true));
            }
        }

        foreach (var (name, granted) in install)
        {
            if (emitted.Add(name))
            {
                permissions.Add(new AppPermission(name, granted, false));
            }
        }

        return new AppPackageDetail
        {
            PackageName = packageName,
            VersionName = Capture(VersionNameRegex(), text),
            VersionCode = long.TryParse(Capture(VersionCodeRegex(), text), CultureInfo.InvariantCulture, out var vc) ? vc : null,
            MinSdk = int.TryParse(Capture(MinSdkRegex(), text), CultureInfo.InvariantCulture, out var min) ? min : null,
            TargetSdk = int.TryParse(Capture(TargetSdkRegex(), text), CultureInfo.InvariantCulture, out var target) ? target : null,
            UserId = Capture(UserIdRegex(), text),
            FirstInstallTime = Capture(FirstInstallRegex(), text),
            LastUpdateTime = Capture(LastUpdateRegex(), text),
            IsDebuggable = flags.Contains("DEBUGGABLE", StringComparison.Ordinal),
            IsSystem = flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("SYSTEM"),
            Permissions = permissions,
        };
    }

    private static string? Capture(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success ? match.Groups["v"].Value.Trim() : null;
    }

    private static void ParseSections(
        string text,
        List<string> requested,
        Dictionary<string, bool> install,
        Dictionary<string, bool> runtime)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            var kind = trimmed switch
            {
                "requested permissions:" => 1,
                "install permissions:" => 2,
                "runtime permissions:" => 3,
                _ => 0,
            };
            if (kind == 0)
            {
                continue;
            }

            var headerIndent = IndentOf(lines[i]);
            for (i++; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || IndentOf(line) <= headerIndent)
                {
                    i--;
                    break;
                }

                var match = PermissionLineRegex().Match(line.Trim());
                if (!match.Success)
                {
                    continue;
                }

                var name = match.Groups["name"].Value;
                var granted = GrantedRegex().Match(match.Groups["rest"].Value);
                switch (kind)
                {
                    case 1:
                        requested.Add(name);
                        break;
                    case 2:
                        install[name] = granted.Success && granted.Groups["g"].Value == "true";
                        break;
                    default:
                        runtime[name] = granted.Success && granted.Groups["g"].Value == "true";
                        break;
                }
            }
        }
    }

    private static int IndentOf(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }
}
