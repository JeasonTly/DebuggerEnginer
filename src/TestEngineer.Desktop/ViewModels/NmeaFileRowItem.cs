using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TestEngineer.Core.Gnss;

namespace TestEngineer.Desktop.ViewModels;

public sealed partial class NmeaFileRowItem : ObservableObject
{
    public NmeaFileRowItem(NmeaFileStats stats)
    {
        Stats = stats;
        RelativePath = stats.RelativePath;
        FullPath = stats.Path;
        Verdict = stats.Verdict;
        StatusText = FormatVerdict(stats);
        GgaCount = stats.Counts.TotalGga;
        AnomalyCount = stats.Counts.AnomalyLines;
        FixedPercent = Percent(stats.Counts.Fixed, stats.Counts.TotalGga);
        FloatPercent = Percent(stats.Counts.Float, stats.Counts.TotalGga);
        DgpsPercent = Percent(stats.Counts.Dgps, stats.Counts.TotalGga);
        SinglePercent = Percent(stats.Counts.Single, stats.Counts.TotalGga);
        InvalidPercent = Percent(stats.Counts.Invalid, stats.Counts.TotalGga);
        OtherPercent = Percent(stats.Counts.Other, stats.Counts.TotalGga);

        FixedStar = Star(stats.Counts.Fixed, stats.Counts.TotalGga);
        FloatStar = Star(stats.Counts.Float, stats.Counts.TotalGga);
        DgpsStar = Star(stats.Counts.Dgps, stats.Counts.TotalGga);
        SingleStar = Star(stats.Counts.Single, stats.Counts.TotalGga);
        InvalidStar = Star(stats.Counts.Invalid, stats.Counts.TotalGga);
        OtherStar = Star(stats.Counts.Other, stats.Counts.TotalGga);

        TimeRangeText = FormatTimeRange(stats.FirstUtcTime, stats.LastUtcTime);
        AnomalySamples = stats.AnomalySamples;
        IsValid = stats.IsIncludedInAggregate;
        HasAnomalies = stats.Verdict == NmeaFileVerdict.ValidWithAnomalies || stats.Counts.AnomalyLines > 0;
        IsSkipped = !stats.IsIncludedInAggregate;
        HasDgps = stats.Counts.Dgps > 0;
    }

    public NmeaFileStats Stats { get; }

    public string RelativePath { get; }

    public string FullPath { get; }

    public NmeaFileVerdict Verdict { get; }

    public string StatusText { get; }

    public int GgaCount { get; }

    public int AnomalyCount { get; }

    public string FixedPercent { get; }

    public string FloatPercent { get; }

    public string DgpsPercent { get; }

    public string SinglePercent { get; }

    public string InvalidPercent { get; }

    public string OtherPercent { get; }

    public GridLength FixedStar { get; }

    public GridLength FloatStar { get; }

    public GridLength DgpsStar { get; }

    public GridLength SingleStar { get; }

    public GridLength InvalidStar { get; }

    public GridLength OtherStar { get; }

    public string TimeRangeText { get; }

    public IReadOnlyList<NmeaAnomalySample> AnomalySamples { get; }

    public bool IsValid { get; }

    public bool HasAnomalies { get; }

    public bool IsSkipped { get; }

    public bool HasDgps { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(RelativePath)
        ? Path.GetFileName(FullPath)
        : RelativePath;

    private static string Percent(int count, int total) =>
        total <= 0 ? "—" : $"{100.0 * count / total:0.0}%";

    private static GridLength Star(int count, int total)
    {
        if (total <= 0 || count <= 0)
        {
            return new GridLength(0);
        }

        return new GridLength(count, GridUnitType.Star);
    }

    private static string FormatVerdict(NmeaFileStats stats) => stats.Verdict switch
    {
        NmeaFileVerdict.Valid => "有效",
        NmeaFileVerdict.ValidWithAnomalies => $"有效·含异常({stats.Counts.AnomalyLines})",
        NmeaFileVerdict.NoGga => "跳过·无 GGA",
        NmeaFileVerdict.NotNmea => "跳过·非 NMEA",
        NmeaFileVerdict.ReadFailed => "读取失败",
        _ => stats.Verdict.ToString(),
    };

    private static string FormatTimeRange(TimeOnly? first, TimeOnly? last)
    {
        if (first is null && last is null)
        {
            return "—";
        }

        var a = first?.ToString("HH:mm:ss") ?? "—";
        var b = last?.ToString("HH:mm:ss") ?? "—";
        return $"{a} – {b} UTC";
    }
}

/// <summary>汇总区横向条分段。</summary>
public sealed class NmeaShareSegment
{
    public required string Label { get; init; }

    public required string PercentText { get; init; }

    public required GridLength Star { get; init; }

    public required Brush Fill { get; init; }

    public bool HasWidth => Star.GridUnitType == GridUnitType.Star && Star.Value > 0;
}

public enum NmeaFileFilterKind
{
    All,
    Valid,
    Skipped,
    Anomalies,
}

public enum NmeaResultViewMode
{
    Table,
    Chart,
}
