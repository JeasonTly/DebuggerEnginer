using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Gnss;
using TestEngineer.Infrastructure.Gnss;

namespace TestEngineer.Core.Tests;

/// <summary>只读扫描真实采集目录；KML 只写到临时目录，绝不写回 Nmea2Kmz。</summary>
public sealed class NmeaAcceptanceSmokeTests
{
    private const string RealRoot = @"C:\Users\Tuly\Desktop\工作文件\工作工具\Nmea2Kmz";

    [Fact]
    public async Task Scan_RealNmea2Kmz_TxtDefault_AndKmlToTemp()
    {
        if (!Directory.Exists(RealRoot))
        {
            return;
        }

        var analyzer = new NmeaLogAnalyzer();
        var result = await analyzer.ScanAsync(
            [RealRoot],
            new NmeaScanOptions
            {
                IncludeSubdirectories = true,
                ExtraExtensions = [],
            });

        var txtFiles = result.Files
            .Where(f => f.Path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(txtFiles.Count >= 50, $"expected many txt, got {txtFiles.Count}");

        var valid = txtFiles.Where(f => f.IsIncludedInAggregate).ToList();
        Assert.True(valid.Count >= 40, $"expected many valid, got {valid.Count}");
        Assert.True(result.Aggregate.Counts.TotalGga > 10_000, $"expected large GGA count, got {result.Aggregate.Counts.TotalGga}");

        // 扩展名开关：加上 .log 应不少于仅 txt
        var withLog = await analyzer.ScanAsync(
            [RealRoot],
            new NmeaScanOptions
            {
                IncludeSubdirectories = true,
                ExtraExtensions = [".log", ".nmea"],
            });
        Assert.True(withLog.Files.Count >= result.Files.Count);

        var outDir = Path.Combine(Path.GetTempPath(), "TestEngineer-NmeaSmoke", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(outDir);

        var sample = valid.Take(3).ToList();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tracks = sample.Select(f =>
        {
            var color = KmlColorPalette.RandomUnused(used);
            used.Add(color);
            return new KmlTrackRequest
            {
                SourcePath = f.Path,
                DisplayName = f.RelativePath,
                KmlColorAabbggrr = color,
            };
        }).ToList();

        var writer = new KmlTrackWriter();
        var written = await writer.WriteAsync(
            tracks,
            new KmlWriteOptions
            {
                OutputDirectory = outDir,
                Kmz = false,
                AbsoluteAltitude = false,
                IncludePointPlacemarks = false,
            });

        Assert.NotEmpty(written);
        Assert.All(written, p =>
        {
            Assert.StartsWith(outDir, p, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(p));
            var text = File.ReadAllText(p);
            Assert.Contains("<LineString>", text, StringComparison.Ordinal);
            Assert.Contains("<coordinates>", text, StringComparison.Ordinal);
        });

        // 确认未在真实目录写入本次冒烟产物
        foreach (var path in written)
        {
            Assert.DoesNotContain(RealRoot, path, StringComparison.OrdinalIgnoreCase);
        }
    }
}
