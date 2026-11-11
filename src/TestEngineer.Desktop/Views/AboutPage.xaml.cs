using System.Windows;
using System.Windows.Controls;
using TestEngineer.Core.Settings;
using TestEngineer.Desktop.ViewModels;

namespace TestEngineer.Desktop.Views;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
    }

    private void OnThemeSegmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || DataContext is not AboutPageViewModel vm)
        {
            return;
        }

        if (Enum.TryParse<AppThemeMode>(tag, ignoreCase: true, out var mode))
        {
            vm.SelectedTheme = mode;
        }
    }
}
