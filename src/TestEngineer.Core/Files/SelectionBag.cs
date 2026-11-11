using TestEngineer.Core.Models;

namespace TestEngineer.Core.Files;

/// <summary>
/// 跨目录多选集合，以设备绝对路径为键（区分大小写）。切换目录不会丢失已选项，
/// 适合"在 A 目录选一些、去 B 目录再选一些，然后统一拉取/删除"的场景。线程安全。
/// </summary>
public sealed class SelectionBag
{
    private readonly object _gate = new();
    private readonly Dictionary<string, FileEntry> _items = new(StringComparer.Ordinal);

    /// <summary>集合内容发生变化时触发（可能在任意线程）。</summary>
    public event EventHandler? Changed;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public bool IsEmpty => Count == 0;

    /// <summary>已选路径快照，按路径排序。</summary>
    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (_gate)
            {
                return [.. _items.Keys.Order(StringComparer.Ordinal)];
            }
        }
    }

    public IReadOnlyList<FileEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _items.Values.OrderBy(e => e.AbsolutePath, StringComparer.Ordinal)];
            }
        }
    }

    public bool Contains(string absolutePath)
    {
        var key = RemotePath.Normalize(absolutePath);
        lock (_gate)
        {
            return _items.ContainsKey(key);
        }
    }

    /// <summary>加入条目；已存在则以新条目覆盖元数据。返回是否为新增。</summary>
    public bool Add(FileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var key = RemotePath.Normalize(entry.AbsolutePath);
        bool added;
        lock (_gate)
        {
            added = !_items.ContainsKey(key);
            _items[key] = entry with { AbsolutePath = key };
        }

        OnChanged();
        return added;
    }

    public void AddRange(IEnumerable<FileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_gate)
        {
            foreach (var entry in entries)
            {
                var key = RemotePath.Normalize(entry.AbsolutePath);
                _items[key] = entry with { AbsolutePath = key };
            }
        }

        OnChanged();
    }

    public bool Remove(string absolutePath)
    {
        var key = RemotePath.Normalize(absolutePath);
        bool removed;
        lock (_gate)
        {
            removed = _items.Remove(key);
        }

        if (removed)
        {
            OnChanged();
        }

        return removed;
    }

    /// <summary>切换选中状态，返回切换后是否被选中。</summary>
    public bool Toggle(FileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var key = RemotePath.Normalize(entry.AbsolutePath);
        bool selected;
        lock (_gate)
        {
            if (_items.Remove(key))
            {
                selected = false;
            }
            else
            {
                _items[key] = entry with { AbsolutePath = key };
                selected = true;
            }
        }

        OnChanged();
        return selected;
    }

    public void Clear()
    {
        bool had;
        lock (_gate)
        {
            had = _items.Count > 0;
            _items.Clear();
        }

        if (had)
        {
            OnChanged();
        }
    }

    /// <summary>
    /// 以指定目录当前列表同步选择：仅该目录的直接子项被替换为 <paramref name="selectedInDirectory"/>，
    /// 其他目录的已选项保持不变。用于列表控件的 SelectionChanged。
    /// </summary>
    public void ReplaceDirectorySelection(string directory, IEnumerable<FileEntry> selectedInDirectory)
    {
        ArgumentNullException.ThrowIfNull(selectedInDirectory);
        var dir = RemotePath.Normalize(directory);
        lock (_gate)
        {
            foreach (var key in _items.Keys.Where(k => RemotePath.GetParent(k) == dir && k != RemotePath.Root).ToList())
            {
                _items.Remove(key);
            }

            foreach (var entry in selectedInDirectory)
            {
                var key = RemotePath.Normalize(entry.AbsolutePath);
                _items[key] = entry with { AbsolutePath = key };
            }
        }

        OnChanged();
    }

    /// <summary>按父目录分组，便于界面展示"来自 N 个目录"。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<FileEntry>> GroupByDirectory()
    {
        lock (_gate)
        {
            return _items.Values
                .GroupBy(e => RemotePath.GetParent(e.AbsolutePath), StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<FileEntry>)[.. g.OrderBy(e => e.AbsolutePath, StringComparer.Ordinal)],
                    StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// 有效根路径：若某已选目录的祖先目录也被选中，则被祖先覆盖的路径不再单独列出，
    /// 避免拉取/删除时重复处理。
    /// </summary>
    public IReadOnlyList<string> EffectiveRoots()
    {
        lock (_gate)
        {
            var directories = _items.Values.Where(e => e.IsDirectory).Select(e => e.AbsolutePath).ToList();
            return
            [
                .. _items.Keys
                    .Where(path => !directories.Any(dir => RemotePath.IsUnder(path, dir)))
                    .Order(StringComparer.Ordinal),
            ];
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
