using System.Globalization;
using System.Text.RegularExpressions;
using TestEngineer.Core.Files;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb.Parsing;

/// <summary>
/// 解析 Android toybox <c>ls -l</c> 输出。支持：含空格/中文的文件名、符号链接（name -> target）、
/// 无法 stat 的条目（<c>d?????????   ? ?  ?  ?  ? name</c>）、设备文件（主/次设备号替代大小）。
/// </summary>
public static partial class LsOutputParser
{
    // mode links owner group size date [time] name
    [GeneratedRegex(
        @"^(?<mode>[-dlcbpsD][-rwxsStTl?]{9}[.+@]?)\s+(?<links>\d+|\?)\s+(?<owner>\S+)\s+(?<group>\S+)\s+(?<size>\d+,\s*\d+|\d+|\?)\s+(?:(?<date>\d{4}-\d{2}-\d{2})\s+(?<time>\d{2}:\d{2}(?::\d{2})?)|\?)\s+(?<name>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex LineRegex();

    /// <summary>解析整段输出；<paramref name="directory"/> 为被列出的目录，用于拼接绝对路径。</summary>
    public static IReadOnlyList<FileEntry> Parse(string? output, string directory)
    {
        var entries = new List<FileEntry>();
        if (string.IsNullOrEmpty(output))
        {
            return entries;
        }

        foreach (var line in output.Split('\n'))
        {
            var entry = ParseLine(line, directory);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>解析单行；不是条目行（total、错误信息、. 与 ..）时返回 null。</summary>
    public static FileEntry? ParseLine(string? rawLine, string directory)
    {
        var line = rawLine?.TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var match = LineRegex().Match(line.TrimStart());
        if (!match.Success)
        {
            return null;
        }

        var mode = match.Groups["mode"].Value;
        var name = match.Groups["name"].Value;
        var isSymlink = mode[0] == 'l';
        string? linkTarget = null;
        if (isSymlink)
        {
            var arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow > 0)
            {
                linkTarget = name[(arrow + 4)..];
                name = name[..arrow];
            }
        }

        if (name is "." or "..")
        {
            return null;
        }

        var sizeText = match.Groups["size"].Value;
        long size = long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSize) ? parsedSize : -1;

        DateTime? mtime = null;
        if (match.Groups["date"].Success)
        {
            var stamp = $"{match.Groups["date"].Value} {match.Groups["time"].Value}";
            if (DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
            {
                mtime = parsedTime;
            }
        }

        return new FileEntry(
            name,
            RemotePath.Combine(directory, name),
            mode[0] == 'd',
            mode,
            size,
            mtime)
        {
            IsSymlink = isSymlink,
            LinkTarget = linkTarget,
            Owner = match.Groups["owner"].Value,
            Group = match.Groups["group"].Value,
        };
    }
}
