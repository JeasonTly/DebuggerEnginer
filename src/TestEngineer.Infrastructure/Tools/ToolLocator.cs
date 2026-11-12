using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Settings;

namespace TestEngineer.Infrastructure.Tools;

public enum ToolSource
{
    SettingsOverride,
    Bundled,
    EnvironmentSdk,
    Fallback,
    Path,
}

public sealed record ToolLocation(string FullPath, ToolSource Source);

/// <summary>
/// 查找 adb.exe / scrcpy.exe。顺序：设置覆盖 → 随包 tools/third-party → ANDROID_HOME/SDK_ROOT → 附加回落目录 → PATH。
/// 随包目录既查程序目录，也向上逐级查找（便于开发时从 bin/Debug 运行）。
/// </summary>
public sealed class ToolLocator : IToolLocator
{
    public const string ThirdPartyRelativePath = "tools/third-party";

    /// <summary>本机约定的 SDK platform-tools 回落位置。</summary>
    public static readonly IReadOnlyList<string> DefaultFallbackDirectories = [@"F:\AndroidSdk\platform-tools"];

    private const int MaxParentSearchDepth = 8;

    private readonly IReadOnlyList<string> _startDirectories;
    private readonly IReadOnlyList<string> _fallbackDirectories;
    private readonly Func<ToolPathSettings?>? _overrides;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public ToolLocator(
        IEnumerable<string>? startDirectories = null,
        Func<ToolPathSettings?>? overrides = null,
        IEnumerable<string>? fallbackDirectories = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        _startDirectories = [.. (startDirectories ?? [AppContext.BaseDirectory, Environment.CurrentDirectory]).Select(Path.GetFullPath)];
        _fallbackDirectories = [.. fallbackDirectories ?? DefaultFallbackDirectories];
        _overrides = overrides;
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public string? FindAdb() => LocateAdb()?.FullPath;

    public string? FindScrcpy() => LocateScrcpy()?.FullPath;

    public ToolLocation? LocateAdb()
    {
        var configured = _overrides?.Invoke()?.Adb;
        if (IsFile(configured))
        {
            return new ToolLocation(Path.GetFullPath(configured!), ToolSource.SettingsOverride);
        }

        foreach (var root in EnumerateThirdPartyRoots())
        {
            var candidate = Path.Combine(root, "platform-tools", "adb.exe");
            if (File.Exists(candidate))
            {
                return new ToolLocation(candidate, ToolSource.Bundled);
            }
        }

        foreach (var variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var sdk = _getEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(sdk))
            {
                var candidate = Path.Combine(sdk, "platform-tools", "adb.exe");
                if (File.Exists(candidate))
                {
                    return new ToolLocation(candidate, ToolSource.EnvironmentSdk);
                }
            }
        }

        foreach (var directory in _fallbackDirectories)
        {
            var candidate = Path.Combine(directory, "adb.exe");
            if (File.Exists(candidate))
            {
                return new ToolLocation(candidate, ToolSource.Fallback);
            }
        }

        return FindOnPath("adb.exe");
    }

    public ToolLocation? LocateScrcpy()
    {
        var configured = _overrides?.Invoke()?.Scrcpy;
        if (IsFile(configured))
        {
            return new ToolLocation(Path.GetFullPath(configured!), ToolSource.SettingsOverride);
        }

        foreach (var root in EnumerateThirdPartyRoots())
        {
            var found = FindFileLimitedDepth(root, "scrcpy.exe", maxDepth: 3);
            if (found is not null)
            {
                return new ToolLocation(found, ToolSource.Bundled);
            }
        }

        return FindOnPath("scrcpy.exe");
    }

    public string? FindApksigner()
    {
        foreach (var variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var found = FindInSdk(_getEnvironmentVariable(variable));
            if (found is not null)
            {
                return found;
            }
        }

        foreach (var directory in _fallbackDirectories)
        {
            var found = FindInSdk(SdkRootFrom(directory));
            if (found is not null)
            {
                return found;
            }
        }

        return FindOnPath("apksigner.bat")?.FullPath;
    }

    private static string SdkRootFrom(string directory)
    {
        var full = Path.GetFullPath(directory);
        var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Equals("platform-tools", StringComparison.OrdinalIgnoreCase)
            || name.Equals("build-tools", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(full) ?? full;
        }

        return full;
    }

    private static string? FindInSdk(string? sdk)
    {
        if (string.IsNullOrWhiteSpace(sdk))
        {
            return null;
        }

        var buildTools = Path.Combine(sdk, "build-tools");
        if (!Directory.Exists(buildTools))
        {
            return null;
        }

        string? bestPath = null;
        Version? bestVersion = null;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(buildTools))
            {
                var bat = Path.Combine(dir, "apksigner.bat");
                if (!File.Exists(bat))
                {
                    continue;
                }

                Version.TryParse(Path.GetFileName(dir), out var version);
                if (bestPath is null || (version is not null && (bestVersion is null || version > bestVersion)))
                {
                    bestPath = bat;
                    bestVersion = version;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return bestPath;
        }

        return bestPath;
    }

    private static bool IsFile(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private IEnumerable<string> EnumerateThirdPartyRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in _startDirectories)
        {
            var directory = new DirectoryInfo(start);
            for (var depth = 0; directory is not null && depth <= MaxParentSearchDepth; depth++, directory = directory.Parent)
            {
                var root = Path.Combine(directory.FullName, ThirdPartyRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(root) && seen.Add(root))
                {
                    yield return root;
                }
            }
        }
    }

    private static string? FindFileLimitedDepth(string root, string fileName, int maxDepth)
    {
        var direct = Path.Combine(root, fileName);
        if (File.Exists(direct))
        {
            return direct;
        }

        if (maxDepth <= 0)
        {
            return null;
        }

        try
        {
            foreach (var sub in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                var found = FindFileLimitedDepth(sub, fileName, maxDepth - 1);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 目录不可访问时跳过。
        }

        return null;
    }

    private ToolLocation? FindOnPath(string fileName)
    {
        var path = _getEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), fileName);
                if (File.Exists(candidate))
                {
                    return new ToolLocation(candidate, ToolSource.Path);
                }
            }
            catch (ArgumentException)
            {
                // PATH 中含非法字符的条目忽略。
            }
        }

        return null;
    }
}
