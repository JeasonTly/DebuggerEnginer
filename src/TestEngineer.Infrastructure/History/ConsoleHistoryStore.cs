using System.Text;
using TestEngineer.Core.Abstractions;

namespace TestEngineer.Infrastructure.History;

/// <summary>
/// 控制台历史：每行一条命令的 UTF-8 文本文件。相邻重复项合并，超过上限时丢弃最旧的。
/// </summary>
public sealed class ConsoleHistoryStore : IConsoleHistory
{
    private readonly string _filePath;
    private readonly Func<int> _limitProvider;
    private readonly object _gate = new();
    private readonly List<string> _items;

    public ConsoleHistoryStore(string filePath, Func<int>? limitProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _limitProvider = limitProvider ?? (() => 500);
        _items = Load();
        TrimToLimit();
    }

    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    public void Add(string commandLine)
    {
        var line = Sanitize(commandLine);
        if (line.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_items.Count > 0 && _items[^1] == line)
            {
                return;
            }

            _items.Add(line);
            if (!TrimToLimit())
            {
                AppendToFile(line);
            }
            else
            {
                Rewrite();
            }
        }
    }

    public bool Remove(string commandLine)
    {
        var line = Sanitize(commandLine);
        if (line.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i] != line)
                {
                    continue;
                }

                _items.RemoveAt(i);
                Rewrite();
                return true;
            }
        }

        return false;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            Rewrite();
        }
    }

    private static string Sanitize(string? commandLine) =>
        (commandLine ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();

    private List<string> Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? [.. File.ReadAllLines(_filePath, Encoding.UTF8).Select(Sanitize).Where(l => l.Length > 0)]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private bool TrimToLimit()
    {
        var limit = Math.Max(1, _limitProvider());
        if (_items.Count <= limit)
        {
            return false;
        }

        _items.RemoveRange(0, _items.Count - limit);
        return true;
    }

    private void AppendToFile(string line)
    {
        try
        {
            EnsureDirectory();
            File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 历史写入失败不影响使用，内存中仍保留。
        }
    }

    private void Rewrite()
    {
        try
        {
            EnsureDirectory();
            var temp = _filePath + ".tmp";
            File.WriteAllLines(temp, _items, Encoding.UTF8);
            File.Move(temp, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
