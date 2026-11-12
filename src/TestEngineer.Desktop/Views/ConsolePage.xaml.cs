using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TestEngineer.Desktop.ViewModels;

namespace TestEngineer.Desktop.Views;

public partial class ConsolePage : UserControl
{
    private bool _stickToBottom = true;
    private bool _suppressHistoryClick;

    public ConsolePage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ConsolePageViewModel vm)
            {
                vm.OutputAppended -= OnOutputAppended;
                vm.OutputAppended += OnOutputAppended;
            }
        };
    }

    private void SyncInputToViewModel()
    {
        if (DataContext is ConsolePageViewModel vm)
        {
            vm.Input = InputBox.Text ?? string.Empty;
        }
    }

    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        SyncInputToViewModel();
        if (DataContext is ConsolePageViewModel vm)
        {
            vm.OnPreviewKeyDown(e);
        }
    }

    private void OnRunPreviewDown(object sender, MouseButtonEventArgs e)
    {
        // 点击运行前先把输入框文本同步到 VM（避免绑定未提交导致空命令直接 return）
        SyncInputToViewModel();
    }

    private void OnHistoryRemovePreviewDown(object sender, MouseButtonEventArgs e)
    {
        _suppressHistoryClick = true;
    }

    private void OnHistoryItemClick(object sender, MouseButtonEventArgs e)
    {
        if (_suppressHistoryClick)
        {
            _suppressHistoryClick = false;
            return;
        }

        if (sender is not FrameworkElement { DataContext: string line })
        {
            return;
        }

        if (DataContext is ConsolePageViewModel vm)
        {
            vm.FillHistoryCommand.Execute(line);
        }
    }

    private void OnOutputScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
        {
            _stickToBottom = OutputBox.VerticalOffset + OutputBox.ViewportHeight
                             >= OutputBox.ExtentHeight - 24;
        }
    }

    private void OnOutputAppended(object? sender, EventArgs e)
    {
        if (!_stickToBottom) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (OutputBox.SelectionLength == 0)
            {
                OutputBox.ScrollToEnd();
            }
        });
    }
}
