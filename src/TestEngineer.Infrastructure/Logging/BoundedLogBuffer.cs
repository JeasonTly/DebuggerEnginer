namespace TestEngineer.Infrastructure.Logging;

/// <summary>固定容量的行缓冲（环形）：满后丢弃最旧行。用于控制台/日志面板，避免长时间运行内存无限增长。线程安全。</summary>
public sealed class BoundedLogBuffer
{
    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();
    private long _totalAppended;

    public BoundedLogBuffer(int capacity = 5000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }

    /// <summary>每追加一行触发（可能在任意线程）。</summary>
    public event Action<string>? LineAppended;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _lines.Count;
            }
        }
    }

    /// <summary>累计追加行数（含已被丢弃的），可用于判断是否有新内容。</summary>
    public long TotalAppended
    {
        get
        {
            lock (_gate)
            {
                return _totalAppended;
            }
        }
    }

    public void Append(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_gate)
        {
            _lines.Enqueue(line);
            _totalAppended++;
            while (_lines.Count > Capacity)
            {
                _lines.Dequeue();
            }
        }

        LineAppended?.Invoke(line);
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return [.. _lines];
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
        }
    }
}
