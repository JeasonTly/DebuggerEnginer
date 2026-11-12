using System.Globalization;

namespace TestEngineer.Core.Gnss;

public sealed class GgaFix
{
    public GgaFix(
        string time,
        TimeOnly? utcTime,
        double? latitude,
        double? longitude,
        double altitude,
        FixQuality quality,
        int qualityCode,
        int numSatellites,
        double hdop)
    {
        Time = time;
        UtcTime = utcTime;
        Latitude = latitude;
        Longitude = longitude;
        Altitude = altitude;
        Quality = quality;
        QualityCode = qualityCode;
        NumSatellites = numSatellites;
        Hdop = hdop;
    }

    /// <summary>原始 UTC 时间字段（hhmmss.ss）。</summary>
    public string Time { get; }

    public TimeOnly? UtcTime { get; }

    public double? Latitude { get; }

    public double? Longitude { get; }

    public double Altitude { get; }

    public FixQuality Quality { get; }

    public int QualityCode { get; }

    public int NumSatellites { get; }

    public double Hdop { get; }

    /// <summary>
    /// 从已通过校验的标准 GGA 语句解析。字段不足或关键字段非法时返回 null。
    /// quality=0（未定位）仍返回对象，供统计使用。
    /// </summary>
    public static GgaFix? TryParse(NmeaSentence sentence)
    {
        if (!sentence.IsStandard || !sentence.IsGga)
        {
            return null;
        }

        var fields = sentence.Fields;
        // time,lat,N/S,lon,E/W,quality,numSv,hdop,alt  → 至少 9 个字段
        if (fields.Count < 9)
        {
            return null;
        }

        try
        {
            var time = fields[0];
            var qualityCode = string.IsNullOrWhiteSpace(fields[5])
                ? 0
                : int.Parse(fields[5], CultureInfo.InvariantCulture);
            var quality = MapQuality(qualityCode);

            double? lat = null;
            double? lon = null;
            var latStr = fields[1];
            var latHem = fields[2];
            var lonStr = fields[3];
            var lonHem = fields[4];

            if (!string.IsNullOrEmpty(latStr) && !string.IsNullOrEmpty(latHem) &&
                !string.IsNullOrEmpty(lonStr) && !string.IsNullOrEmpty(lonHem))
            {
                lat = ToDecimalDegrees(latStr, latHem);
                lon = ToDecimalDegrees(lonStr, lonHem);
            }
            else if (qualityCode != 0)
            {
                // 有效定位必须有坐标
                return null;
            }

            var numSv = string.IsNullOrWhiteSpace(fields[6])
                ? 0
                : int.Parse(fields[6], CultureInfo.InvariantCulture);
            var hdop = string.IsNullOrWhiteSpace(fields[7])
                ? 99.9
                : double.Parse(fields[7], CultureInfo.InvariantCulture);
            var alt = string.IsNullOrWhiteSpace(fields[8])
                ? 0.0
                : double.Parse(fields[8], CultureInfo.InvariantCulture);

            return new GgaFix(
                time,
                TryParseUtcTime(time),
                lat,
                lon,
                alt,
                quality,
                qualityCode,
                numSv,
                hdop);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    public static FixQuality MapQuality(int code) => code switch
    {
        0 => FixQuality.Invalid,
        1 => FixQuality.Gps,
        2 => FixQuality.Dgps,
        3 => FixQuality.Pps,
        4 => FixQuality.RtkFixed,
        5 => FixQuality.RtkFloat,
        6 => FixQuality.Estimated,
        7 => FixQuality.Manual,
        8 => FixQuality.Simulation,
        _ => FixQuality.Other,
    };

    public static double ToDecimalDegrees(string coord, string hemisphere)
    {
        var hem = hemisphere.Trim().ToUpperInvariant();
        var degDigits = hem is "N" or "S" ? 2 : 3;
        if (coord.Length <= degDigits)
        {
            throw new FormatException("坐标字段过短。");
        }

        var degrees = double.Parse(coord[..degDigits], CultureInfo.InvariantCulture);
        var minutes = double.Parse(coord[degDigits..], CultureInfo.InvariantCulture);
        var decimalDegrees = degrees + minutes / 60.0;
        if (hem is "S" or "W")
        {
            decimalDegrees = -decimalDegrees;
        }

        return decimalDegrees;
    }

    public static TimeOnly? TryParseUtcTime(string time)
    {
        if (string.IsNullOrWhiteSpace(time) || time.Length < 6)
        {
            return null;
        }

        if (!int.TryParse(time.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hour) ||
            !int.TryParse(time.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minute))
        {
            return null;
        }

        var secondPart = time[4..];
        if (!double.TryParse(secondPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            hour is < 0 or > 23 ||
            minute is < 0 or > 59 ||
            seconds is < 0 or >= 60)
        {
            return null;
        }

        var wholeSeconds = (int)seconds;
        var fraction = seconds - wholeSeconds;
        var ticks = (long)Math.Round(fraction * TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero);
        return new TimeOnly(hour, minute, wholeSeconds).Add(TimeSpan.FromTicks(ticks));
    }
}
