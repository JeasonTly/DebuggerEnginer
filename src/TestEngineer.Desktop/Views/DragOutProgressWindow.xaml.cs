using System.ComponentModel;
using System.Windows;
using TestEngineer.Core.Models;

namespace TestEngineer.Desktop.Views;

/// <summary>松手落到目标后才出现。可以隐藏，传输继续在后台跑。</summary>
public partial class DragOutProgressWindow : Window
{
    private bool _allowClose;
    private CancellationTokenSource? _cancel;

    public DragOutProgressWindow()
    {
        InitializeComponent();
    }

    public bool CancelRequested { get; private set; }

    public void Bind(CancellationTokenSource cancel) => _cancel = cancel;

    public void Prepare(string title)
    {
        Title = title;
        TitleText.Text = title;
        DetailText.Text = "准备中…";
        Bar.IsIndeterminate = true;
        Bar.Value = 0;
        CancelButton.Content = "取消";
        _allowClose = false;
    }

    public void Apply(RunningTaskState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Apply(state));
            return;
        }

        if (_allowClose)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(state.Title))
        {
            TitleText.Text = state.Title;
        }

        DetailText.Text = string.IsNullOrWhiteSpace(state.Message) ? "正在读取…" : state.Message;
        if (state.Progress is { } fraction)
        {
            Bar.IsIndeterminate = false;
            Bar.Value = Math.Clamp(fraction * 100, 0, 100);
        }
    }

    public void ShowFailure(string message, string title = "拖出失败")
    {
        if (!IsVisible)
        {
            ForceClose();
            return;
        }

        TitleText.Text = title;
        DetailText.Text = message;
        Bar.IsIndeterminate = false;
        Bar.Value = 0;
        CancelButton.Content = "关闭";
        CancelRequested = false;
    }

    public void ShowCompleted(string message, string title = "拖出完成")
    {
        if (!IsVisible)
        {
            ForceClose();
            return;
        }

        TitleText.Text = title;
        DetailText.Text = message;
        Bar.IsIndeterminate = false;
        Bar.Value = 100;
        CancelButton.Content = "关闭";
        CancelRequested = false;
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (CancelButton.Content as string == "关闭")
        {
            ForceClose();
            return;
        }

        CancelRequested = true;
        DetailText.Text = "正在取消…";
        _cancel?.Cancel();
    }
}
