using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Gnss;

namespace TestEngineer.Infrastructure.Gnss;

public sealed class KmlTrackWriter : IKmlTrackWriter
{
    private static readonly Dictionary<int, string> QualityLabels = new()
    {
        [0] = "无效",
        [1] = "GPS",
        [2] = "DGPS",
        [3] = "PPS",
        [4] = "RTK固定",
        [5] = "RTK浮动",
        [6] = "推算",
        [7] = "手动",
        [8] = "模拟",
    };

    public async Task<IReadOnlyList<string>> WriteAsync(
        IEnumerable<KmlTrackRequest> tracks,
        KmlWriteOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(options);

        if (options.OutputDirectory is { } outDir)
        {
            Directory.CreateDirectory(outDir);
        }

        var written = new List<string>();
        foreach (var track in tracks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = await WriteOneAsync(track, options, cancellationToken).ConfigureAwait(false);
            if (path is not null)
            {
                written.Add(path);
            }
        }

        return written;
    }

    private static async Task<string?> WriteOneAsync(
        KmlTrackRequest track,
        KmlWriteOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(track.SourcePath) || !File.Exists(track.SourcePath))
        {
            return null;
        }

        var points = await Task.Run(
                () => NmeaTrackReader.ReadTrackPoints(track.SourcePath),
                cancellationToken)
            .ConfigureAwait(false);

        if (points.Count == 0)
        {
            return null;
        }

        var displayName = string.IsNullOrWhiteSpace(track.DisplayName)
            ? Path.GetFileNameWithoutExtension(track.SourcePath)
            : track.DisplayName;
        var sourceLabel = Path.GetFileName(track.SourcePath);
        var altMode = options.AbsoluteAltitude ? "absolute" : "clampToGround";
        var color = string.IsNullOrWhiteSpace(track.KmlColorAabbggrr)
            ? KmlColorPalette.DefaultTrackColor
            : track.KmlColorAabbggrr.Trim().ToLowerInvariant();

