using System.Windows;
using TestEngineer.Desktop.ViewModels;
using TestEngineer.Mirror;

namespace TestEngineer.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IMirrorSession _mirror;

    public MainWindow(MainViewModel viewModel, IMirrorSession mirror)
    {
        _viewModel = viewModel;
        _mirror = mirror;
        InitializeComponent();
        DataContext = viewModel;
        _viewModel.MirrorAttachRequested += OnMirrorAttachRequested;
        _mirror.StateChanged += (_, _) => Dispatcher.Invoke(SyncMirrorHost);
        Closed += async (_, _) =>
        {
            MirrorHost.Detach();
            await _mirror.DisposeAsync();
        };
    }

    private void OnMirrorAttachRequested(object? sender, EventArgs e) => SyncMirrorHost();

    private void SyncMirrorHost()
    {
        if (_mirror.State == MirrorSessionState.Running && _mirror.WindowHandle != IntPtr.Zero)
        {
            MirrorPlaceholder.Visibility = Visibility.Collapsed;
            MirrorHost.Visibility = Visibility.Visible;
            MirrorHost.Attach(_mirror.WindowHandle);
        }
        else
        {
            MirrorHost.Detach();
            // Win32 static 宿主默认可为白底；折叠后露出 MirrorStage #0A0E14
            MirrorHost.Visibility = Visibility.Collapsed;
            MirrorPlaceholder.Visibility = Visibility.Visible;
        }
    }
}
