namespace TestEngineer.Core.Gnss;

public sealed record NmeaAnomalySample(int LineNumber, string Reason, string Excerpt);

public sealed class NmeaFileStats
{
    public const int MaxAnomalySamples = 20;

    public string Path { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public NmeaFileVerdict Verdict { get; init; }

    public NmeaQualityCounts Counts { get; init; } = new();

    public IReadOnlyList<NmeaAnomalySample> AnomalySamples { get; init; } = Array.Empty<NmeaAnomalySample>();

    public TimeOnly? FirstUtcTime { get; init; }

    public TimeOnly? LastUtcTime { get; init; }

    public int StandardSentenceCount { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsIncludedInAggregate =>
        Verdict is NmeaFileVerdict.Valid or NmeaFileVerdict.ValidWithAnomalies;
}
