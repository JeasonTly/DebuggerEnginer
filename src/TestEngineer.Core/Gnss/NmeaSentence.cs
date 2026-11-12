using System.Globalization;

namespace TestEngineer.Core.Gnss;

public enum NmeaSentenceKind
{
    /// <summary>无 `$` 或无法识别为语句。</summary>
    None,

    /// <summary>标准头（2 字母 talker + 3 字母类型）且校验和合法。</summary>
    Standard,

    /// <summary>厂商私有/调试语句（头不符合标准形态）。</summary>
    Private,

    /// <summary>标准头但校验和缺失或错误，或其它非法标准句。</summary>
    InvalidStandard,
}

public sealed class NmeaSentence
{
    private NmeaSentence(
        NmeaSentenceKind kind,
        string rawFromDollar,
        string? talker,
        string? sentenceType,
        string? checksumHex,
        bool checksumValid,
        string? truncationReason)
    {
        Kind = kind;
        RawFromDollar = rawFromDollar;
        Talker = talker;
        SentenceType = sentenceType;
        ChecksumHex = checksumHex;
        ChecksumValid = checksumValid;
        TruncationReason = truncationReason;
    }

    public NmeaSentenceKind Kind { get; }

    /// <summary>从第一个 `$` 起的原文（已 TrimEnd）。</summary>
    public string RawFromDollar { get; }

    public string? Talker { get; }

    public string? SentenceType { get; }

    public string? ChecksumHex { get; }

    public bool ChecksumValid { get; }

    /// <summary>若语句疑似截断（缺 `*` 或 `*HH` 不完整），给出原因；完整但校验失败则为 null。</summary>
    public string? TruncationReason { get; }

    public bool IsStandard => Kind == NmeaSentenceKind.Standard;

    public bool IsPrivate => Kind == NmeaSentenceKind.Private;

    public bool IsInvalidStandard => Kind == NmeaSentenceKind.InvalidStandard;

    public bool IsGga =>
        SentenceType is not null &&
        SentenceType.Equals("GGA", StringComparison.OrdinalIgnoreCase);

    /// <summary>地址域之后、`*` 之前按逗号拆分的字段（不含 talker+type）。</summary>
    public IReadOnlyList<string> Fields { get; private set; } = Array.Empty<string>();

    public static NmeaSentence? TryParse(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var dollar = line.IndexOf('$');
        if (dollar < 0)
        {
            return null;
        }

        var raw = line.AsSpan(dollar).TrimEnd().ToString();
        if (raw.Length < 2)
        {
            return new NmeaSentence(NmeaSentenceKind.Private, raw, null, null, null, false, "语句过短");
        }

        var body = raw.AsSpan(1); // after $
        var star = body.IndexOf('*');
        ReadOnlySpan<char> addressAndFields;
        string? checksumHex = null;
        string? truncationReason = null;

        if (star < 0)
        {
            addressAndFields = body;
            truncationReason = "缺少校验和 *HH";
        }
        else
        {
            addressAndFields = body[..star];
            var afterStar = body[(star + 1)..].TrimEnd();
            if (afterStar.Length < 2)
            {
                truncationReason = "校验和位数不足";
                checksumHex = afterStar.Length == 0 ? null : afterStar.ToString();
            }
            else
            {
                // 只取两位十六进制；其后若有多余字符仍视为完整校验域
                checksumHex = afterStar[..2].ToString();
                if (!IsHexByte(checksumHex))
                {
                    truncationReason = null; // 两位存在但非十六进制 → 非法标准（若头标准）
                    checksumHex = afterStar[..2].ToString();
                }
            }
        }

        var comma = addressAndFields.IndexOf(',');
        ReadOnlySpan<char> address = comma < 0 ? addressAndFields : addressAndFields[..comma];
        var fieldsPart = comma < 0 ? ReadOnlySpan<char>.Empty : addressAndFields[(comma + 1)..];

        if (!IsStandardAddress(address))
        {
            return new NmeaSentence(NmeaSentenceKind.Private, raw, null, null, checksumHex, false, null)
            {
                Fields = SplitFields(fieldsPart),
            };
        }

        var talker = address[..2].ToString().ToUpperInvariant();
        var type = address[2..].ToString().ToUpperInvariant();
        var fields = SplitFields(fieldsPart);

        if (truncationReason is not null)
        {
            return new NmeaSentence(
                NmeaSentenceKind.InvalidStandard,
                raw,
                talker,
                type,
                checksumHex,
                false,
                truncationReason)
            {
                Fields = fields,
            };
        }

        var expected = ComputeChecksumXor(addressAndFields);
        var checksumValid =
            checksumHex is not null &&
            int.TryParse(checksumHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var given) &&
            given == expected;

        if (!checksumValid)
        {
            return new NmeaSentence(
                NmeaSentenceKind.InvalidStandard,
                raw,
                talker,
                type,
                checksumHex,
                false,
                null)
            {
                Fields = fields,
            };
        }

        return new NmeaSentence(
            NmeaSentenceKind.Standard,
            raw,
            talker,
            type,
            checksumHex,
            true,
            null)
        {
            Fields = fields,
        };
    }

    public static byte ComputeChecksumXor(ReadOnlySpan<char> betweenDollarAndStar)
    {
        byte xor = 0;
        foreach (var ch in betweenDollarAndStar)
        {
            xor ^= (byte)ch;
        }

        return xor;
    }

    private static bool IsStandardAddress(ReadOnlySpan<char> address)
    {
        if (address.Length != 5)
        {
            return false;
        }

        for (var i = 0; i < 5; i++)
        {
            var c = address[i];
            if (c is (< 'A' or > 'Z') and (< 'a' or > 'z'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHexByte(string value)
    {
        return value.Length == 2 &&
               Uri.IsHexDigit(value[0]) &&
               Uri.IsHexDigit(value[1]);
    }

    private static IReadOnlyList<string> SplitFields(ReadOnlySpan<char> fieldsPart)
    {
        if (fieldsPart.IsEmpty)
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        while (true)
        {
            var comma = fieldsPart.IndexOf(',');
            if (comma < 0)
            {
                list.Add(fieldsPart.ToString());
                break;
            }

            list.Add(fieldsPart[..comma].ToString());
            fieldsPart = fieldsPart[(comma + 1)..];
        }

        return list;
    }
}