        var kmlBytes = BuildKml(points, displayName, sourceLabel, color, altMode, options.IncludePointPlacemarks);
        var outputPath = ResolveOutputPath(track.SourcePath, options);
        await SaveAsync(kmlBytes, outputPath, options.Kmz, cancellationToken).ConfigureAwait(false);
        return outputPath;
    }

    private static string ResolveOutputPath(string sourcePath, KmlWriteOptions options)
    {
        var ext = options.Kmz ? ".kmz" : ".kml";
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (options.OutputDirectory is { } dir)
        {
            return Path.Combine(dir, baseName + ext);
        }

        var directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(directory))
        {
            return baseName + ext;
        }

        return Path.Combine(directory, baseName + ext);
    }

    private static async Task SaveAsync(
        byte[] kmlBytes,
        string outputPath,
        bool kmz,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (kmz)
        {
            var kmlEntryName = Path.GetFileNameWithoutExtension(outputPath) + ".kml";
            await using var fs = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry(kmlEntryName, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(kmlBytes, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await File.WriteAllBytesAsync(outputPath, kmlBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private static byte[] BuildKml(
        IReadOnlyList<GgaFix> points,
        string name,
        string sourceFile,
        string trackColor,
        string altMode,
        bool includePoints)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("kml", "http://www.opengis.net/kml/2.2");
            writer.WriteStartElement("Document");

            writer.WriteElementString("name", name);
            writer.WriteElementString("description", $"Source: {sourceFile}");

            WriteLineStyle(writer, "trackStyle", trackColor, lineWidth: "4");
            WritePointStyle(writer, "startStyle", trackColor, iconScale: "1.2", labelScale: "1.0", labelColor: true);
            WritePointStyle(writer, "endStyle", trackColor, iconScale: "1.2", labelScale: "1.0", labelColor: true);
            WritePointStyle(writer, "pointStyle", trackColor, iconScale: "0.5", labelScale: "0", labelColor: false);

            var first = points[0];
            var last = points[^1];
            var trackFileLabel = sourceFile;

            WritePlacemarkPoint(
                writer,
                $"起点 [{trackFileLabel}]",
                "#startStyle",
                DescribePoint(trackFileLabel, first),
                first);

            if (points.Count > 1)
            {
                WritePlacemarkPoint(
                    writer,
                    $"终点 [{trackFileLabel}]",
                    "#endStyle",
                    DescribePoint(trackFileLabel, last),
                    last);
            }

            writer.WriteStartElement("Placemark");
            writer.WriteElementString("name", $"轨迹 [{name}]");
            writer.WriteElementString("styleUrl", "#trackStyle");
            writer.WriteElementString(
                "description",
                $"File: {trackFileLabel}\n" +
                $"定位点数: {points.Count}\n" +
                $"起始时间: {NmeaTrackReader.FormatTimeGmtPlus8(first.Time)}\n" +
                $"结束时间: {NmeaTrackReader.FormatTimeGmtPlus8(last.Time)}");
            writer.WriteStartElement("LineString");
            writer.WriteElementString("tessellate", "1");
            writer.WriteElementString("altitudeMode", altMode);
            writer.WriteStartElement("coordinates");
            var sb = new StringBuilder();
            foreach (var p in points)
            {
                sb.Append(FormatCoordinate(p)).Append('\n');
            }

            writer.WriteString(sb.ToString().TrimEnd('\n'));
            writer.WriteEndElement(); // coordinates
            writer.WriteEndElement(); // LineString
            writer.WriteEndElement(); // Placemark

            if (includePoints)
            {
                writer.WriteStartElement("Folder");
                writer.WriteElementString("name", "GGA Points");
                writer.WriteElementString("description", $"All {points.Count} GGA fix points with details");
                for (var i = 0; i < points.Count; i++)
                {
                    var p = points[i];
                    var time = NmeaTrackReader.FormatTimeGmtPlus8(p.Time);
                    writer.WriteStartElement("Placemark");
                    writer.WriteElementString("name", $"#{i + 1}  {time}");
                    writer.WriteElementString("styleUrl", "#pointStyle");
                    writer.WriteElementString(
                        "description",
                        $"序号: {i + 1}/{points.Count}\n" +
                        $"时间: {time}\n" +
                        $"纬度: {p.Latitude!.Value.ToString("0.0000000", CultureInfo.InvariantCulture)}\n" +
                        $"经度: {p.Longitude!.Value.ToString("0.0000000", CultureInfo.InvariantCulture)}\n" +
                        $"海拔: {p.Altitude.ToString("0.0", CultureInfo.InvariantCulture)} m\n" +
                        $"卫星数: {p.NumSatellites}\n" +
                        $"HDOP: {p.Hdop.ToString("0.0", CultureInfo.InvariantCulture)}\n" +
                        $"定位类型: {QualityLabel(p.QualityCode)}");
                    writer.WriteStartElement("Point");
                    writer.WriteElementString("coordinates", FormatCoordinate(p));
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }

                writer.WriteEndElement(); // Folder
            }

            writer.WriteEndElement(); // Document
            writer.WriteEndElement(); // kml
            writer.WriteEndDocument();
        }

        return ms.ToArray();
    }

    private static void WriteLineStyle(XmlWriter writer, string id, string color, string lineWidth)
    {
        writer.WriteStartElement("Style");
        writer.WriteAttributeString("id", id);
        writer.WriteStartElement("LineStyle");
        writer.WriteElementString("color", color);
        writer.WriteElementString("width", lineWidth);
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WritePointStyle(
        XmlWriter writer,
        string id,
        string color,
        string iconScale,
        string labelScale,
        bool labelColor)
    {
        writer.WriteStartElement("Style");
        writer.WriteAttributeString("id", id);
        writer.WriteStartElement("IconStyle");
        writer.WriteElementString("color", color);
        writer.WriteElementString("scale", iconScale);
        writer.WriteEndElement();
        writer.WriteStartElement("LabelStyle");
        if (labelColor)
        {
            writer.WriteElementString("color", color);
        }

        writer.WriteElementString("scale", labelScale);
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WritePlacemarkPoint(
        XmlWriter writer,
        string name,
        string styleUrl,
        string description,
        GgaFix point)
    {
        writer.WriteStartElement("Placemark");
        writer.WriteElementString("name", name);
        writer.WriteElementString("styleUrl", styleUrl);
        writer.WriteElementString("description", description);
        writer.WriteStartElement("Point");
        writer.WriteElementString("coordinates", FormatCoordinate(point));
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static string DescribePoint(string trackFileLabel, GgaFix p)
    {
        var time = NmeaTrackReader.FormatTimeGmtPlus8(p.Time);
        return
            $"File: {trackFileLabel}\n" +
            $"时间: {time}\n" +
            $"纬度: {p.Latitude!.Value.ToString("0.0000000", CultureInfo.InvariantCulture)}\n" +
            $"经度: {p.Longitude!.Value.ToString("0.0000000", CultureInfo.InvariantCulture)}\n" +
            $"海拔: {p.Altitude.ToString("0.0", CultureInfo.InvariantCulture)} m\n" +
            $"卫星数: {p.NumSatellites}  HDOP: {p.Hdop.ToString("0.0", CultureInfo.InvariantCulture)}\n" +
            $"定位类型: {QualityLabel(p.QualityCode)}";
    }

    private static string FormatCoordinate(GgaFix p) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{p.Longitude!.Value:0.0000000},{p.Latitude!.Value:0.0000000},{p.Altitude:0.0}");

    private static string QualityLabel(int code) =>
        QualityLabels.TryGetValue(code, out var label) ? label : code.ToString(CultureInfo.InvariantCulture);
}
