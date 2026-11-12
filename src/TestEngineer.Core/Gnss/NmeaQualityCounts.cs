namespace TestEngineer.Core.Gnss;

public sealed class NmeaQualityCounts
{
    /// <summary>quality = 4（RTK 固定）。</summary>
    public int Fixed { get; set; }

    /// <summary>quality = 5（RTK 浮点）。</summary>
    public int Float { get; set; }

    /// <summary>quality = 2（差分）。</summary>
    public int Dgps { get; set; }

    /// <summary>quality = 1（单点 GPS）。</summary>
    public int Single { get; set; }

    /// <summary>quality = 0 或空（未定位）。</summary>
    public int Invalid { get; set; }

    /// <summary>其余质量值（含 3/6/7/8 及未知）。</summary>
    public int Other { get; set; }

    /// <summary>合法 GGA 总数（比例分母）。</summary>
    public int TotalGga { get; set; }

    public int AnomalyLines { get; set; }

    /// <summary>因校验/字段问题丢弃的 GGA 行数。</summary>
    public int DroppedGga { get; set; }

    public int PrivateLines { get; set; }

    public void AddFix(FixQuality quality)
    {
        TotalGga++;
        switch (quality)
        {
            case FixQuality.RtkFixed:
                Fixed++;
                break;
            case FixQuality.RtkFloat:
                Float++;
                break;
            case FixQuality.Dgps:
                Dgps++;
                break;
            case FixQuality.Gps:
                Single++;
                break;
            case FixQuality.Invalid:
                Invalid++;
                break;
            default:
                Other++;
                break;
        }
    }

    public void Add(NmeaQualityCounts other)
    {
        Fixed += other.Fixed;
        Float += other.Float;
        Dgps += other.Dgps;
        Single += other.Single;
        Invalid += other.Invalid;
        Other += other.Other;
        TotalGga += other.TotalGga;
        AnomalyLines += other.AnomalyLines;
        DroppedGga += other.DroppedGga;
        PrivateLines += other.PrivateLines;
    }

    public NmeaQualityCounts Clone() => new()
    {
        Fixed = Fixed,
        Float = Float,
        Dgps = Dgps,
        Single = Single,
        Invalid = Invalid,
        Other = Other,
        TotalGga = TotalGga,
        AnomalyLines = AnomalyLines,
        DroppedGga = DroppedGga,
        PrivateLines = PrivateLines,
    };
}
