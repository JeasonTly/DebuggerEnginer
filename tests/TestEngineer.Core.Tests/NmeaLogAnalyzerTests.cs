using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Gnss;
using TestEngineer.Infrastructure.Gnss;

namespace TestEngineer.Core.Tests;

public sealed class NmeaLogAnalyzerTests
{
    private static string FixturesNmeaDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Nmea"));

    [Fact]
    public async Task ScanAsync_FixturesDirectory_FindsTxtAndAggregates()
    {
        var analyzer = new NmeaLogAnalyzer();
        var progressReports = new List<NmeaScanProgress>();
        var progress = new Progress<NmeaScanProgress>(p => progressReports.Add(p));

        var result = await analyzer.ScanAsync(
            [FixturesNmeaDir],
            new NmeaScanOptions(),
            progress);

        Assert.True(result.Files.Count >= 5, $"expected fixtures, got {result.Files.Count}");
        Assert.All(result.Files, f => Assert.False(string.IsNullOrWhiteSpace(f.RelativePath)));
        Assert.Contains(result.Files, f => f.Verdict is NmeaFileVerdict.Valid or NmeaFileVerdict.ValidWithAnomalies);
        Assert.Equal(result.Files.Count, result.Aggregate.TotalFiles);
        Assert.True(progressReports.Count > 0);
        Assert.Contains(progressReports, p => p.FilesDiscovered > 0);
        Assert.Contains(progressReports, p => p.FilesProcessed > 0);
    }

    [Fact]
    public async Task ScanAsync_SingleFileRoot_UsesFileNameAsRelativePath()
    {
        var path = Path.Combine(FixturesNmeaDir, "valid-gga-head.txt");
        Assert.True(File.Exists(path), path);

        var result = await new NmeaLogAnalyzer().ScanAsync(
            [path],
            new NmeaScanOptions());

        Assert.Single(result.Files);
        Assert.Equal("valid-gga-head.txt", result.Files[0].RelativePath);
        Assert.Equal(NmeaFileVerdict.Valid, result.Files[0].Verdict);
    }

    [Fact]
    public async Task ScanAsync_DeduplicatesAbsolutePaths()
    {
        var path = Path.Combine(FixturesNmeaDir, "valid-gga-head.txt");
        var result = await new NmeaLogAnalyzer().ScanAsync(
            [path, path],
            new NmeaScanOptions());

        Assert.Single(result.Files);
    }

    [Fact]
    public async Task ScanAsync_RealFieldTestDir_DiscoversMultipleTxt_WhenPresent()
    {
        const string realDir = @"C:\Users\Tuly\Desktop\工作文件\工作工具\Nmea2Kmz\20260908场测";
        if (!Directory.Exists(realDir))
        {
            return; // 冒烟：目录不在本机则跳过
        }

        var result = await new NmeaLogAnalyzer().ScanAsync(
            [realDir],
            new NmeaScanOptions { IncludeSubdirectories = true });

        var txtCount = result.Files.Count(f =>
            f.Path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        Assert.True(txtCount >= 2, $"expected multiple txt under field-test dir, got {txtCount}");
    }
}
