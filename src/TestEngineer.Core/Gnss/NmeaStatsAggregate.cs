namespace TestEngineer.Core.Gnss;

/// <summary>对 Valid / ValidWithAnomalies 文件按合法 GGA 条数加权汇总。</summary>
public sealed class NmeaStatsAggregate
{
    public NmeaQualityCounts Counts { get; init; } = new();

    public int ValidFileCount { get; init; }

    public int TotalFiles { get; init; }

    public double FixedRatio => Ratio(Counts.Fixed);

    public double FloatRatio => Ratio(Counts.Float);

    public double DgpsRatio => Ratio(Counts.Dgps);

    public double SingleRatio => Ratio(Counts.Single);

    public double InvalidRatio => Ratio(Counts.Invalid);

    public double OtherRatio => Ratio(Counts.Other);

    public static NmeaStatsAggregate FromFiles(IEnumerable<NmeaFileStats> files)
    {
        var list = files as IList<NmeaFileStats> ?? files.ToList();
        var counts = new NmeaQualityCounts();
        var valid = 0;
        foreach (var file in list)
        {
            if (!file.IsIncludedInAggregate)
            {
                continue;
            }

            valid++;
            counts.Add(file.Counts);
        }

        return new NmeaStatsAggregate
        {
            Counts = counts,
            ValidFileCount = valid,
            TotalFiles = list.Count,
        };
    }

    private double Ratio(int numerator) =>
        Counts.TotalGga == 0 ? 0.0 : (double)numerator / Counts.TotalGga;
}
