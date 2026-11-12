using System.Text.RegularExpressions;
using TestEngineer.Adb.Parsing;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb;

/// <summary>应用管理：pm / dumpsys package / appops / monkey 启动；可选 device-helper 富化名称与图标。</summary>
public sealed partial class AppManagerService : IAppManagerService
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(5);

    private readonly IAdbProcessRunner _runner;
    private readonly DeviceHelperClient _deviceHelper;

    public AppManagerService(IAdbProcessRunner runner, DeviceHelperClient? deviceHelper = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _deviceHelper = deviceHelper ?? new DeviceHelperClient(runner);
    }

    // Android 包名/权限名/appop 名：字母数字、点、下划线。
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    public async Task<IReadOnlyList<AppPackage>> ListAsync(string serial, AppListFilter filter, CancellationToken cancellationToken = default)
    {
        // 始终拉全量（-3 + -s），合并后再按 filter 裁切，便于上层一次缓存、分段本地筛选
        var third = await ListByFlagAsync(serial, "-3", isSystem: false, cancellationToken).ConfigureAwait(false);
        var system = await ListByFlagAsync(serial, "-s", isSystem: true, cancellationToken).ConfigureAwait(false);

        var map = new Dictionary<string, AppPackage>(StringComparer.Ordinal);
        foreach (var p in third)
        {
            map[p.PackageName] = p;
        }

        foreach (var p in system)
        {
            if (map.TryGetValue(p.PackageName, out var existing))
            {
                // 同包既在 -3 又在 -s（常见于系统更新包）：一律系统，路径优先系统分区
                var path = AppPackage.IsSystemApkPath(p.ApkPath) ? p.ApkPath : existing.ApkPath;
                map[p.PackageName] = existing with
                {
                    IsSystem = true,
                    ApkPath = string.IsNullOrWhiteSpace(path) ? existing.ApkPath : path,
                };
            }
            else
            {
                map[p.PackageName] = p;
            }
        }

        var packages = map.Values
            .Select(p => p with { IsSystem = p.IsSystem || AppPackage.IsSystemApkPath(p.ApkPath) })
            .OrderBy(p => p.PackageName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var enriched = await _deviceHelper.TryEnrichAsync(serial, packages, withIcons: true, cancellationToken)
            .ConfigureAwait(false);
        if (enriched is not null)
        {
            packages = enriched
                .Select(p => p with { IsSystem = p.IsSystem || AppPackage.IsSystemApkPath(p.ApkPath) })
                .ToList();
        }
        else
        {
            // helper 失败时用 query-activities 补 Launcher 标记，否则「有图标」会空列表
            packages = await MarkLauncherPackagesAsync(serial, packages, cancellationToken).ConfigureAwait(false);
        }

        return ApplyClientFilter(packages, filter);
    }

    /// <summary>内存筛选（供 UI 缓存后切换分段，无需再打 adb）。</summary>
    public static IReadOnlyList<AppPackage> ApplyClientFilter(IEnumerable<AppPackage> source, AppListFilter filter) =>
        filter switch
        {
            AppListFilter.System => source.Where(p => p.IsSystem).ToList(),
            AppListFilter.ThirdParty => source.Where(p => !p.IsSystem).ToList(),
            // 「有图标」= 有桌面入口（真实 PNG 或首字占位）；无 Launcher 的服务包不出现
            AppListFilter.HasIcon => source.Where(p => p.HasLauncher).ToList(),
            _ => source.ToList(),
        };

    private async Task<List<AppPackage>> MarkLauncherPackagesAsync(
        string serial,
        List<AppPackage> packages,
        CancellationToken cancellationToken)
    {
        var launchers = await ListLauncherPackageNamesAsync(serial, cancellationToken).ConfigureAwait(false);
        if (launchers.Count == 0)
        {
            return packages;
        }

        return packages
            .Select(p => launchers.Contains(p.PackageName) ? p with { HasLauncher = true } : p)
            .ToList();
    }

    private async Task<HashSet<string>> ListLauncherPackageNamesAsync(string serial, CancellationToken cancellationToken)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cmd in new[]
                 {
                     "cmd package query-activities --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER",
                     "pm query-activities --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER",
                 })
        {
            var r = await _runner.ShellAsync(serial, cmd, ShortTimeout, cancellationToken).ConfigureAwait(false);
            var text = r.StandardOutput ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var line = raw.Trim();
                // com.pkg/.Activity 或 package:com.pkg
                if (line.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
                {
                    var pkg = line["package:".Length..].Trim();
                    if (pkg.Length > 0) set.Add(pkg);
                    continue;
                }

                var slash = line.IndexOf('/');
                if (slash > 0)
                {
                    var pkg = line[..slash].Trim();
                    if (pkg.Contains('.', StringComparison.Ordinal) && !pkg.Contains(' ', StringComparison.Ordinal))
                    {
                        set.Add(pkg);
                    }
                }
            }

            if (set.Count > 0)
            {
                break;
            }
        }

        return set;
    }

    public async Task<AppPackageDetail> GetDetailAsync(string serial, string packageName, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(packageName, nameof(packageName));
        var result = await _runner.ShellAsync(serial, $"dumpsys package {packageName}", TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new DeviceCommandException($"读取应用详情失败：{result.FailureMessage}", result);
        }

        return PackageDumpParser.Parse(packageName, result.StandardOutput);
    }

    public Task<AdbCommandResult> InstallAsync(
        string serial,
        string apkPath,
        bool replace = true,
        bool grantAllPermissions = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apkPath);
        var args = new List<string> { "install" };
        if (replace)
        {
            args.Add("-r");
        }

        if (grantAllPermissions)
        {
            args.Add("-g");
        }

        args.Add(apkPath);
        return RunCheckedAsync(args, serial, InstallTimeout, cancellationToken);
    }

    public Task<AdbCommandResult> UninstallAsync(string serial, string packageName, bool keepData = false, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(packageName, nameof(packageName));
        return ShellCheckedAsync(serial, keepData ? $"pm uninstall -k {packageName}" : $"pm uninstall {packageName}", cancellationToken);
    }

    public Task<AdbCommandResult> ClearDataAsync(string serial, string packageName, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(packageName, nameof(packageName));
        return ShellCheckedAsync(serial, $"pm clear {packageName}", cancellationToken);
    }

    public Task<AdbCommandResult> ForceStopAsync(string serial, string packageName, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(packageName, nameof(packageName));
        return ShellCheckedAsync(serial, $"am force-stop {packageName}", cancellationToken);
    }

    public Task<AdbCommandResult> GrantPermissionAsync(string serial, string packageName, string permission, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(packageName, nameof(packageName));
        EnsureIdentifier(permission, nameof(permission));
        return ShellCheckedAsync(serial, $"pm grant {packageName} {permission}", cancellationToken);
    }

    public Task<AdbCommandResult> RevokePermissionAsync(string serial, string packageName, string permission, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(packageName, nameof(packageName));
        EnsureIdentifier(permission, nameof(permission));
        return ShellCheckedAsync(serial, $"pm revoke {packageName} {permission}", cancellationToken);
    }

    public Task<AdbCommandResult> SetAppOpAsync(string serial, string packageName, string operation, string mode, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(packageName, nameof(packageName));
        EnsureIdentifier(operation, nameof(operation));
        if (!AppOpModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"不支持的 appops 模式：{mode}（可选 {string.Join('/', AppOpModes)}）", nameof(mode));
        }

        return ShellCheckedAsync(serial, $"appops set {packageName} {operation} {mode.ToLowerInvariant()}", cancellationToken);
    }

    public async Task<AdbCommandResult> LaunchAsync(string serial, string packageName, CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(packageName, nameof(packageName));
        var result = await _runner.ShellAsync(
            serial,
            $"monkey -p {packageName} -c android.intent.category.LAUNCHER 1",
            ShortTimeout,
            cancellationToken).ConfigureAwait(false);

        // monkey 在找不到启动 Activity 时仍可能返回 0，需检查输出。
        if (result.Succeeded && result.StandardOutput.Contains("No activities found", StringComparison.OrdinalIgnoreCase))
        {
            return result with { ExitCode = 1, StandardError = "未找到可启动的 Activity（该应用可能没有 LAUNCHER 入口）。" };
        }

        return result;
    }

    public static IReadOnlyList<string> AppOpModes { get; } = ["allow", "ignore", "deny", "default", "foreground"];

    private async Task<IReadOnlyList<AppPackage>> ListByFlagAsync(string serial, string flag, bool isSystem, CancellationToken cancellationToken)
    {
        var result = await _runner.ShellAsync(serial, $"pm list packages -f {flag}", ShortTimeout, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new DeviceCommandException($"读取应用列表失败：{result.FailureMessage}", result);
        }

        return PackageListParser.Parse(result.StandardOutput, isSystem);
    }

    private Task<AdbCommandResult> ShellCheckedAsync(string serial, string command, CancellationToken cancellationToken) =>
        RunCheckedAsync(["shell", command], serial, ShortTimeout, cancellationToken);

    /// <summary>pm/adb install 对失败常以 "Failure [...]" / "Error:" / "Exception" 文本报告而退出码仍可能为 0，这里统一识别。</summary>
    private async Task<AdbCommandResult> RunCheckedAsync(IReadOnlyList<string> args, string serial, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(args, serial, timeout, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Succeeded && LooksLikeFailure(result.CombinedOutput))
        {
            return result with { ExitCode = 1, StandardError = result.CombinedOutput };
        }

        return result;
    }

    private static bool LooksLikeFailure(string output) =>
        output.Contains("Failure", StringComparison.Ordinal)
        || output.Contains("Error:", StringComparison.Ordinal)
        || output.Contains("Exception", StringComparison.Ordinal)
        || output.Contains("Unknown package", StringComparison.OrdinalIgnoreCase);

    private static void EnsureIdentifier(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || !IdentifierRegex().IsMatch(value))
        {
            throw new ArgumentException($"非法标识符：\"{value}\"。", paramName);
        }
    }
}
