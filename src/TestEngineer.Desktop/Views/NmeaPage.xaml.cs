using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TestEngineer.Desktop.ViewModels;

namespace TestEngineer.Desktop.Views;

public partial class NmeaPage : UserControl
{
    public NmeaPage()
    {
        InitializeComponent();
    }

    private void OnFolderDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFolderDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not NmeaPageViewModel vm)
        {
            return;
        }

        var paths = ReadFiles(e)
            .Where(path => Directory.Exists(path) || File.Exists(path))
            .ToArray();
        if (paths.Length == 0)
        {
            e.Handled = true;
            return;
        }

        vm.ApplyDroppedPaths(paths);
        e.Handled = true;
    }

    private void OnChartRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NmeaFileRowItem row }
            && DataContext is NmeaPageViewModel vm)
        {
            vm.SelectedRow = row;
        }
    }

    private static string[] ReadFiles(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return [];
        }

        return e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
    }
}
