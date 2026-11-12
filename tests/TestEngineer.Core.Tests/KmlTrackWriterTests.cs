using System.IO.Compression;
using System.Text;
using TestEngineer.Core.Abstractions;
using TestEngineer.Infrastructure.Gnss;

namespace TestEngineer.Core.Tests;

public sealed class KmlTrackWriterTests : TempDirectoryTestBase
{
    private static string FixturesNmeaDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Nmea"));

    [Fact]
    public async Task WriteAsync_Kml_ContainsLineStringAndCoordinates()
    {
        var source = Path.Combine(FixturesNmeaDir, "valid-gga-head.txt");
        Assert.True(File.Exists(source), source);

        var writer = new KmlTrackWriter();
        var written = await writer.WriteAsync(
            [
                new KmlTrackRequest
                {
                    SourcePath = source,
                    DisplayName = "valid-gga-head",
                    KmlColorAabbggrr = KmlColorPalette.GetKmlColor("red"),
                },
            ],
            new KmlWriteOptions
            {
                OutputDirectory = Root,
                Kmz = false,
                IncludePointPlacemarks = false,
            });

        Assert.Single(written);
        Assert.True(File.Exists(written[0]));
        var kml = await File.ReadAllTextAsync(written[0], Encoding.UTF8);
        Assert.Contains("<LineString>", kml, StringComparison.Ordinal);
        Assert.Contains("<coordinates>", kml, StringComparison.Ordinal);
        Assert.Contains("GMT+8", kml, StringComparison.Ordinal);
        Assert.Contains(KmlColorPalette.GetKmlColor("red"), kml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_Kmz_ContainsEmbeddedKmlWithCoordinates()
    {
        var source = Path.Combine(FixturesNmeaDir, "valid-gga-head.txt");
        var writer = new KmlTrackWriter();
        var written = await writer.WriteAsync(
            [
                new KmlTrackRequest
                {
                    SourcePath = source,
                    DisplayName = "valid-gga-head",
                    KmlColorAabbggrr = KmlColorPalette.RgbToKml("1E88E5"),
                },
            ],
            new KmlWriteOptions
            {
                OutputDirectory = Root,
                Kmz = true,
                IncludePointPlacemarks = true,
            });

        Assert.Single(written);
        Assert.EndsWith(".kmz", written[0], StringComparison.OrdinalIgnoreCase);

        using var zip = ZipFile.OpenRead(written[0]);
        var entry = Assert.Single(zip.Entries);
        Assert.EndsWith(".kml", entry.Name, StringComparison.OrdinalIgnoreCase);
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var kml = await reader.ReadToEndAsync();
        Assert.Contains("<LineString>", kml, StringComparison.Ordinal);
        Assert.Contains("<coordinates>", kml, StringComparison.Ordinal);
        Assert.Contains("GGA Points", kml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_SkipsFileWithNoTrackPoints()
    {
        var source = Path.Combine(FixturesNmeaDir, "nmea-gsv-only.txt");
        var writer = new KmlTrackWriter();
        var written = await writer.WriteAsync(
            [
                new KmlTrackRequest
                {
                    SourcePath = source,
                    DisplayName = "gsv-only",
                    KmlColorAabbggrr = KmlColorPalette.DefaultTrackColor,
                },
            ],
            new KmlWriteOptions { OutputDirectory = Root });

        Assert.Empty(written);
    }

    [Fact]
    public void ColorPalette_MatchesPythonRgbToKml()
    {
        Assert.Equal("ff3539e5", KmlColorPalette.RgbToKml("E53935"));
        Assert.Equal("ffe5881e", KmlColorPalette.RgbToKml("1E88E5"));
        Assert.Equal(20, KmlColorPalette.Colors.Count);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            KmlColorPalette.GetKmlColor("red"),
        };
        var next = KmlColorPalette.RandomUnused(used);
        Assert.DoesNotContain(next, used);
    }

    [Fact]
    public void TrackReader_FormatTimeGmtPlus8_MatchesPython()
    {
        Assert.Equal("16:34:47.00 GMT+8", NmeaTrackReader.FormatTimeGmtPlus8("083447.00"));
        Assert.Equal("00:00:00 GMT+8", NmeaTrackReader.FormatTimeGmtPlus8("160000"));
    }
}
