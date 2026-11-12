using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TestEngineer.Desktop.ViewModels;
using TestEngineer.Mirror;

namespace TestEngineer.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IMirrorSession _mirror;

    /// <summary>投屏栏处于用户偏好宽度时的主窗口宽度；横屏加宽时在此基础上向右扩展。</summary>
    private double _windowWidthAtPreferredMirror;
    private WindowState _windowStateBeforeFullscreen = WindowState.Normal;
    private double _widthBeforeFullscreen;
    private double _heightBeforeFullscreen;
    private double _leftBeforeFullscreen;
    private double _topBeforeFullscreen;
    private DispatcherTimer? _resizeSettledTimer;
    private DispatcherTimer? _mirrorRestartTimer;
    private WindowState _lastWindowState = WindowState.Normal;
    private bool _restartMirrorWhenShown;
    private bool _mirrorReloading;
    private Task? _mirrorPauseTask;

    public MainWindow(MainViewModel viewModel, IMirrorSession mirror)
    {
        _viewModel = viewModel;
        _mirror = mirror;
        InitializeComponent();
        DataContext = viewModel;
        _viewModel.MirrorAttachRequested += OnMirrorAttachRequested;
        _viewModel.MirrorAspectChanged += OnMirrorAspectChanged;
        _viewModel.MirrorLayoutChanged += OnMirrorLayoutChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _mirror.StateChanged += (_, _) => Dispatcher.Invoke(SyncMirrorHost);
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
        SizeChanged += OnWindowSizeChanged;
        StateChanged += OnWindowStateChanged;
        Closing += OnClosing;
        _lastWindowState = WindowState;
    }

    private int _closeGate;

    /// <summary>先停掉 scrcpy，再真正关闭。否则子窗口会被拆回桌面，进程也来不及退出。</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _closeGate, 1, 0) != 0)
        {
            return;
        }

        e.Cancel = true;
        try
        {
            _viewModel.StopMirrorForExit();
            await _mirror.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // 停投屏失败也要继续退出。
        }

        Close();
    }

    /// <summary>对话框打开期间隐藏嵌入的投屏窗口，结束时恢复。</summary>
    public IDisposable SuspendMirrorAirspace() => new AirspaceLease(MirrorHost);

    private sealed class AirspaceLease : IDisposable
    {
        private Controls.ScrcpyHwndHost? _host;

        public AirspaceLease(Controls.ScrcpyHwndHost host)
        {
            _host = host;
            host.SuspendAirspace();
        }

        public void Dispose()
        {
            _host?.ResumeAirspace();
            _host = null;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        _windowWidthAtPreferredMirror = Width;
        ReportMirrorPanelSize();
        await _viewModel.InitializeMirrorAsync();
        SyncMirrorHost();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ReportMirrorPanelSize();
        // 仅在投屏栏为偏好宽度时记住壳宽，避免把横屏加宽写进基准
        if (!_viewModel.IsMirrorFullscreen
            && Math.Abs(_viewModel.MirrorWidth - _viewModel.PreferredMirrorWidth) < 1.0)
        {
            _windowWidthAtPreferredMirror = Width;
        }

        // 拖边界过程中同步宿主矩形；停下后再补一次，避免中间帧客户区为 0 把画面弄丢
        RefreshMirrorSurface();
        ScheduleResizeSettledSync();
    }

    private void ScheduleResizeSettledSync()
    {
        _resizeSettledTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };
        _resizeSettledTimer.Tick -= OnResizeSettled;
        _resizeSettledTimer.Tick += OnResizeSettled;
        _resizeSettledTimer.Stop();
        _resizeSettledTimer.Start();
    }

    private void OnResizeSettled(object? sender, EventArgs e)
    {
        _resizeSettledTimer?.Stop();
        ReportMirrorPanelSize();
        if (_mirror.State != MirrorSessionState.Running || _mirror.WindowHandle == IntPtr.Zero || _mirrorReloading)
        {
            return;
        }

        // 尺寸稳定后：重新确认父窗关系 + 按舞台居中（不 Detach，避免闪断）
        MirrorPlaceholder.Visibility = Visibility.Collapsed;
        MirrorHost.Visibility = Visibility.Visible;
        MirrorHost.Attach(_mirror.WindowHandle);
        RefreshMirrorSurface();
    }

    private void ReportMirrorPanelSize()
    {
        if (MirrorStageBorder is null) return;
        var w = MirrorStageBorder.ActualWidth;
        var h = MirrorStageBorder.ActualHeight;
        if (w > 1 && h > 1)
        {
            _viewModel.ReportMirrorPanelSize(w, h);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_viewModel.IsMirrorFullscreen)
            {
                _viewModel.IsMirrorFullscreen = false;
                e.Handled = true;
                return;
            }

            if (_viewModel.IsDeviceMenuOpen)
            {
                _viewModel.CloseDeviceMenuCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F11)
        {
            _viewModel.ToggleMirrorFullscreenCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPageViewModel))
        {
            PlayPageEnterAnimation();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsMirrorFullscreen))
        {
            ApplyMirrorFullscreenLayout();
        }
    }

    private void ApplyMirrorFullscreenLayout()
    {
        RequestMirrorReload();

        if (_viewModel.IsMirrorFullscreen)
        {
            _windowStateBeforeFullscreen = WindowState;
            _widthBeforeFullscreen = Width;
            _heightBeforeFullscreen = Height;
            _leftBeforeFullscreen = Left;
            _topBeforeFullscreen = Top;

            // 投屏占满工作区列：导航/工作区/分隔条已 Collapsed，列宽让给投屏
            NavColumn.Width = new GridLength(0);
            WorkColumn.Width = new GridLength(0);
            WorkColumn.MinWidth = 0;
            SplitterColumn.Width = new GridLength(0);
            MirrorColumn.Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(MirrorPane, 0);
            Grid.SetColumnSpan(MirrorPane, 4);
            MirrorPane.ClearValue(FrameworkElement.WidthProperty);
            MirrorPane.ClearValue(FrameworkElement.MaxWidthProperty);
            MirrorPane.HorizontalAlignment = HorizontalAlignment.Stretch;
            MirrorPane.BorderThickness = new Thickness(0);
            MirrorFullscreenIcon.Content = FindResource("Icon.Mirror.FullscreenExit");

            WindowState = WindowState.Maximized;
        }
        else
        {
            Grid.SetColumn(MirrorPane, 3);
            Grid.SetColumnSpan(MirrorPane, 1);
            NavColumn.Width = GridLength.Auto;
            WorkColumn.Width = new GridLength(1, GridUnitType.Star);
            WorkColumn.MinWidth = 300;
            SplitterColumn.Width = new GridLength(5);
            MirrorColumn.Width = GridLength.Auto;
            MirrorPane.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(MainViewModel.MirrorWidth)));
            MirrorPane.MaxWidth = 720;
            MirrorPane.HorizontalAlignment = HorizontalAlignment.Left;
            MirrorPane.BorderThickness = new Thickness(1, 0, 0, 0);
            MirrorFullscreenIcon.Content = FindResource("Icon.Mirror.Fullscreen");

            WindowState = _windowStateBeforeFullscreen == WindowState.Maximized
                ? WindowState.Normal
                : _windowStateBeforeFullscreen;
            if (WindowState == WindowState.Normal)
            {
                Width = Math.Max(MinWidth, _widthBeforeFullscreen);
                Height = Math.Max(MinHeight, _heightBeforeFullscreen);
                Left = _leftBeforeFullscreen;
                Top = _topBeforeFullscreen;
            }

            ExpandWindowForMirrorWidth();
        }

        UpdateLayout();
        ScheduleMirrorRestart();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        var previous = _lastWindowState;
        _lastWindowState = WindowState;

        if (WindowState == WindowState.Minimized)
        {
            _mirrorRestartTimer?.Stop();
            if (_viewModel.MirrorShouldRun)
            {
                _restartMirrorWhenShown = true;
                BeginMirrorReload();
            }

            return;
        }

        var restored = (previous == WindowState.Maximized && WindowState == WindowState.Normal)
                       || (previous == WindowState.Minimized && _restartMirrorWhenShown);
        _restartMirrorWhenShown = false;
        if (restored)
        {
            RequestMirrorReload();
            return;
        }

        if (_mirrorReloading)
        {
            return;
        }

        ScheduleResizeSettledSync();
    }

    /// <summary>关掉当前画面并盖上加载层，等布局稳定后再启动。</summary>
    private void RequestMirrorReload()
    {
        if (!_viewModel.MirrorShouldRun)
        {
            return;
        }

        BeginMirrorReload();
        ScheduleMirrorRestart();
    }

    private void BeginMirrorReload()
    {
        if (_mirrorReloading)
        {
            return;
        }

        _mirrorReloading = true;
        MirrorPlaceholder.Visibility = Visibility.Collapsed;
        MirrorHost.Detach(showAsTopLevel: false);
        MirrorHost.Visibility = Visibility.Collapsed;
        MirrorLoading.Visibility = Visibility.Visible;
        _mirrorPauseTask = _viewModel.PauseMirrorForReloadAsync();
    }

    /// <summary>等布局稳定后再启动，让新会话按当前舞台尺寸打开。</summary>
    private void ScheduleMirrorRestart()
    {
        if (!_viewModel.MirrorShouldRun)
        {
            return;
        }

        _mirrorRestartTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _mirrorRestartTimer.Tick -= OnMirrorRestartTick;
        _mirrorRestartTimer.Tick += OnMirrorRestartTick;
        _mirrorRestartTimer.Stop();
        _mirrorRestartTimer.Start();
    }

    private async void OnMirrorRestartTick(object? sender, EventArgs e)
    {
        _mirrorRestartTimer?.Stop();
        if (WindowState == WindowState.Minimized)
        {
            _restartMirrorWhenShown = _viewModel.MirrorShouldRun;
            return;
        }

        var pause = _mirrorPauseTask;
        _mirrorPauseTask = null;
        if (pause is not null)
        {
            try
            {
                await pause.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _viewModel.MirrorStatus = "启动失败：" + ex.Message;
            }
        }

        if (!_viewModel.MirrorShouldRun || WindowState == WindowState.Minimized)
        {
            EndMirrorReload(showPlaceholder: !_viewModel.MirrorShouldRun);
            return;
        }

        UpdateLayout();
        ReportMirrorPanelSize();
        try
        {
            await _viewModel.RestartMirrorAsync();
        }
        catch (Exception ex)
        {
            _viewModel.MirrorStatus = "启动失败：" + ex.Message;
        }

        if (_mirror.State != MirrorSessionState.Running)
        {
            EndMirrorReload(showPlaceholder: true);
        }
    }

    private void EndMirrorReload(bool showPlaceholder)
    {
        _mirrorReloading = false;
        MirrorLoading.Visibility = Visibility.Collapsed;
        if (showPlaceholder && _mirror.State != MirrorSessionState.Running)
        {
            MirrorPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void RefreshMirrorSurface()
    {
        if (MirrorHost is null || _mirrorReloading)
        {
            return;
        }

        ReportMirrorPanelSize();
        if (_mirror.State == MirrorSessionState.Running && _mirror.WindowHandle != IntPtr.Zero)
        {
            MirrorHost.ForceSyncSize();
        }
    }

    /// <summary>页卡切换：轻微右滑 + 淡入，避免生硬跳切。</summary>
    private void PlayPageEnterAnimation()
    {
        if (PageHost is null) return;

        PageHost.BeginAnimation(OpacityProperty, null);
        PageHostSlide.BeginAnimation(TranslateTransform.XProperty, null);

        PageHost.Opacity = 0;
        PageHostSlide.X = 18;

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease };
        var slide = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
        PageHost.BeginAnimation(OpacityProperty, fade);
        PageHostSlide.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void OnMirrorAttachRequested(object? sender, EventArgs e) => SyncMirrorHost();

    private void OnMirrorAspectChanged(object? sender, double aspect)
    {
        if (aspect > 0.05)
        {
            MirrorHost.AspectRatio = aspect;
        }

        ScheduleForceSync();
    }

    private void OnMirrorLayoutChanged(object? sender, EventArgs e)
    {
        if (_viewModel.IsMirrorFullscreen)
        {
            ScheduleForceSync();
            return;
        }

        // 横屏加宽投屏栏：整窗向右扩展，不挤占左侧功能区
        ExpandWindowForMirrorWidth();
        ScheduleForceSync();
    }

    /// <summary>
    /// 投屏栏超出用户偏好宽度时，主窗口增加同样像素并向右伸展；
    /// 竖屏恢复偏好宽度时，窗口回到基准宽。
    /// </summary>
    private void ExpandWindowForMirrorWidth()
    {
        if (_windowWidthAtPreferredMirror < MinWidth)
        {
            _windowWidthAtPreferredMirror = Math.Max(MinWidth, Width);
        }

        var preferred = _viewModel.PreferredMirrorWidth;
        var extra = Math.Max(0, _viewModel.MirrorWidth - preferred);
        var targetWidth = _windowWidthAtPreferredMirror + extra;
        targetWidth = Math.Max(MinWidth, targetWidth);

        var work = SystemParameters.WorkArea;
        var maxWidth = Math.Max(MinWidth, work.Right - Left);
        if (targetWidth > maxWidth)
        {
            // 右侧不够时整体左移，尽量保住扩展，避免挤压工作区
            var need = targetWidth - maxWidth;
            var shift = Math.Min(need, Math.Max(0, Left - work.Left));
            if (shift > 0.5)
            {
                Left -= shift;
                maxWidth = Math.Max(MinWidth, work.Right - Left);
            }

            targetWidth = Math.Min(targetWidth, maxWidth);
        }

        if (Math.Abs(Width - targetWidth) > 0.5)
        {
            Width = targetWidth;
        }
    }

    private void OnMirrorStageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ReportMirrorPanelSize();
        ScheduleForceSync();
    }

    private void ScheduleForceSync()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_mirrorReloading || _mirror.State != MirrorSessionState.Running || _mirror.WindowHandle == IntPtr.Zero)
            {
                return;
            }

            ReportMirrorPanelSize();
            MirrorHost.ForceSyncSize();
        }, DispatcherPriority.Loaded);
    }

    private void SyncMirrorHost()
    {
        if (_mirror.State == MirrorSessionState.Running && _mirror.WindowHandle != IntPtr.Zero)
        {
            _mirrorReloading = false;
            MirrorLoading.Visibility = Visibility.Collapsed;
            MirrorPlaceholder.Visibility = Visibility.Collapsed;
            MirrorHost.Visibility = Visibility.Visible;
            if (_mirror.VideoSize is { Width: > 0, Height: > 0 } vs)
            {
                MirrorHost.AspectRatio = vs.AspectRatio;
            }

            MirrorHost.Attach(_mirror.WindowHandle);
            ScheduleForceSync();
            ScheduleResizeSettledSync();
            Dispatcher.BeginInvoke(() =>
            {
                if (_mirrorReloading)
                {
                    return;
                }

                ReportMirrorPanelSize();
                MirrorHost.ForceSyncSize();
            }, DispatcherPriority.ContextIdle);
        }
        else if (_mirrorReloading || _mirror.State is MirrorSessionState.Starting)
        {
            MirrorHost.Visibility = Visibility.Collapsed;
            MirrorPlaceholder.Visibility = Visibility.Collapsed;
            MirrorLoading.Visibility = Visibility.Visible;
        }
        else
        {
            MirrorHost.Detach(showAsTopLevel: false);
            MirrorHost.Visibility = Visibility.Collapsed;
            MirrorLoading.Visibility = Visibility.Collapsed;
            MirrorPlaceholder.Visibility = Visibility.Visible;
        }
    }
}
