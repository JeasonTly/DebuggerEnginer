using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TestEngineer.Core.Gnss;

namespace TestEngineer.Infrastructure.Gnss;

/// <summary>从 NMEA 文件流式提取可用于轨迹的 GGA 点（quality≠0 且有 lat/lon）。</summary>
public static class NmeaTrackReader
{
    private static readonly Encoding Latin1 = Encoding.Latin1;
    private static readonly Regex SecondsPattern = new(@"^\d{2}(\.\d+)?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<GgaFix> ReadTrackPoints(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadTrackPoints(stream);
    }

    public static IReadOnlyList<GgaFix> ReadTrackPoints(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(
            stream,
            Latin1,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        return ReadTrackPoints(reader);
    }

    public static IReadOnlyList<GgaFix> ReadTrackPoints(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var points = new List<GgaFix>();
        while (reader.ReadLine() is { } line)
        {
            if (TryReadTrackPoint(line) is { } point)
            {
                points.Add(point);
            }
        }

        return points;
    }

    public static GgaFix? TryReadTrackPoint(string line)
    {
        var sentence = NmeaSentence.TryParse(line);
        if (sentence is null || !sentence.IsStandard || !sentence.IsGga)
        {
            return null;
        }

        var fix = GgaFix.TryParse(sentence);
        if (fix is null ||
            fix.QualityCode == 0 ||
            fix.Latitude is null ||
            fix.Longitude is null)
        {
            return null;
        }

        return fix;
    }

    /// <summary>与 nmea_to_kml.py <c>format_time</c> 一致：UTC → GMT+8 显示。</summary>
    public static string FormatTimeGmtPlus8(string timeStr)
    {
        try
        {
            if (string.IsNullOrEmpty(timeStr) || timeStr.Length < 6)
            {
                return timeStr;
            }

            if (!int.TryParse(timeStr.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hour))
            {
                return timeStr;
            }

            var minute = timeStr[2..4];
            var second = timeStr[4..];
            if (!minute.All(char.IsDigit) || !SecondsPattern.IsMatch(second))
            {
                return timeStr;
            }

            hour = (hour + 8) % 24;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{hour:00}:{minute}:{second} GMT+8");
        }
        catch
        {
            return timeStr;
        }
    }
}
