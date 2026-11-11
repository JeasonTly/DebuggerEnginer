using System.Windows.Controls;
using System.Windows.Input;
using TestEngineer.Desktop.ViewModels;

namespace TestEngineer.Desktop.Views;

public partial class ConsolePage : UserControl
{
    public ConsolePage()
    {
        InitializeComponent();
    }

    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is ConsolePageViewModel vm)
        {
            vm.OnPreviewKeyDown(e);
        }
    }
}
