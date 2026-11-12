using System.Windows;
using System.Windows.Controls;
using TestEngineer.Desktop.ViewModels;

namespace TestEngineer.Desktop.Views;

public partial class AppsPage : UserControl
{
    public AppsPage()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppsPageViewModel vm)
        {
            await vm.RefreshCommand.ExecuteAsync(null);
        }
    }

    private void OnSegmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && DataContext is AppsPageViewModel vm)
        {
            vm.Segment = tag;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not AppsPageViewModel vm) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        await vm.InstallLocalApksAsync(files);
    }
}
