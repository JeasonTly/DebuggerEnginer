using CommunityToolkit.Mvvm.ComponentModel;
using TestEngineer.Core.Models;

namespace TestEngineer.Desktop.ViewModels;

/// <summary>文件列表行：包装 <see cref="FileEntry"/>，提供 IsSelected 与展示字段。</summary>
public sealed partial class FileRowItem : ObservableObject
{
    public FileRowItem(FileEntry entry, bool isSelected)
    {
        Entry = entry;
        _isSelected = isSelected;
    }

    public FileEntry Entry { get; }

    public string Name => Entry.Name;

    public string AbsolutePath => Entry.AbsolutePath;

    public bool IsDirectory => Entry.IsDirectory;

    public bool CanRead => Entry.CanRead;

    public bool CanWrite => Entry.CanWrite;

    /// <summary>不可读行不可勾选 / 进入 / 右键。</summary>
    public bool CanInteract => Entry.CanRead;

    public string TypeMark => Entry.IsDirectory ? "DIR" : "FILE";

    /// <summary>权限标签文案：可读写 / 可读 / 不可读。</summary>
    public string AccessLabel =>
        !Entry.CanRead ? "不可读" : Entry.CanWrite ? "可读写" : "可读";

    /// <summary>rw / r / none，供样式触发。</summary>
    public string AccessKind =>
        !Entry.CanRead ? "none" : Entry.CanWrite ? "rw" : "r";

    /// <summary>模式列：去掉 ls 类型前缀（d/-/l）。</summary>
    public string ModeText
    {
        get
        {
            var mode = Entry.Mode ?? "";
            if (mode.Length > 1 && (mode[0] is 'd' or '-' or 'l' or 'c' or 'b' or 'p' or 's'))
            {
                return mode[1..];
            }

            return mode;
        }
    }

    public string SizeText
    {
        get
        {
            if (Entry.IsDirectory) return "—";
            if (Entry.Size < 0) return "—";
            return FormatSize(Entry.Size);
        }
    }

    public string ModifiedText =>
        Entry.MTime is { } t ? t.ToString("MM-dd HH:mm") : "—";

    [ObservableProperty] private bool _isSelected;

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "K", "M", "G", "T"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }

        return i == 0 ? $"{bytes}B" : $"{v:0.#}{units[i]}";
    }
}
