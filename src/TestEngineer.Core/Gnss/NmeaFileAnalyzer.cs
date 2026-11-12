using System.Text;

namespace TestEngineer.Core.Gnss;

/// <summary>NMEA 文件级分析（纯内存/流，不依赖 WPF）。</summary>
public static class NmeaFileAnalyzer
{
    public const int BinaryProbeBytes = 4096;

    private static readonly Encoding Latin1 = Encoding.Latin1;

    public static NmeaFileStats AnalyzeFile(string path, string? relativePath = null)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Analyze(stream, path, relativePath ?? Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new NmeaFileStats
            {
                Path = path,
                RelativePath = relativePath ?? Path.GetFileName(path),
                Verdict = NmeaFileVerdict.ReadFailed,
                ErrorMessage = ex.Message,
            };
        }
    }

    public static NmeaFileStats Analyze(Stream stream, string path = "", string relativePath = "")
    {
        ArgumentNullException.ThrowIfNull(stream);

        Stream readable = stream;
        IDisposable? owned = null;
        try
        {
            if (stream.CanSeek)
            {
                if (ContainsNulInPrefixSeekable(stream, BinaryProbeBytes))
                {
                    return NotNmea(path, relativePath);
                }
            }
            else
            {
                var prefix = ReadExact(stream, BinaryProbeBytes);
                if (ContainsNul(prefix))
                {
                    return NotNmea(path, relativePath);
                }

                readable = new ConcatStream(prefix, stream);
                owned = readable;
            }

            using var reader = new StreamReader(
                readable,
                Latin1,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);
            return Analyze(reader, path, relativePath);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    public static NmeaFileStats Analyze(TextReader reader, string path = "", string relativePath = "")
    {
        ArgumentNullException.ThrowIfNull(reader);

        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return AnalyzeLines(lines, path, relativePath);
    }

    public static NmeaFileStats AnalyzeLines(
        IReadOnlyList<string> lines,
        string path = "",
        string relativePath = "")
    {
        var counts = new NmeaQualityCounts();
        var anomalies = new List<NmeaAnomalySample>();
        var standardCount = 0;
        TimeOnly? firstUtc = null;
        TimeOnly? lastUtc = null;

        for (var i = 0; i < lines.Count; i++)
        {
            var lineNumber = i + 1;
            var isLast = i == lines.Count - 1;
            var classification = NmeaLineClassifier.Classify(lines[i], isLast);

            switch (classification.Class)
            {
                case NmeaLineClass.Ignored:
                    break;

                case NmeaLineClass.Private:
                    counts.PrivateLines++;
                    break;

                case NmeaLineClass.Standard:
                    standardCount++;
                    if (classification.Gga is { } gga)
                    {
                        counts.AddFix(gga.Quality);
                        if (gga.UtcTime is { } utc)
                        {
                            firstUtc ??= utc;
                            lastUtc = utc;
                        }
                    }

                    break;

                case NmeaLineClass.Anomaly:
                    standardCount++; // 标准头仍视为“出现过标准句”，便于 NoGga vs NotNmea
                    counts.AnomalyLines++;
                    if (classification.IsDroppedGga)
                    {
                        counts.DroppedGga++;
                    }

                    if (anomalies.Count < NmeaFileStats.MaxAnomalySamples)
                    {
                        anomalies.Add(new NmeaAnomalySample(
                            lineNumber,
                            classification.AnomalyReason ?? "异常行",
                            Truncate(lines[i], 120)));
                    }

                    break;
            }
        }

        var verdict = ResolveVerdict(standardCount, counts);
        return new NmeaFileStats
        {
            Path = path,
            RelativePath = relativePath,
            Verdict = verdict,
            Counts = counts,
            AnomalySamples = anomalies,
            FirstUtcTime = firstUtc,
            LastUtcTime = lastUtc,
            StandardSentenceCount = standardCount,
        };
    }

    private static NmeaFileVerdict ResolveVerdict(int standardCount, NmeaQualityCounts counts)
    {
        if (standardCount == 0)
        {
            return NmeaFileVerdict.NotNmea;
        }

        if (counts.TotalGga == 0)
        {
            return NmeaFileVerdict.NoGga;
        }

        return counts.AnomalyLines > 0
            ? NmeaFileVerdict.ValidWithAnomalies
            : NmeaFileVerdict.Valid;
    }

    private static NmeaFileStats NotNmea(string path, string relativePath) => new()
    {
        Path = path,
        RelativePath = relativePath,
        Verdict = NmeaFileVerdict.NotNmea,
    };

    private static bool ContainsNulInPrefixSeekable(Stream stream, int maxBytes)
    {
        var origin = stream.Position;
        var buffer = new byte[maxBytes];
        var read = stream.Read(buffer, 0, buffer.Length);
        stream.Position = origin;
        return ContainsNul(buffer.AsSpan(0, read));
    }

    private static byte[] ReadExact(Stream stream, int maxBytes)
    {
        var buffer = new byte[maxBytes];
        var n = 0;
        while (n < buffer.Length)
        {
            var r = stream.Read(buffer, n, buffer.Length - n);
            if (r == 0)
            {
                break;
            }

            n += r;
        }

        if (n == buffer.Length)
        {
            return buffer;
        }

        return buffer.AsSpan(0, n).ToArray();
    }

    private static bool ContainsNul(ReadOnlySpan<byte> data) => data.IndexOf((byte)0) >= 0;

    private static string Truncate(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars] + "…";

    private sealed class ConcatStream(byte[] prefix, Stream rest) : Stream
    {
        private int _prefixOffset;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var total = 0;
            if (_prefixOffset < prefix.Length)
            {
                var fromPrefix = Math.Min(count, prefix.Length - _prefixOffset);
                Buffer.BlockCopy(prefix, _prefixOffset, buffer, offset, fromPrefix);
                _prefixOffset += fromPrefix;
                total += fromPrefix;
                offset += fromPrefix;
                count -= fromPrefix;
            }

            if (count > 0)
            {
                total += rest.Read(buffer, offset, count);
            }

            return total;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
