using System.Windows.Controls;
using System.Windows.Input;
using TestEngineer.Core.Models;
using TestEngineer.Desktop.ViewModels;

namespace TestEngineer.Desktop.Views;

public partial class DebugPage : UserControl
{
    public DebugPage()
    {
        InitializeComponent();
    }

    private void OnCommandDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: DebugCommandDefinition cmd } && DataContext is DebugPageViewModel vm)
        {
            vm.RunCommand.Execute(cmd);
        }
    }
}
