using TestEngineer.Core.Gnss;

namespace TestEngineer.Core.Tests;

public sealed class NmeaFileAnalyzerTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Nmea", name);

    [Fact]
    public void ValidGgaHead_HasFixedSolution()
    {
        var stats = NmeaFileAnalyzer.AnalyzeFile(Fixture("valid-gga-head.txt"));

        Assert.Equal(NmeaFileVerdict.Valid, stats.Verdict);
        Assert.True(stats.Counts.TotalGga >= 1);
        Assert.True(stats.Counts.Fixed >= 1);
        Assert.Equal(0, stats.Counts.AnomalyLines);
        Assert.Equal(0, stats.Counts.PrivateLines);
        Assert.NotNull(stats.FirstUtcTime);
        Assert.Equal(new TimeOnly(8, 34, 47), stats.FirstUtcTime);
    }

    [Fact]
    public void NmeaGsvOnly_IsNoGga()
    {
        var stats = NmeaFileAnalyzer.AnalyzeFile(Fixture("nmea-gsv-only.txt"));

        Assert.Equal(NmeaFileVerdict.NoGga, stats.Verdict);
        Assert.Equal(0, stats.Counts.TotalGga);
        Assert.True(stats.StandardSentenceCount > 0);
    }

    [Fact]
    public void EvkMixedHead_IsValidWithPrivateLines()
    {
        var stats = NmeaFileAnalyzer.AnalyzeFile(Fixture("evk-mixed-head.txt"));

        Assert.Equal(NmeaFileVerdict.Valid, stats.Verdict);
        Assert.True(stats.Counts.TotalGga >= 1);
        Assert.True(stats.Counts.Dgps >= 1);
        Assert.True(stats.Counts.PrivateLines > 0);
        Assert.Equal(0, stats.Counts.AnomalyLines);
    }

    [Fact]
    public void F8QxWithNack_PrivateNotAnomaly()
    {
        var stats = NmeaFileAnalyzer.AnalyzeFile(Fixture("f8-qx-with-nack.txt"));

        Assert.True(stats.Verdict is NmeaFileVerdict.Valid or NmeaFileVerdict.ValidWithAnomalies);
        Assert.True(stats.Counts.TotalGga >= 1);
        Assert.True(stats.Counts.Float >= 1);
        Assert.True(stats.Counts.PrivateLines > 0);
        Assert.Equal(0, stats.Counts.AnomalyLines);
        Assert.Equal(NmeaFileVerdict.Valid, stats.Verdict);
    }

    [Fact]
    public void BadChecksumGga_ValidWithAnomalies_KeepsGoodLines()
    {
        var stats = NmeaFileAnalyzer.AnalyzeFile(Fixture("bad-checksum-gga.txt"));

        Assert.Equal(NmeaFileVerdict.ValidWithAnomalies, stats.Verdict);
        Assert.True(stats.Counts.DroppedGga >= 1);
        Assert.True(stats.Counts.AnomalyLines >= 1);
        Assert.True(stats.Counts.TotalGga >= 1);
        Assert.True(stats.Counts.Float >= 1);
        Assert.Contains(stats.AnomalySamples, s => s.LineNumber == 1);
    }

    [Fact]
    public void TruncatedLastLine_DoesNotCountAsAnomaly()
    {
        var stats = NmeaFileAnalyzer.AnalyzeFile(Fixture("truncated-last-line.txt"));

        Assert.Equal(NmeaFileVerdict.Valid, stats.Verdict);
        Assert.Equal(0, stats.Counts.AnomalyLines);
        Assert.Equal(0, stats.Counts.DroppedGga);
        Assert.Equal(1, stats.Counts.TotalGga);
        Assert.Equal(1, stats.Counts.Fixed);
    }

    [Fact]
    public void TimestampPrefix_CanParse()
    {
        var stats = NmeaFileAnalyzer.AnalyzeFile(Fixture("timestamp-prefix.txt"));

        Assert.Equal(NmeaFileVerdict.Valid, stats.Verdict);
        Assert.Equal(1, stats.Counts.TotalGga);
        Assert.Equal(1, stats.Counts.Fixed);
        Assert.Equal(new TimeOnly(8, 37, 16), stats.FirstUtcTime);
    }

    [Fact]
    public void BinaryFake_IsNotNmea()
    {
        var stats = NmeaFileAnalyzer.AnalyzeFile(Fixture("binary-fake.txt"));

        Assert.Equal(NmeaFileVerdict.NotNmea, stats.Verdict);
        Assert.Equal(0, stats.Counts.TotalGga);
    }
}

public sealed class NmeaSentenceTests
{
    [Fact]
    public void Checksum_XorBetweenDollarAndStar()
    {
        const string line = "$GBGGA,083447.00,2234.9749540,N,11406.6856557,E,4,12,1.4,16.7355,M,-2.896,M,1.0,1686*41";
        var sentence = NmeaSentence.TryParse(line);

        Assert.NotNull(sentence);
        Assert.True(sentence!.IsStandard);
        Assert.Equal("GB", sentence.Talker);
        Assert.Equal("GGA", sentence.SentenceType);
        Assert.True(sentence.ChecksumValid);
    }

    [Fact]
    public void PrivateQx_IsPrivateNotAnomaly()
    {
        var classification = NmeaLineClassifier.Classify("$QXSTAT,0,36,7");
        Assert.Equal(NmeaLineClass.Private, classification.Class);
    }

    [Fact]
    public void QualityZero_StillReturnsGgaFix()
    {
        // 构造 quality=0 合法校验和语句
        var payload = "GPGGA,000000.00,,,,,0,00,99.9,,M,,M,,";
        var xor = NmeaSentence.ComputeChecksumXor(payload);
        var line = $"${payload}*{xor:X2}";
        var sentence = NmeaSentence.TryParse(line);
        Assert.NotNull(sentence);
        Assert.True(sentence!.IsStandard);

        var gga = GgaFix.TryParse(sentence);
        Assert.NotNull(gga);
        Assert.Equal(FixQuality.Invalid, gga!.Quality);
        Assert.Equal(0, gga.QualityCode);
        Assert.Null(gga.Latitude);
    }
}

public sealed class NmeaStatsAggregateTests
{
    [Fact]
    public void FromFiles_WeightsByValidGgaCount()
    {
        var a = new NmeaFileStats
        {
            Verdict = NmeaFileVerdict.Valid,
            Counts = new NmeaQualityCounts { Fixed = 3, TotalGga = 3 },
        };
        var b = new NmeaFileStats
        {
            Verdict = NmeaFileVerdict.ValidWithAnomalies,
            Counts = new NmeaQualityCounts { Float = 1, TotalGga = 1, AnomalyLines = 2 },
        };
        var skipped = new NmeaFileStats
        {
            Verdict = NmeaFileVerdict.NoGga,
            Counts = new NmeaQualityCounts { PrivateLines = 10 },
        };

        var agg = NmeaStatsAggregate.FromFiles([a, b, skipped]);

        Assert.Equal(2, agg.ValidFileCount);
        Assert.Equal(4, agg.Counts.TotalGga);
        Assert.Equal(3, agg.Counts.Fixed);
        Assert.Equal(1, agg.Counts.Float);
        Assert.Equal(0.75, agg.FixedRatio);
        Assert.Equal(0.25, agg.FloatRatio);
    }
}
