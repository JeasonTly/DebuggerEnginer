using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TestEngineer.Adb;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Files;
using TestEngineer.Core.Models;

namespace TestEngineer.Desktop.ViewModels;

public sealed partial class FilesPageViewModel : ObservableObject
{
    private readonly IDeviceContext _devices;
    private readonly IFileManagerService _files;
    private readonly IDebugCommandService _debug;
    private readonly IDeviceInfoService _info;
    private readonly SelectionBag _selection = new();
    private ClipboardOp? _clipboard;

    public FilesPageViewModel(
        IDeviceContext devices,
        IFileManagerService files,
        IDebugCommandService debug,
        IDeviceInfoService info)
    {
        _devices = devices;
        _files = files;
        _debug = debug;
        _info = info;
        CurrentPath = "/sdcard";
        _devices.CurrentChanged += (_, _) => Application.Current.Dispatcher.Invoke(() =>
        {
            _selection.Clear();
            SelectionSummary = "已选 0";
            _ = RefreshAsync();
        });
    }

    public ObservableCollection<FileEntry> Entries { get; } = [];
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    [ObservableProperty] private string _currentPath = "/sdcard";
    [ObservableProperty] private string _rootMode = "sdcard";
    [ObservableProperty] private string _status = "等待加载";
    [ObservableProperty] private string _selectionSummary = "已选 0";
    [ObservableProperty] private string _clipboardSummary = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private FileEntry? _focusedEntry;

    private sealed record ClipboardOp(string Op, IReadOnlyList<string> Paths);

    private IReadOnlyList<string> TargetsFor(FileEntry? entry)
    {
        if (entry is not null && _selection.Contains(entry.AbsolutePath))
        {
            return _selection.EffectiveRoots();
        }

        if (_selection.Count > 0 && entry is null)
        {
            return _selection.EffectiveRoots();
        }

        return entry is null ? [] : [entry.AbsolutePath];
    }

