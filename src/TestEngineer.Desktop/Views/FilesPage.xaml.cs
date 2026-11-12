using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TestEngineer.Desktop.ViewModels;

namespace TestEngineer.Desktop.Views;

public partial class FilesPage : UserControl
{
    private Point _dragStart;
    private bool _dragPending;
    private bool _dragArmed;

    public FilesPage()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is FilesPageViewModel vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not FilesPageViewModel vm) return;
        if (FileList.SelectedItem is FileRowItem { CanInteract: true, IsDirectory: true } row)
        {
            vm.EnterCommand.Execute(row);
        }
    }

    private void OnCheckBoxClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: FileRowItem row }
            && DataContext is FilesPageViewModel vm)
        {
            e.Handled = true;
            vm.ToggleSelectCommand.Execute(row);
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not FilesPageViewModel vm) return;
        var row = FindRowItem(e.OriginalSource as DependencyObject);
        if (row is null || !row.CanInteract)
        {
            e.Handled = true;
            return;
        }

        vm.FocusedEntry = row;
        if (!row.IsSelected)
        {
            vm.ToggleSelectCommand.Execute(row);
        }
    }

    private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragPending = true;

        if (DataContext is not FilesPageViewModel vm) return;

        // Ctrl/Cmd+点击切换选中
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            || (Keyboard.Modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
        {
            var row = FindRowItem(e.OriginalSource as DependencyObject);
            if (row is { CanInteract: true })
            {
                vm.ToggleSelectCommand.Execute(row);
                vm.FocusedEntry = row;
                e.Handled = true;
                _dragPending = false;
            }
        }
    }

    private void OnListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragPending || _dragArmed || e.LeftButton != MouseButtonState.Pressed
            || DataContext is not FilesPageViewModel vm)
        {
            return;
        }

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragPending = false;

        // 未勾选时：把当前行纳入选中再拖出
        if (!vm.HasSelection)
        {
            var row = FindRowItem(e.OriginalSource as DependencyObject);
            if (row is { CanInteract: true })
            {
                vm.FocusedEntry = row;
                if (!row.IsSelected)
                {
                    vm.ToggleSelectCommand.Execute(row);
                }
            }
        }

        if (!vm.HasSelection || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        // 只记住拖放手势。真正拉取要等松开鼠标，并且不能进入系统拖放循环，否则界面线程会一直被占住。
        _dragArmed = true;
        Mouse.OverrideCursor = Cursors.Hand;
        FileList.CaptureMouse();
    }

    private async void OnListPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragArmed)
        {
            _dragPending = false;
            return;
        }

        EndDragGesture();
        if (DataContext is not FilesPageViewModel vm)
        {
            return;
        }

        var folder = ExplorerDropFolder.TryGetFolderAtCursor();
        if (string.IsNullOrWhiteSpace(folder))
        {
            vm.Status = "请把条目拖到资源管理器里打开的文件夹上再松开";
            return;
        }

        await vm.ExportDroppedAsync(folder);
    }

    private void OnListPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_dragArmed || e.Key != Key.Escape)
        {
            return;
        }

        EndDragGesture();
        e.Handled = true;
        if (DataContext is FilesPageViewModel vm)
        {
            vm.Status = "已取消拖出";
        }
    }

    private void EndDragGesture()
    {
        _dragArmed = false;
        _dragPending = false;
        if (FileList.IsMouseCaptured)
        {
            FileList.ReleaseMouseCapture();
        }

        Mouse.OverrideCursor = null;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not FilesPageViewModel vm) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        await vm.PushLocalFilesAsync(files);
    }

    private static FileRowItem? FindRowItem(DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (origin is FrameworkElement { DataContext: FileRowItem row })
            {
                return row;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return null;
    }
}
