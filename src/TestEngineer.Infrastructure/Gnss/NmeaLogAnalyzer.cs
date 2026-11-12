using System.Collections.Concurrent;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Gnss;

namespace TestEngineer.Infrastructure.Gnss;

public sealed class NmeaLogAnalyzer : INmeaLogAnalyzer
{
    public async Task<NmeaScanResult> ScanAsync(
        IEnumerable<string> roots,
        NmeaScanOptions options,
        IProgress<NmeaScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(options);

        var discovered = DiscoverFiles(roots, options);
        progress?.Report(new NmeaScanProgress
        {
            FilesDiscovered = discovered.Count,
            FilesProcessed = 0,
        });

        if (discovered.Count == 0)
        {
            return new NmeaScanResult
            {
                Files = Array.Empty<NmeaFileStats>(),
                Aggregate = NmeaStatsAggregate.FromFiles(Array.Empty<NmeaFileStats>()),
            };
        }

        var degree = Math.Min(4, Math.Max(1, Environment.ProcessorCount));
        using var gate = new SemaphoreSlim(degree, degree);
        var processed = 0;
        var bag = new ConcurrentBag<(int Index, NmeaFileStats Stats)>();

        var tasks = discovered.Select(async (item, index) =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stats = await Task.Run(
                        () => NmeaFileAnalyzer.AnalyzeFile(item.FullPath, item.RelativePath),
                        cancellationToken)
                    .ConfigureAwait(false);

                var done = Interlocked.Increment(ref processed);
                progress?.Report(new NmeaScanProgress
                {
                    FilesDiscovered = discovered.Count,
                    FilesProcessed = done,
                    CurrentPath = item.FullPath,
                    CurrentFile = stats,
                });

                bag.Add((index, stats));
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var files = bag
            .OrderBy(x => x.Index)
            .Select(x => x.Stats)
            .ToList();

        return new NmeaScanResult
        {
            Files = files,
            Aggregate = NmeaStatsAggregate.FromFiles(files),
        };
    }

    private static List<(string FullPath, string RelativePath)> DiscoverFiles(
        IEnumerable<string> roots,
        NmeaScanOptions options)
    {
        var extensions = new HashSet<string>(options.GetEffectiveExtensions(), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string FullPath, string RelativePath)>();

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (File.Exists(fullRoot))
            {
                if (!extensions.Contains(Path.GetExtension(fullRoot)))
                {
                    continue;
                }

                if (!seen.Add(fullRoot))
                {
                    continue;
                }

                result.Add((fullRoot, Path.GetFileName(fullRoot)));
                continue;
            }

            if (!Directory.Exists(fullRoot))
            {
                continue;
            }

            foreach (var file in EnumerateFilesSafe(fullRoot, options.IncludeSubdirectories, extensions))
            {
                if (!seen.Add(file))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(fullRoot, file);
                result.Add((file, relative));
            }
        }

        return result;
    }

    private static IEnumerable<string> EnumerateFilesSafe(
        string rootDirectory,
        bool includeSubdirectories,
        HashSet<string> extensions)
    {
        var stack = new Stack<string>();
        stack.Push(rootDirectory);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                FileAttributes attrs;
                try
                {
                    attrs = File.GetAttributes(entry);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    continue;
                }

                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attrs & FileAttributes.Directory) != 0)
                {
                    if (includeSubdirectories)
                    {
                        stack.Push(entry);
                    }

                    continue;
                }

                if (extensions.Contains(Path.GetExtension(entry)))
                {
                    yield return entry;
                }
            }
        }
    }
}