    [RelayCommand]
    private void UseSdcard()
    {
        RootMode = "sdcard";
        CurrentPath = "/sdcard";
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void UseRoot()
    {
        RootMode = "root";
        CurrentPath = "/";
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _devices.Current?.IsOnline != true)
        {
            Entries.Clear();
            Status = "设备未连接";
            return;
        }

        IsBusy = true;
        Status = "列出目录…";
        try
        {
            var list = await _files.ListAsync(serial, CurrentPath, probeAccess: true);
            Entries.Clear();
            foreach (var e in list)
            {
                Entries.Add(e);
            }

            RebuildBreadcrumbs();
            SelectionSummary = $"已选 {_selection.Count}（跨目录）";
            Status = $"{Entries.Count} 项 · {CurrentPath}";
        }
        catch (Exception ex)
        {
            Status = "列出失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Enter(FileEntry? entry)
    {
        if (entry is null || !entry.IsDirectory) return;
        CurrentPath = entry.AbsolutePath;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void GoBreadcrumb(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        CurrentPath = path;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void ToggleSelect(FileEntry? entry)
    {
        if (entry is null) return;
        _selection.Toggle(entry);
        SelectionSummary = $"已选 {_selection.Count}（跨目录）";
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _selection.Clear();
        SelectionSummary = "已选 0";
    }

    [RelayCommand]
    private void Copy(FileEntry? entry)
    {
        var paths = TargetsFor(entry ?? FocusedEntry);
        if (paths.Count == 0) return;
        _clipboard = new ClipboardOp("copy", paths);
        ClipboardSummary = $"剪贴板：复制 {paths.Count} 项";
    }

    [RelayCommand]
    private void Cut(FileEntry? entry)
    {
        var paths = TargetsFor(entry ?? FocusedEntry);
        if (paths.Count == 0) return;
        _clipboard = new ClipboardOp("cut", paths);
        ClipboardSummary = $"剪贴板：剪切 {paths.Count} 项";
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _clipboard is null || _clipboard.Paths.Count == 0) return;

        IsBusy = true;
        var ok = 0;
        var fail = 0;
        foreach (var src in _clipboard.Paths)
        {
            var name = RemotePath.GetName(src);
            if (name is "/" or "") name = "pasted";
            var dest = RemotePath.Combine(CurrentPath, name);
            var result = _clipboard.Op == "cut"
                ? await _files.MoveAsync(serial, src, dest)
                : await _files.CopyAsync(serial, src, dest);
            if (result.Succeeded) ok++; else fail++;
        }

        if (_clipboard.Op == "cut")
        {
            _clipboard = null;
            ClipboardSummary = "";
        }

        Status = $"粘贴完成：成功 {ok}，失败 {fail}";
        IsBusy = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(FileEntry? entry)
    {
        var serial = _devices.Current?.Serial;
        var paths = TargetsFor(entry ?? FocusedEntry);
        if (string.IsNullOrWhiteSpace(serial) || paths.Count == 0) return;

        if (MessageBox.Show($"确认删除 {paths.Count} 项？此操作不可恢复。", "删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        var ok = 0;
        var fail = 0;
        foreach (var path in paths)
        {
            if (path is "/" or "/sdcard" or "/storage/emulated/0")
            {
                fail++;
                continue;
            }

            var r = await _files.DeleteAsync(serial, path, recursive: true);
            if (r.Succeeded)
            {
                ok++;
                _selection.Remove(path);
            }
            else
            {
                fail++;
            }
        }

        SelectionSummary = $"已选 {_selection.Count}（跨目录）";
        Status = $"删除完成：成功 {ok}，失败 {fail}";
        IsBusy = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ExportSelectionAsync()
    {
        var serial = _devices.Current?.Serial;
        var paths = _selection.Count > 0 ? _selection.EffectiveRoots() : TargetsFor(FocusedEntry);
        if (string.IsNullOrWhiteSpace(serial) || paths.Count == 0) return;

        var dlg = new OpenFolderDialog { Title = "导出到本地目录" };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FolderName)) return;

        IsBusy = true;
        var ok = 0;
        var fail = 0;
        foreach (var path in paths)
        {
            var name = RemotePath.GetName(path).Trim('/');
            if (string.IsNullOrEmpty(name) || name == "/") name = "root-export";
            var local = System.IO.Path.Combine(dlg.FolderName, name);
            var r = await _files.PullAsync(serial, path, local);
            if (r.Succeeded) ok++; else fail++;
        }

        Status = $"导出完成：成功 {ok}，失败 {fail}";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ImportLocalAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        var dlg = new OpenFileDialog { Multiselect = true, Title = "导入到当前目录" };
        if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;
        await PushLocalFilesAsync(dlg.FileNames);
    }

    public async Task PushLocalFilesAsync(IEnumerable<string> localPaths)
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        IsBusy = true;
        var ok = 0;
        var fail = 0;
        foreach (var local in localPaths)
        {
            if (!System.IO.File.Exists(local) && !System.IO.Directory.Exists(local))
            {
                fail++;
                continue;
            }

            var r = await _files.PushAsync(serial, local, CurrentPath);
            if (r.Succeeded) ok++; else fail++;
        }

        Status = $"上传完成：成功 {ok}，失败 {fail}";
        IsBusy = false;
        await RefreshAsync();
    }

    /// <summary>将当前焦点/勾选项拉取到临时目录，供拖出到资源管理器。</summary>
    public async Task<IReadOnlyList<string>> PrepareDragOutLocalPathsAsync()
    {
        var serial = _devices.Current?.Serial;
        var paths = _selection.Count > 0 ? _selection.EffectiveRoots() : TargetsFor(FocusedEntry);
        if (string.IsNullOrWhiteSpace(serial) || paths.Count == 0)
        {
            return [];
        }

        var tempRoot = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "TestEngineer",
            "drag-out",
            DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        System.IO.Directory.CreateDirectory(tempRoot);

        IsBusy = true;
        Status = "准备拖出…";
        var locals = new List<string>();
        try
        {
            foreach (var path in paths)
            {
                var name = RemotePath.GetName(path).Trim('/');
                if (string.IsNullOrEmpty(name) || name == "/") name = "export";
                var local = System.IO.Path.Combine(tempRoot, name);
                var r = await _files.PullAsync(serial, path, local);
                if (r.Succeeded && (System.IO.File.Exists(local) || System.IO.Directory.Exists(local)))
                {
                    locals.Add(local);
                }
            }

            Status = locals.Count > 0 ? $"已准备拖出 {locals.Count} 项" : "拖出准备失败";
            return locals;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 缺陷证据包：screencap + logcat + 设备摘要，以及已选远程文件（若有）。
    /// 无选中时仍至少含一次 screencap。
    /// </summary>
    [RelayCommand]
    private async Task ExportEvidencePackAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _devices.Current?.IsOnline != true)
        {
            Status = "设备未连接";
            return;
        }

        var defaultDir = DefaultExportDirectory();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dlg = new SaveFileDialog
        {
            Title = "保存缺陷证据包",
            Filter = "Zip|*.zip",
            InitialDirectory = defaultDir,
            FileName = $"evidence-{serial}-{stamp}.zip",
        };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FileName)) return;

        var selected = _selection.Count > 0
            ? _selection.EffectiveRoots()
            : TargetsFor(FocusedEntry);

        IsBusy = true;
        Status = "组装缺陷证据包…";
        var staging = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "TestEngineer",
            "evidence-pack-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(staging);
        var notes = new StringBuilder();
        notes.AppendLine("TestEngineer 缺陷证据包");
        notes.AppendLine($"设备序列号: {serial}");
        notes.AppendLine($"生成时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:sszzz}");
        notes.AppendLine();

        try
        {
            // 1) screencap（无选中时也必含）
            var shotPath = System.IO.Path.Combine(staging, "screencap.png");
            Status = "截屏…";
            var shot = await _debug.CaptureScreenshotAsync(serial, shotPath);
            notes.AppendLine(shot.Succeeded && System.IO.File.Exists(shotPath)
                ? "OK   screencap.png"
                : $"FAIL screencap.png — {shot.FailureMessage}");

            // 2) logcat（先 zip 再解出 txt，避免重复实现）
            Status = "导出 logcat…";
            var logcatZip = System.IO.Path.Combine(staging, "_logcat-temp.zip");
            var logcat = await _debug.ExportLogcatZipAsync(serial, logcatZip);
            if (logcat.Succeeded && System.IO.File.Exists(logcatZip))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(logcatZip);
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        var dest = System.IO.Path.Combine(staging, entry.Name == "logcat.txt" ? "logcat.txt" : "logcat-" + entry.Name);
                        entry.ExtractToFile(dest, overwrite: true);
                    }

                    notes.AppendLine("OK   logcat.txt");
                }
                catch (Exception ex)
                {
                    notes.AppendLine("FAIL logcat 解压 — " + ex.Message);
                }
                finally
                {
                    TryDeleteFile(logcatZip);
                }
            }
            else
            {
                notes.AppendLine("FAIL logcat — " + logcat.FailureMessage);
            }

            // 3) 设备摘要
            Status = "采集设备摘要…";
            try
            {
                var snapshot = await _info.GetSnapshotAsync(serial);
                var summaryPath = System.IO.Path.Combine(staging, "device-summary.txt");
                await System.IO.File.WriteAllTextAsync(summaryPath, FormatDeviceSnapshot(snapshot), new UTF8Encoding(false));
                notes.AppendLine("OK   device-summary.txt");
            }
            catch (Exception ex)
            {
                notes.AppendLine("FAIL device-summary.txt — " + ex.Message);
            }

            // 4) 已选远程文件
            if (selected.Count > 0)
            {
                var remoteRoot = System.IO.Path.Combine(staging, "remote-files");
                System.IO.Directory.CreateDirectory(remoteRoot);
                notes.AppendLine();
                notes.AppendLine("远程文件:");
                for (var i = 0; i < selected.Count; i++)
                {
                    var remote = selected[i];
                    Status = $"拉取远程文件 {i + 1}/{selected.Count}…";
                    var name = RemotePath.GetName(remote).Trim('/');
                    if (string.IsNullOrEmpty(name) || name == "/") name = $"item-{i + 1:D3}";
                    var local = System.IO.Path.Combine(remoteRoot, $"{i + 1:D3}_{SanitizeFileName(name)}");
                    var pulled = await _files.PullAsync(serial, remote, local);
                    notes.AppendLine($"{(pulled.Succeeded ? "OK  " : "FAIL")} {remote}");
                    if (!pulled.Succeeded)
                    {
                        notes.AppendLine("     " + pulled.FailureMessage.ReplaceLineEndings(" "));
                    }
                }
            }
            else
            {
                notes.AppendLine();
                notes.AppendLine("远程文件: （无选中，仅含 screencap/logcat/摘要）");
            }

            await System.IO.File.WriteAllTextAsync(
                System.IO.Path.Combine(staging, "manifest.txt"),
                notes.ToString(),
                new UTF8Encoding(false));

            Status = "打包 ZIP…";
            ZipPackHelper.CreateFromDirectory(staging, dlg.FileName);
            Status = "证据包已保存：" + dlg.FileName;
        }
        catch (Exception ex)
        {
            Status = "证据包失败：" + ex.Message;
        }
        finally
        {
            TryDeleteDirectory(staging);
            IsBusy = false;
        }
    }

    /// <summary>将 DeviceSnapshot 写成 UTF-8 txt，可选再打 zip。</summary>
    [RelayCommand]
    private async Task ExportSystemInfoAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _devices.Current?.IsOnline != true)
        {
            Status = "设备未连接";
            return;
        }

        var defaultDir = DefaultExportDirectory();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dlg = new SaveFileDialog
        {
            Title = "导出系统信息",
            Filter = "文本|*.txt|Zip|*.zip",
            InitialDirectory = defaultDir,
            FileName = $"sysinfo-{serial}-{stamp}.txt",
        };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FileName)) return;

        IsBusy = true;
        Status = "采集系统信息…";
        try
        {
            var snapshot = await _info.GetSnapshotAsync(serial);
            var text = FormatDeviceSnapshot(snapshot);
            var path = dlg.FileName;
            var asZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            if (asZip)
            {
                ZipPackHelper.CreateFromTextEntries(path, [new KeyValuePair<string, string>("device-summary.txt", text)]);
            }
            else
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                await System.IO.File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
            }

            Status = "系统信息已保存：" + path;
        }
        catch (Exception ex)
        {
            Status = "系统信息导出失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        foreach (var p in RemotePath.Breadcrumbs(CurrentPath))
        {
            Breadcrumbs.Add(p);
        }
    }

    private static string DefaultExportDirectory()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TestEngineer",
            "Exports");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    internal static string FormatDeviceSnapshot(DeviceSnapshot s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TestEngineer 系统信息 / 设备摘要");
        sb.AppendLine($"采集时间: {s.CapturedAt:yyyy-MM-dd HH:mm:sszzz}");
        sb.AppendLine($"序列号: {s.Serial}");
        sb.AppendLine();
        sb.AppendLine("[基本]");
        sb.AppendLine($"制造商: {s.Manufacturer ?? "-"}");
        sb.AppendLine($"品牌: {s.Brand ?? "-"}");
        sb.AppendLine($"型号: {s.Model ?? "-"}");
        sb.AppendLine($"Android: {s.AndroidVersion ?? "-"} (SDK {s.SdkLevel})");
        sb.AppendLine($"BuildType: {s.BuildType ?? "-"}");
        sb.AppendLine($"BuildDisplayId: {s.BuildDisplayId ?? "-"}");
        sb.AppendLine($"Debuggable: {FormatBool(s.Debuggable)}");
        sb.AppendLine($"SecurityPatch: {s.SecurityPatch ?? "-"}");
        sb.AppendLine($"Kernel: {s.Kernel ?? "-"}");
        sb.AppendLine($"Baseband: {s.Baseband ?? "-"}");
        sb.AppendLine();
        sb.AppendLine("[电池]");
        sb.AppendLine($"电量: {FormatNullable(s.BatteryLevel, "%")}");
        sb.AppendLine($"状态: {s.BatteryStatus ?? "-"}");
        sb.AppendLine($"充电中: {FormatBool(s.IsCharging)}");
        sb.AppendLine($"温度: {(s.BatteryTemperatureC is { } t ? $"{t:0.0}°C" : "-")}");
        sb.AppendLine($"电压: {FormatNullable(s.BatteryVoltageMv, " mV")}");
        sb.AppendLine($"电源: {s.PowerSource ?? "-"}");
        sb.AppendLine();
        sb.AppendLine("[存储 /data]");
        sb.AppendLine($"总量: {FormatBytes(s.StorageTotalBytes)}");
        sb.AppendLine($"已用: {FormatBytes(s.StorageUsedBytes)}");
        sb.AppendLine($"可用: {FormatBytes(s.StorageFreeBytes)}");
        sb.AppendLine();
        sb.AppendLine("[内存]");
        sb.AppendLine($"总量: {FormatBytes(s.MemoryTotalBytes)}");
        sb.AppendLine($"可用: {FormatBytes(s.MemoryAvailableBytes)}");
        sb.AppendLine();
        sb.AppendLine("[CPU]");
        sb.AppendLine($"型号: {s.CpuModel ?? "-"}");
        sb.AppendLine($"核心: {s.CpuCores}");
        sb.AppendLine($"ABI: {s.CpuAbi ?? "-"}");
        sb.AppendLine();
        sb.AppendLine("[屏幕]");
        sb.AppendLine($"分辨率: {s.Resolution}");
        sb.AppendLine($"密度: {s.Density}");
        sb.AppendLine();
        sb.AppendLine("[网络]");
        sb.AppendLine($"类型: {s.NetworkType ?? "-"}");
        sb.AppendLine($"Wi-Fi SSID: {s.WifiSsid ?? "-"}");
        sb.AppendLine($"Wi-Fi MAC: {s.WifiMac ?? "-"}");
        sb.AppendLine($"IP: {s.IpAddress ?? "-"}");
        sb.AppendLine();
        sb.AppendLine("[运行]");
        sb.AppendLine($"UptimeSec: {s.UptimeSec}");
        if (s.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[警告]");
            foreach (var w in s.Warnings)
            {
                sb.AppendLine("- " + w);
            }
        }

        return sb.ToString();
    }

    private static string FormatBool(bool? value) => value is null ? "-" : value.Value ? "是" : "否";

    private static string FormatNullable(int? value, string suffix) =>
        value is null ? "-" : value.Value + suffix;

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "-";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }

        return $"{v:0.##} {units[i]} ({bytes})";
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "item" : name;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
