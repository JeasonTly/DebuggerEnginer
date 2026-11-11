namespace TestEngineer.Core.Models;

/// <summary>应用列表筛选。</summary>
public enum AppListFilter
{
    ThirdParty,
    System,
    All,
    /// <summary>仅显示带真实图标数据（<see cref="AppPackage.HasIcon"/>）的项。</summary>
    HasIcon,
}

/// <summary><c>pm list packages -f</c> 的一条记录，可经 device-helper 富化。</summary>
public sealed record AppPackage(
    string PackageName,
    string ApkPath,
    bool IsSystem,
    string? Label = null,
    string? VersionName = null,
    long? VersionCode = null,
    bool HasLauncher = false,
    byte[]? IconData = null)
{
    /// <summary>是否已拿到 PNG 图标字节。</summary>
    public bool HasIcon => IconData is { Length: > 0 };

    /// <summary>界面主行：优先应用名，否则包名末段。</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Label) ? Label! : FallbackLabel(PackageName);

    /// <summary>占位图标首字（取 DisplayName 第一个可见字符）。</summary>
    public string Initial
    {
        get
        {
            var name = DisplayName.Trim();
            if (name.Length == 0)
            {
                return "?";
            }

            return name[..RuneLength(name)].ToUpperInvariant();
        }
    }

    /// <summary>系统 / 第三方文字标签。</summary>
    public string KindLabel => IsSystem ? "系统" : "第三方";

    /// <summary>包名最后一段作为回退显示名（如 <c>com.android.chrome</c> → <c>chrome</c>）。</summary>
    public static string FallbackLabel(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return packageName;
        }

        var lastDot = packageName.LastIndexOf('.');
        if (lastDot < 0 || lastDot == packageName.Length - 1)
        {
            return packageName;
        }

        return packageName[(lastDot + 1)..];
    }

    private static int RuneLength(string value)
    {
        if (char.IsHighSurrogate(value[0]) && value.Length > 1 && char.IsLowSurrogate(value[1]))
        {
            return 2;
        }

        return 1;
    }
}

/// <summary>应用权限（来自 <c>dumpsys package</c>）。</summary>
/// <param name="Name">权限名。</param>
/// <param name="Granted">已授予；未知为 null（仅声明请求，无授予信息）。</param>
/// <param name="IsRuntime">运行时权限（可用 pm grant/revoke 变更）。</param>
public sealed record AppPermission(string Name, bool? Granted, bool IsRuntime);

/// <summary>单个应用的详情。</summary>
public sealed record AppPackageDetail
{
    public required string PackageName { get; init; }

    public string? VersionName { get; init; }

    public long? VersionCode { get; init; }

    public string? FirstInstallTime { get; init; }

    public string? LastUpdateTime { get; init; }

    public int? TargetSdk { get; init; }

    public int? MinSdk { get; init; }

    public string? UserId { get; init; }

    public bool IsDebuggable { get; init; }

    public bool IsSystem { get; init; }

    public IReadOnlyList<AppPermission> Permissions { get; init; } = [];
}
