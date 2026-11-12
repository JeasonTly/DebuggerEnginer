using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TestEngineer.Adb;
using TestEngineer.Desktop.Views;
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
            RefreshSelectionUi();
            _ = RefreshAsync();
        });
    }

    public ObservableCollection<FileRowItem> Entries { get; } = [];
    public ObservableCollection<FilesBreadcrumbItem> Breadcrumbs { get; } = [];

    [ObservableProperty] private string _currentPath = "/sdcard";
    [ObservableProperty] private string _rootMode = "sdcard";
    [ObservableProperty] private string _status = "等待加载";
    [ObservableProperty] private string _selectionSummary = "已选 0（跨目录）";
    [ObservableProperty] private string _selectionPathsSummary = "—";
    [ObservableProperty] private string _selectionPathsFull = "";
    [ObservableProperty] private string _clipboardSummary = "";
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _hasClipboard;
    [ObservableProperty] private bool _currentDirectoryWritable;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private FileRowItem? _focusedEntry;
    [ObservableProperty] private string _emptyHint = "";
    [ObservableProperty] private bool _showEmptyHint;
    [ObservableProperty] private string _pasteToolTip = "无可粘贴内容或目录只读";
    [ObservableProperty] private string _writeToolTip = "目录只读";

    private sealed record ClipboardOp(string Op, IReadOnlyList<string> Paths);

    private IReadOnlyList<string> TargetsFor(FileRowItem? row)
    {
        var entry = row?.Entry;
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

    partial void OnCurrentDirectoryWritableChanged(bool value) => RefreshCommandStates();
    partial void OnHasClipboardChanged(bool value) => RefreshCommandStates();
    partial void OnHasSelectionChanged(bool value) => RefreshCommandStates();

    private void RefreshCommandStates()
    {
        PasteCommand.NotifyCanExecuteChanged();
        CreateFolderCommand.NotifyCanExecuteChanged();
        ImportLocalCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        PasteToolTip = CanPaste() ? "粘贴到当前目录" : "无可粘贴内容或目录只读";
        WriteToolTip = CurrentDirectoryWritable ? "写入当前目录" : "目录只读";
    }

    private void RefreshSelectionUi()
    {
        HasSelection = _selection.Count > 0;
        SelectionSummary = $"已选 {_selection.Count}（跨目录）";
        var paths = _selection.Paths;
        SelectionPathsFull = paths.Count == 0 ? "" : string.Join("\n", paths);
        SelectionPathsSummary = paths.Count == 0
            ? "—"
            : string.Join(" · ", paths);
        foreach (var row in Entries)
        {
            row.IsSelected = _selection.Contains(row.AbsolutePath);
        }

        ClearSelectionCommand.NotifyCanExecuteChanged();
    }

    private void SetClipboard(ClipboardOp? op)
    {
        _clipboard = op;
        if (op is null || op.Paths.Count == 0)
        {
            HasClipboard = false;
            ClipboardSummary = "";
        }
        else
        {
            HasClipboard = true;
            ClipboardSummary = op.Op == "cut"
                ? $"剪切：{op.Paths.Count} 项"
                : $"复制：{op.Paths.Count} 项";
        }

        RefreshCommandStates();
    }

    private bool CanPaste() =>
        _clipboard is not null && _clipboard.Paths.Count > 0 && CurrentDirectoryWritable;

    private bool CanWriteCurrent() => CurrentDirectoryWritable;

    private bool CanClearSelection() => _selection.Count > 0;

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
            CurrentDirectoryWritable = false;
            Status = "设备未连接";
            EmptyHint = "设备已断开";
            ShowEmptyHint = true;
            RebuildBreadcrumbs();
            RefreshCommandStates();
            return;
        }

        IsBusy = true;
        Status = "列出目录…";
        ShowEmptyHint = false;
        try
        {
            var list = await _files.ListAsync(serial, CurrentPath, probeAccess: true);
            Entries.Clear();
            foreach (var e in list)
            {
                Entries.Add(new FileRowItem(e, _selection.Contains(e.AbsolutePath)));
            }

            // 探测当前目录可写性
            var dirProbe = await _files.ProbeAccessAsync(serial, [
                new FileEntry(
                    RemotePath.GetName(CurrentPath) is "/" or "" ? "root" : RemotePath.GetName(CurrentPath),
                    CurrentPath,
                    true,
                    "drwx------",
                    -1,
                    null),
            ]);
            CurrentDirectoryWritable = dirProbe.Count > 0 && dirProbe[0].CanWrite;

            RebuildBreadcrumbs();
            RefreshSelectionUi();
            Status = $"{Entries.Count} 项 · {CurrentPath}" + (CurrentDirectoryWritable ? " 可写" : " 只读");
            if (Entries.Count == 0)
            {
                EmptyHint = CurrentDirectoryWritable ? "此目录为空 · 可上传文件" : "此目录为空";
                ShowEmptyHint = true;
            }
            else
            {
                ShowEmptyHint = false;
            }
        }
        catch (Exception ex)
        {
            Entries.Clear();
            Status = "列出失败：" + ex.Message;
            EmptyHint = Status;
            ShowEmptyHint = true;
            CurrentDirectoryWritable = false;
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    [RelayCommand]
    private void Enter(FileRowItem? row)
    {
        if (row is null || !row.IsDirectory || !row.CanInteract) return;
        CurrentPath = row.AbsolutePath;
        if (CurrentPath.StartsWith("/sdcard", StringComparison.Ordinal))
        {
            RootMode = "sdcard";
        }
        else
        {
            RootMode = "root";
        }

        _ = RefreshAsync();
    }

    [RelayCommand]
    private void GoBreadcrumb(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        CurrentPath = path;
        RootMode = path.StartsWith("/sdcard", StringComparison.Ordinal) ? "sdcard" : "root";
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void ToggleSelect(FileRowItem? row)
    {
        if (row is null || !row.CanInteract) return;
        _selection.Toggle(row.Entry);
        row.IsSelected = _selection.Contains(row.AbsolutePath);
        RefreshSelectionUi();
    }

    [RelayCommand(CanExecute = nameof(CanClearSelection))]
    private void ClearSelection()
    {
        _selection.Clear();
        RefreshSelectionUi();
    }

    [RelayCommand]
    private void ViewSelection()
    {
        var paths = _selection.Paths;
        var body = paths.Count == 0 ? "(空)" : string.Join(Environment.NewLine, paths);
        var result = MessageBox.Show(
            body + Environment.NewLine + Environment.NewLine + "点「是」清空选中，点「否」关闭。",
            $"跨目录已选 {_selection.Count}",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (result == MessageBoxResult.Yes)
        {
            ClearSelection();
        }
    }

    [RelayCommand]
    private void ClearClipboard()
    {
        SetClipboard(null);
    }

    [RelayCommand]
    private void Copy(FileRowItem? row)
    {
        EnsureContextSelection(row);
        var paths = TargetsFor(row ?? FocusedEntry);
        if (paths.Count == 0) return;
        SetClipboard(new ClipboardOp("copy", paths));
    }

    [RelayCommand]
    private void Cut(FileRowItem? row)
    {
        EnsureContextSelection(row);
        var paths = TargetsFor(row ?? FocusedEntry);
        if (paths.Count == 0) return;
        SetClipboard(new ClipboardOp("cut", paths));
    }

    /// <summary>右键未选中项时先加入 bag。</summary>
    private void EnsureContextSelection(FileRowItem? row)
    {
        if (row is null || !row.CanInteract) return;
        if (!_selection.Contains(row.AbsolutePath))
        {
            _selection.Add(row.Entry);
            row.IsSelected = true;
            RefreshSelectionUi();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task PasteAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _clipboard is null || _clipboard.Paths.Count == 0) return;
        if (!CurrentDirectoryWritable) return;

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
            SetClipboard(null);
        }

        Status = $"粘贴完成：成功 {ok}，失败 {fail}";
        IsBusy = false;
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanWriteCurrent))]
    private async Task CreateFolderAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || !CurrentDirectoryWritable) return;

        var name = PromptFolderName();
        if (string.IsNullOrWhiteSpace(name)) return;

        var remote = RemotePath.Combine(CurrentPath, name.Trim());
        IsBusy = true;
        var result = await _files.MakeDirectoryAsync(serial, remote);
        Status = result.Succeeded ? $"已创建：{remote}" : "新建失败：" + result.FailureMessage;
        IsBusy = false;
        await RefreshAsync();
    }

    private static string? PromptFolderName()
    {
        var dialog = new Window
        {
            Title = "新建文件夹",
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current.MainWindow,
        };
        var nameBox = new TextBox
        {
            Margin = new Thickness(16, 16, 16, 8),
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "NewFolder",
        };
        var ok = new Button { Content = "创建", Width = 72, Height = 32, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 72, Height = 32, IsCancel = true };
        string? result = null;
        ok.Click += (_, _) =>
        {
            result = nameBox.Text;
            dialog.DialogResult = true;
        };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16),
            Children = { ok, cancel },
        };
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(nameBox);
        dialog.Content = root;
        nameBox.SelectAll();
        nameBox.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }

    [RelayCommand]
    private async Task DeleteAsync(FileRowItem? row)
    {
        EnsureContextSelection(row);
        var serial = _devices.Current?.Serial;
        var paths = TargetsFor(row ?? FocusedEntry);
        if (string.IsNullOrWhiteSpace(serial) || paths.Count == 0) return;

        var list = string.Join(Environment.NewLine, paths);
        if (MessageBox.Show(
                $"删除 {paths.Count} 项？{Environment.NewLine}{Environment.NewLine}{list}",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
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

        RefreshSelectionUi();
        Status = $"删除完成：成功 {ok}，失败 {fail}";
        IsBusy = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ExportSelectionAsync()
    {
        var serial = _devices.Current?.Serial;
        var paths = _selection.Count > 0 ? _selection.EffectiveRoots() : TargetsFor(FocusedEntry);
        if (paths.Count == 0)
        {
            MessageBox.Show("勾选或 Ctrl+点击；可跨目录累积后再导出。", "未选中", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(serial)) return;

        var dlg = new OpenFolderDialog { Title = "导出到本地目录" };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FolderName)) return;

        var folder = dlg.FolderName;
        var targets = paths.ToList();
        await RunTransferDialogAsync("正在导出", "导出完成", "导出失败", async (progress, token) =>
        {
            var ok = 0;
            var fail = 0;
            for (var i = 0; i < targets.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var remote = targets[i];
                var name = RemotePath.GetName(remote).Trim('/');
                if (string.IsNullOrEmpty(name) || name == "/")
                {
                    name = "root-export";
                }

                var local = System.IO.Path.Combine(folder, name);
                progress.Report(RunningTaskState.Begin("正在导出").Update((double)i / targets.Count, name));
                var result = await _files.PullAsync(serial, remote, local, progress, token).ConfigureAwait(false);
                if (result.Cancelled || token.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                if (result.Succeeded)
                {
                    ok++;
                }
                else
                {
                    fail++;
                }
            }

            if (ok == 0 && fail > 0)
            {
                throw new InvalidOperationException($"导出失败 {fail} 项");
            }

            return fail == 0 ? $"已导出 {ok} 项到 {folder}" : $"已导出 {ok} 项，失败 {fail} 项";
        });
    }

    [RelayCommand(CanExecute = nameof(CanWriteCurrent))]
    private async Task ImportLocalAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || !CurrentDirectoryWritable) return;
        var dlg = new OpenFileDialog { Multiselect = true, Title = "导入到当前目录" };
        if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;
        await PushLocalFilesAsync(dlg.FileNames);
    }

    public async Task PushLocalFilesAsync(IEnumerable<string> localPaths)
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        if (!CurrentDirectoryWritable)
        {
            Status = "目录只读，无法上传";
            return;
        }

        var list = localPaths.ToList();
        var remoteDirectory = CurrentPath;
        await RunTransferDialogAsync("正在导入", "导入完成", "导入失败", async (progress, token) =>
        {
            var ok = 0;
            var fail = 0;
            for (var i = 0; i < list.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var local = list[i];
                var name = System.IO.Path.GetFileName(local.TrimEnd('\\', '/'));
                if (!System.IO.File.Exists(local) && !System.IO.Directory.Exists(local))
                {
                    fail++;
                    continue;
                }

                progress.Report(RunningTaskState.Begin("正在导入").Update(list.Count == 0 ? 0 : (double)i / list.Count, name));
                var result = await _files.PushAsync(serial, local, remoteDirectory, progress, token).ConfigureAwait(false);
                if (result.Cancelled || token.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                if (result.Succeeded)
                {
                    ok++;
                }
                else
                {
                    fail++;
                }
            }

            if (ok == 0 && fail > 0)
            {
                throw new InvalidOperationException($"导入失败 {fail} 项");
            }

            return fail == 0 ? $"已导入 {ok} 项" : $"已导入 {ok} 项，失败 {fail} 项";
        });
        await RefreshAsync();
    }

    private async Task RunTransferDialogAsync(
        string title,
        string successTitle,
        string failureTitle,
        Func<IProgress<RunningTaskState>, CancellationToken, Task<string>> work)
    {
        var window = new DragOutProgressWindow { Owner = Application.Current?.MainWindow };
        window.Prepare(title);
        using var cts = new CancellationTokenSource();
        window.Bind(cts);
        window.Show();
        var lastUi = DateTime.UtcNow;
        IProgress<RunningTaskState> progress = new Progress<RunningTaskState>(state =>
        {
            var now = DateTime.UtcNow;
            if (state.IsActive && now - lastUi < TimeSpan.FromMilliseconds(80))
            {
                return;
            }

            lastUi = now;
            window.Apply(state);
            Status = string.IsNullOrWhiteSpace(state.Message) ? state.Title : state.Message;
        });

        try
        {
            var message = await work(progress, cts.Token).ConfigureAwait(true);
            window.ShowCompleted(message, successTitle);
            Status = message;
        }
        catch (OperationCanceledException)
        {
            window.ShowFailure("已取消", failureTitle);
            Status = title + "已取消";
        }
        catch (Exception ex)
        {
            var message = ex.GetBaseException().Message;
            window.ShowFailure(message, failureTitle);
            Status = title + "失败：" + message;
        }
    }

    /// <summary>松手后把选中项直接写入目标文件夹。拉取在后台进行，进度窗只负责显示。</summary>
    public async Task ExportDroppedAsync(string targetDirectory)
    {
        var serial = _devices.Current?.Serial;
        var paths = _selection.Count > 0 ? _selection.EffectiveRoots() : TargetsFor(FocusedEntry);
        if (string.IsNullOrWhiteSpace(serial) || paths.Count == 0)
        {
            Status = "没有可导出的文件";
            return;
        }

        if (!System.IO.Directory.Exists(targetDirectory))
        {
            Status = "目标文件夹不存在";
            return;
        }

        var window = new DragOutProgressWindow { Owner = Application.Current?.MainWindow };
        using var cts = new CancellationTokenSource();
        window.Bind(cts);
        window.Show();
        IProgress<RunningTaskState> progress = new Progress<RunningTaskState>(state =>
        {
            window.Apply(state);
            Status = string.IsNullOrWhiteSpace(state.Message) ? state.Title : state.Message;
        });

        try
        {
            var written = new List<string>();
            await Task.Run(async () =>
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var remote = paths[i];
                    var name = RemotePath.GetName(remote).Trim('/');
                    if (string.IsNullOrEmpty(name) || name == "/")
                    {
                        name = "export";
                    }

                    var local = System.IO.Path.Combine(targetDirectory, name);
                    progress.Report(RunningTaskState.Begin("正在导出到 " + targetDirectory).Update((double)i / paths.Count, name));
                    var result = await _files.PullAsync(serial, remote, local, progress, cts.Token).ConfigureAwait(false);
                    if (result.Cancelled || cts.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }

                    if (!result.Succeeded || (!System.IO.File.Exists(local) && !System.IO.Directory.Exists(local)))
                    {
                        var reason = string.IsNullOrWhiteSpace(result.FailureMessage) ? "目标位置没有 " + name : result.FailureMessage;
                        throw new InvalidOperationException(reason);
                    }

                    written.Add(local);
                }
            }, cts.Token).ConfigureAwait(true);

            var dest = written.Count == 1 ? written[0] : targetDirectory;
            window.ShowCompleted("已导出到 " + dest);
            Status = "已导出到 " + dest;
        }
        catch (OperationCanceledException)
        {
            window.ShowFailure("已取消");
            Status = "导出已取消";
        }
        catch (Exception ex)
        {
            window.ShowFailure(ex.GetBaseException().Message);
            Status = "导出失败：" + ex.GetBaseException().Message;
        }
    }

    /// <summary>将当前焦点/勾选项拉取到临时目录，供拖出到资源管理器。进度由对话框显示，不盖住文件页。</summary>
    public async Task<IReadOnlyList<string>> PrepareDragOutLocalPathsAsync(
        IProgress<RunningTaskState> progress,
        CancellationToken cancellationToken)
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

        var state = RunningTaskState.Begin("正在从设备拖出");
        ReportDragProgress(progress, state);
        var locals = new List<string>();
        for (var i = 0; i < paths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = paths[i];
            var name = RemotePath.GetName(path).Trim('/');
            if (string.IsNullOrEmpty(name) || name == "/") name = "export";
            var done = (double)i / paths.Count;
            state = state.Update(done, $"{i + 1} / {paths.Count} · {name}");
            ReportDragProgress(progress, state);
            var local = System.IO.Path.Combine(tempRoot, name);
            var inner = new Progress<RunningTaskState>(step =>
            {
                var fraction = done + (step.Progress ?? 0) / paths.Count;
                var line = string.IsNullOrWhiteSpace(step.Message) ? name : step.Message.Trim();
                ReportDragProgress(progress, state.Update(fraction, $"{i + 1} / {paths.Count} · {line}"));
            });
            var r = await _files.PullAsync(serial, path, local, inner, cancellationToken).ConfigureAwait(false);
            if (r.Succeeded && (System.IO.File.Exists(local) || System.IO.Directory.Exists(local)))
            {
                locals.Add(local);
            }
            else if (!r.Succeeded && !r.Cancelled)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.FailureMessage) ? "拖出失败" : r.FailureMessage);
            }
        }

        ReportDragProgress(progress, state.Update(1, $"已取出 {locals.Count} 项"));
        return locals;
    }

    private void ReportDragProgress(IProgress<RunningTaskState> progress, RunningTaskState state)
    {
        progress.Report(state);
        var text = string.IsNullOrWhiteSpace(state.Message) ? state.Title : state.Message;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Status = text;
            return;
        }

        dispatcher.BeginInvoke(() => Status = text);
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
            var shotPath = System.IO.Path.Combine(staging, "screencap.png");
            Status = "截屏…";
            var shot = await _debug.CaptureScreenshotAsync(serial, shotPath);
            notes.AppendLine(shot.Succeeded && System.IO.File.Exists(shotPath)
                ? "OK   screencap.png"
                : $"FAIL screencap.png — {shot.FailureMessage}");

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
        var segs = RemotePath.Breadcrumbs(CurrentPath);
        for (var i = 0; i < segs.Count; i++)
        {
            var seg = segs[i];
            var display = seg.Name is "/" or "" ? "根目录" : seg.Name;
            Breadcrumbs.Add(new FilesBreadcrumbItem(display, seg.Path, IsCurrent: i == segs.Count - 1, ShowSeparator: i > 0));
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

/// <summary>面包屑项。</summary>
public sealed record FilesBreadcrumbItem(string DisplayName, string Path, bool IsCurrent, bool ShowSeparator);
