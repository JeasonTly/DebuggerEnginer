using System.Windows;
using Microsoft.Win32;

namespace TestEngineer.Desktop.Services;

/// <summary>把文件框和自有对话框放到主窗口之上，并暂时藏起投屏子窗口。</summary>
internal static class ModalSession
{
    public static IDisposable Suspend()
    {
        var main = Application.Current?.MainWindow as MainWindow;
        return main?.SuspendMirrorAirspace() ?? Noop.Instance;
    }

    public static Window? Owner => Application.Current?.MainWindow;

    public static bool? Show(CommonDialog dialog)
    {
        using var lease = Suspend();
        var owner = Owner;
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    public static bool? Show(Window dialog)
    {
        using var lease = Suspend();
        var owner = Owner;
        if (owner is not null)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        return dialog.ShowDialog();
    }

    public static MessageBoxResult Alert(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        using var lease = Suspend();
        var owner = Owner;
        return owner is null
            ? MessageBox.Show(message, title, buttons, image)
            : MessageBox.Show(owner, message, title, buttons, image);
    }

    private sealed class Noop : IDisposable
    {
        public static readonly Noop Instance = new();

        public void Dispose()
        {
        }
    }
}
