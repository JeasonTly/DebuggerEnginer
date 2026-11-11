using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TestEngineer.Core.Models;
using TestEngineer.Desktop.ViewModels;

namespace TestEngineer.Desktop.Views;

public partial class FilesPage : UserControl
{
    private Point _dragStart;
    private bool _dragPending;

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
        if (sender is ListBox { SelectedItem: FileEntry entry } && DataContext is FilesPageViewModel vm)
        {
            vm.EnterCommand.Execute(entry);
        }
    }

    private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragPending = true;
    }

    private async void OnListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragPending || e.LeftButton != MouseButtonState.Pressed || DataContext is not FilesPageViewModel vm)
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
        var locals = await vm.PrepareDragOutLocalPathsAsync();
        if (locals.Count == 0)
        {
            return;
        }

        var data = new DataObject(DataFormats.FileDrop, locals.ToArray());
        DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy);
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
}
