using TestEngineer.Adb.Parsing;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb;

/// <summary>
/// 定位、推送并运行 tools/device-helper/device-helper.jar（app_process），
/// 失败时返回 null，由调用方走包名末段 + 首字占位回退。
/// </summary>
public sealed class DeviceHelperClient
{
    public const string RemoteJarPath = "/data/local/tmp/te-device-helper.jar";
    public const string MainClass = "com.testengineer.devicehelper.AppInfoDump";

    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(3);

    private readonly IAdbProcessRunner _runner;
    private readonly Func<string?> _locateJar;

    public DeviceHelperClient(IAdbProcessRunner runner, Func<string?>? locateJar = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _locateJar = locateJar ?? (() => DeviceHelperLocator.FindJar());
    }

    /// <summary>尝试富化应用列表；jar 缺失或运行失败返回 null。</summary>
    public async Task<IReadOnlyList<AppPackage>?> TryEnrichAsync(
        string serial,
        IReadOnlyList<AppPackage> packages,
        bool withIcons = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(packages);

        var jar = _locateJar();
        if (string.IsNullOrWhiteSpace(jar) || !File.Exists(jar))
        {
            return null;
        }

        var push = await _runner.RunAsync(
            ["push", jar, RemoteJarPath],
            serial,
            PushTimeout,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!push.Succeeded)
        {
            return null;
        }

        var flags = withIcons ? "" : " --no-icons";
        var shell =
            $"CLASSPATH={RemoteJarPath} app_process /system/bin {MainClass}{flags}";
        var run = await _runner.ShellAsync(serial, shell, RunTimeout, cancellationToken).ConfigureAwait(false);
        if (!DeviceHelperOutputParser.LooksSuccessful(run.StandardOutput)
            && !DeviceHelperOutputParser.LooksSuccessful(run.CombinedOutput))
        {
            return null;
        }

        var output = DeviceHelperOutputParser.LooksSuccessful(run.StandardOutput)
            ? run.StandardOutput
            : run.CombinedOutput;
        var map = DeviceHelperOutputParser.Parse(output);
        if (map.Count == 0)
        {
            return null;
        }

        return DeviceHelperOutputParser.Merge(packages, map);
    }
}

/// <summary>在程序目录及上级查找 tools/device-helper/device-helper.jar。</summary>
public static class DeviceHelperLocator
{
    public const string RelativeJarPath = "tools/device-helper/device-helper.jar";
    private const int MaxParentSearchDepth = 8;

    public static string? FindJar(IEnumerable<string>? startDirectories = null)
    {
        var starts = (startDirectories ?? [AppContext.BaseDirectory, Environment.CurrentDirectory])
            .Select(Path.GetFullPath);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var start in starts)
        {
            var directory = new DirectoryInfo(start);
            for (var depth = 0; directory is not null && depth <= MaxParentSearchDepth; depth++, directory = directory.Parent)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    RelativeJarPath.Replace('/', Path.DirectorySeparatorChar));
                if (seen.Add(candidate) && File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
