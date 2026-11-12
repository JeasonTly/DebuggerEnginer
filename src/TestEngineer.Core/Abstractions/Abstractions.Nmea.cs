using TestEngineer.Core.Gnss;

namespace TestEngineer.Core.Abstractions;

public interface INmeaLogAnalyzer
{
    Task<NmeaScanResult> ScanAsync(
        IEnumerable<string> roots,
        NmeaScanOptions options,
        IProgress<NmeaScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class NmeaScanOptions
{
    public static IReadOnlyList<string> DefaultExtensions { get; } = [".txt"];

    public bool IncludeSubdirectories { get; init; } = true;

    /// <summary>额外扩展名（如 .log / .nmea），默认空；与 <see cref="DefaultExtensions"/> 合并使用。</summary>
    public IReadOnlyList<string> ExtraExtensions { get; init; } = Array.Empty<string>();

    public IEnumerable<string> GetEffectiveExtensions()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in DefaultExtensions)
        {
            set.Add(NormalizeExt(ext));
        }

        foreach (var ext in ExtraExtensions)
        {
            set.Add(NormalizeExt(ext));
        }

        return set;
    }

    private static string NormalizeExt(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext))
        {
            return ext;
        }

        return ext.StartsWith('.') ? ext : "." + ext;
    }
}

public sealed class NmeaScanProgress
{
    public int FilesDiscovered { get; init; }

    public int FilesProcessed { get; init; }

    public string? CurrentPath { get; init; }

    public NmeaFileStats? CurrentFile { get; init; }
}

public sealed class NmeaScanResult
{
    public required IReadOnlyList<NmeaFileStats> Files { get; init; }

    public required NmeaStatsAggregate Aggregate { get; init; }
}

public interface IKmlTrackWriter
{
    Task<IReadOnlyList<string>> WriteAsync(
        IEnumerable<KmlTrackRequest> tracks,
        KmlWriteOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class KmlTrackRequest
{
    public required string SourcePath { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>KML 颜色，AABBGGRR（小写十六进制，如 ff3539e5）。</summary>
    public required string KmlColorAabbggrr { get; init; }
}

public sealed class KmlWriteOptions
{
    /// <summary>输出目录；null 表示写到各源文件旁。</summary>
    public string? OutputDirectory { get; init; }

    public bool Kmz { get; init; }

    public bool AbsoluteAltitude { get; init; }

    public bool IncludePointPlacemarks { get; init; } = true;
}
