namespace TestEngineer.Core.Gnss;

public enum NmeaLineClass
{
    Ignored,
    Private,
    Standard,
    Anomaly,
}

public sealed class NmeaLineClassification
{
    public required NmeaLineClass Class { get; init; }

    public NmeaSentence? Sentence { get; init; }

    public GgaFix? Gga { get; init; }

    public string? AnomalyReason { get; init; }

    public bool IsDroppedGga { get; init; }

    /// <summary>末行截断：应计入忽略，不记异常。</summary>
    public bool IsTruncatedLastLine { get; init; }
}

/// <summary>单行 NMEA 分类（纯内存，无 IO）。</summary>
public static class NmeaLineClassifier
{
    public static NmeaLineClassification Classify(string line, bool isLastLine = false)
    {
        var sentence = NmeaSentence.TryParse(line);
        if (sentence is null)
        {
            return new NmeaLineClassification { Class = NmeaLineClass.Ignored };
        }

        if (sentence.IsPrivate)
        {
            return new NmeaLineClassification
            {
                Class = NmeaLineClass.Private,
                Sentence = sentence,
            };
        }

        if (sentence.IsInvalidStandard)
        {
            if (isLastLine && sentence.TruncationReason is not null)
            {
                return new NmeaLineClassification
                {
                    Class = NmeaLineClass.Ignored,
                    Sentence = sentence,
                    IsTruncatedLastLine = true,
                    AnomalyReason = sentence.TruncationReason,
                };
            }

            var reason = sentence.TruncationReason ??
                         (sentence.IsGga ? "GGA 校验和错误" : "标准语句校验和错误");
            return new NmeaLineClassification
            {
                Class = NmeaLineClass.Anomaly,
                Sentence = sentence,
                AnomalyReason = reason,
                IsDroppedGga = sentence.IsGga,
            };
        }

        // 合法标准句
        if (sentence.IsGga)
        {
            var gga = GgaFix.TryParse(sentence);
            if (gga is null)
            {
                if (isLastLine && LooksLikeTruncatedFields(sentence))
                {
                    return new NmeaLineClassification
                    {
                        Class = NmeaLineClass.Ignored,
                        Sentence = sentence,
                        IsTruncatedLastLine = true,
                        AnomalyReason = "GGA 字段不足或非法（末行截断）",
                    };
                }

                return new NmeaLineClassification
                {
                    Class = NmeaLineClass.Anomaly,
                    Sentence = sentence,
                    AnomalyReason = "GGA 字段不足或非法",
                    IsDroppedGga = true,
                };
            }

            return new NmeaLineClassification
            {
                Class = NmeaLineClass.Standard,
                Sentence = sentence,
                Gga = gga,
            };
        }

        return new NmeaLineClassification
        {
            Class = NmeaLineClass.Standard,
            Sentence = sentence,
        };
    }

    private static bool LooksLikeTruncatedFields(NmeaSentence sentence) =>
        sentence.Fields.Count < 9;
}
