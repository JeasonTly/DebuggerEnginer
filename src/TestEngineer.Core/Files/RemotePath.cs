namespace TestEngineer.Core.Files;

/// <summary>面包屑中的一段。</summary>
public sealed record BreadcrumbSegment(string Name, string Path);

/// <summary>设备端路径工具。设备路径始终使用 '/'，区分大小写。</summary>
public static class RemotePath
{
    public const string Root = "/";

    /// <summary>折叠重复的 '/'、去掉末尾 '/'（根目录除外），并解析 '.' 与 '..'。</summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Root;
        }

        var parts = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            parts.Add(segment);
        }

        return parts.Count == 0 ? Root : "/" + string.Join('/', parts);
    }

    public static string Combine(string directory, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var dir = Normalize(directory);
        return dir == Root ? Normalize("/" + name) : Normalize(dir + "/" + name);
    }

    public static string GetParent(string path)
    {
        var normalized = Normalize(path);
        if (normalized == Root)
        {
            return Root;
        }

        var index = normalized.LastIndexOf('/');
        return index <= 0 ? Root : normalized[..index];
    }

    public static string GetName(string path)
    {
        var normalized = Normalize(path);
        return normalized == Root ? Root : normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    /// <summary><paramref name="path"/> 是否位于 <paramref name="directory"/> 之下（不含自身）。</summary>
    public static bool IsUnder(string path, string directory)
    {
        var p = Normalize(path);
        var d = Normalize(directory);
        if (p == d)
        {
            return false;
        }

        return d == Root || p.StartsWith(d + "/", StringComparison.Ordinal);
    }

    /// <summary>生成从根开始的面包屑，如 /sdcard/DCIM → [/, sdcard, DCIM]。</summary>
    public static IReadOnlyList<BreadcrumbSegment> Breadcrumbs(string path)
    {
        var normalized = Normalize(path);
        var result = new List<BreadcrumbSegment> { new(Root, Root) };
        if (normalized == Root)
        {
            return result;
        }

        var current = string.Empty;
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + segment;
            result.Add(new BreadcrumbSegment(segment, current));
        }

        return result;
    }
}
