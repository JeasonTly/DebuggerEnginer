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
}
