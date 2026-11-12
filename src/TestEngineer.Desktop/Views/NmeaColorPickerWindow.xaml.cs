using System.Windows;
using TestEngineer.Desktop.ViewModels;

namespace TestEngineer.Desktop.Views;

public partial class NmeaColorPickerWindow : Window
{
    public NmeaColorPickerWindow(NmeaColorPickerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
