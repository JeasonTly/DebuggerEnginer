namespace TestEngineer.Core.Models;

/// <summary>设备文件系统中的一个条目。</summary>
/// <param name="Name">文件名（不含路径）。</param>
/// <param name="AbsolutePath">设备上的绝对路径（'/' 分隔，已规范化）。</param>
/// <param name="IsDirectory">是否目录（符号链接需探测后才准确）。</param>
/// <param name="Mode">ls 的权限串，如 drwxrws---；无法 stat 时为 d?????????。</param>
/// <param name="Size">字节数；未知为 -1。</param>
/// <param name="MTime">修改时间（设备本地时间，无时区）；未知为 null。</param>
/// <param name="CanRead"><c>test -r</c> 结果；未探测前为 true。</param>
/// <param name="CanWrite"><c>test -w</c> 结果；未探测前为 false。</param>
public sealed record FileEntry(
    string Name,
    string AbsolutePath,
    bool IsDirectory,
    string Mode,
    long Size,
    DateTime? MTime,
    bool CanRead = true,
    bool CanWrite = false)
{
    public bool IsSymlink { get; init; }

    public string? LinkTarget { get; init; }

    public string? Owner { get; init; }

    public string? Group { get; init; }

    /// <summary>stat 失败（权限受限）的条目，如 <c>d?????????</c>。</summary>
    public bool IsUnknownMetadata => Mode.Contains('?');
}
