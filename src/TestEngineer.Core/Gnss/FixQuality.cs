namespace TestEngineer.Core.Gnss;

/// <summary>NMEA GGA 定位质量（field 6）。</summary>
public enum FixQuality
{
    Invalid = 0,
    Gps = 1,
    Dgps = 2,
    Pps = 3,
    RtkFixed = 4,
    RtkFloat = 5,
    Estimated = 6,
    Manual = 7,
    Simulation = 8,
    Other = 9,
}
