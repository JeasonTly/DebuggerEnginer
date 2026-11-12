namespace TestEngineer.Core.Gnss;

public enum NmeaFileVerdict
{
    Valid,
    ValidWithAnomalies,
    NoGga,
    NotNmea,
    ReadFailed,
}
