using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestEngineer.Adb.Parsing;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;
using TestEngineer.Desktop.Services;
using TestEngineer.Mirror;

namespace TestEngineer.Desktop.ViewModels;

public enum AppPage
{
    Device,
    Apps,
    Files,
    Debug,
    Console,
    Cast,
    Developer,
    Nmea,
    About,
}

public sealed partial class DeviceListItem : ObservableObject
{
    public DeviceListItem(AndroidDevice device, bool isCurrent, string? androidVersion = null, string? buildType = null)
    {
        Device = device;
        IsCurrent = isCurrent;
        AndroidVersion = string.IsNullOrWhiteSpace(androidVersion) ? "?" : androidVersion;
        BuildType = string.IsNullOrWhiteSpace(buildType) ? "—" : buildType;
        ModelName = !string.IsNullOrWhiteSpace(device.Model)
            ? device.Model.Replace('_', '-')
            : (device.Product ?? device.Serial);
        Channel = ResolveChannel(device);
        StatusLabel = FormatStatusLabel(device.State);
        ChipPrimary = $"{ModelName} · Android {AndroidVersion} · {Channel}";
    }

    internal static string ResolveChannel(AndroidDevice device)
    {
        if (!string.IsNullOrWhiteSpace(device.Usb))
        {
            return "USB";
        }

        if (device.Serial.Contains(':', StringComparison.Ordinal))
        {
            return "网络";
        }

        return "USB";
    }

    internal static string FormatStatusLabel(DeviceState state) => state switch
    {
        DeviceState.Device => "在线",
        DeviceState.Unauthorized or DeviceState.Authorizing => "待授权",
        DeviceState.Offline => "离线",
        DeviceState.Connecting => "连接中",
        DeviceState.NoPermissions => "无权限",
        _ => "不可用",
    };

    /// <summary>选择未就绪设备时的提示（未认证单独说明）。</summary>
    internal static (string Title, string Message) FormatUnavailablePrompt(string displayName, DeviceState state) =>
        state is DeviceState.Unauthorized or DeviceState.Authorizing
            ? ("等待设备授权",
                $"「{displayName}」已开启 USB 调试，但尚未授权本电脑。\n请在手机上点击「允许 USB 调试」后再试。")
            : ("设备不可用",
                $"「{displayName}」当前不可用（{FormatStatusLabel(state)}）。");

    public AndroidDevice Device { get; }
    public bool IsCurrent { get; }
    public string ModelName { get; }
    public string AndroidVersion { get; }
    public string BuildType { get; }
    public string Channel { get; }
    public string StatusLabel { get; }
    public string ChipPrimary { get; }
    public string Serial => Device.Serial;
    public bool IsOnline => Device.IsOnline;
    public DeviceState State => Device.State;
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDeviceContext _devices;
    private readonly IAppSettings _settings;
    private readonly IMirrorSession _mirror;
    private readonly IAdbProcessRunner _adb;
    private readonly IDebugCommandService _debug;
    private readonly DevicePageViewModel _devicePage;
    private readonly AppsPageViewModel _appsPage;
    private readonly FilesPageViewModel _filesPage;
    private readonly DebugPageViewModel _debugPage;
    private readonly ConsolePageViewModel _consolePage;
    private readonly CastPageViewModel _castPage;
    private readonly DeveloperPageViewModel _developerPage;
    private readonly NmeaPageViewModel _nmeaPage;
    private readonly AboutPageViewModel _aboutPage;
    private bool _suppressSelect;
    private bool _preferMirrorRunning = true;
    private string? _activeMirrorSerial;
    private int _mirrorEpoch;
    private int _enrichEpoch;
    private int _mirrorPanelW;
    private int _mirrorPanelH;
    private int _reconnectAttempts;
    private CancellationTokenSource? _reconnectCts;
    private CancellationTokenSource? _orientWatchCts;
    private double _lastMirrorAspect;
    private double _userMirrorWidth = 320;
    private bool _adaptingMirrorWidth;
    private bool _restartingForOrient;
    private bool _aspectTrustedFromVideo;
    private CancellationTokenSource? _orientRestartDebounceCts;
    private string? _recordingPath;
    private bool _recordHandoff;

    public event EventHandler? MirrorAttachRequested;
    public event EventHandler<double>? MirrorAspectChanged;
    /// <summary>横竖屏切换导致栏宽变化后，通知窗口在布局完成时强制同步 HWND。</summary>
    public event EventHandler? MirrorLayoutChanged;

    public MainViewModel(
        IDeviceContext devices,
        IAppSettings settings,
        IMirrorSession mirror,
        IAdbProcessRunner adb,
        IDebugCommandService debug,
        DevicePageViewModel devicePage,
        AppsPageViewModel appsPage,
        FilesPageViewModel filesPage,
        DebugPageViewModel debugPage,
        ConsolePageViewModel consolePage,
        CastPageViewModel castPage,
        DeveloperPageViewModel developerPage,
        NmeaPageViewModel nmeaPage,
        AboutPageViewModel aboutPage)
    {
        _devices = devices;
        _settings = settings;
        _mirror = mirror;
        _adb = adb;
        _debug = debug;
        _devicePage = devicePage;
        _appsPage = appsPage;
        _filesPage = filesPage;
        _debugPage = debugPage;
        _consolePage = consolePage;
        _castPage = castPage;
        _developerPage = developerPage;
        _nmeaPage = nmeaPage;
        _aboutPage = aboutPage;

        NavCollapsed = settings.Current.NavCollapsed;
        _userMirrorWidth = settings.Current.MirrorWidth is >= 240 and <= 720
            ? settings.Current.MirrorWidth
            : 320;
        MirrorWidth = _userMirrorWidth;

        _devices.DevicesChanged += (_, _) => Application.Current.Dispatcher.Invoke(ReloadDevices);
        _devices.CurrentChanged += (_, _) => Application.Current.Dispatcher.Invoke(OnCurrentChanged);
        _mirror.StateChanged += (_, _) => Application.Current.Dispatcher.Invoke(OnMirrorStateChanged);
        _mirror.VideoSizeChanged += (_, size) => Application.Current.Dispatcher.Invoke(() => OnMirrorVideoSize(size));

        ReloadDevices();
        // AppPage.Device==0 时赋值不触发 OnCurrentPageChanged，须显式挂上首屏，避免启动空白
        CurrentPage = AppPage.Device;
        CurrentPageViewModel = _devicePage;
        _ = _devicePage.RefreshAsync();
    }

    public ObservableCollection<AndroidDevice> Devices { get; } = [];
    public ObservableCollection<DeviceListItem> DeviceMenuItems { get; } = [];

    /// <summary>用户偏好的投屏栏宽（竖屏/默认）；横屏临时加宽不改此值。</summary>
    public double PreferredMirrorWidth => _userMirrorWidth;

    [ObservableProperty] private AndroidDevice? _selectedDevice;
    [ObservableProperty] private AppPage _currentPage;
    [ObservableProperty] private bool _navCollapsed;
    [ObservableProperty] private double _mirrorWidth = 320;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private string _mirrorStatus = "投屏未启动";
    [ObservableProperty] private object? _currentPageViewModel;
    [ObservableProperty] private bool _isDeviceMenuOpen;
    [ObservableProperty] private string _chipPrimaryText = "未连接设备";
    [ObservableProperty] private string _chipBuildTag = "—";
    [ObservableProperty] private string _chipStatusText = "离线";
    [ObservableProperty] private bool _chipIsOnline;
    [ObservableProperty] private bool _isMirrorFullscreen;
    [ObservableProperty] private string _mirrorFullscreenLabel = "全屏";
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _recordActionLabel = "录屏开始";

    partial void OnIsRecordingChanged(bool value) => RecordActionLabel = value ? "录屏结束" : "录屏开始";

    partial void OnIsMirrorFullscreenChanged(bool value)
    {
        MirrorFullscreenLabel = value ? "退出全屏" : "全屏";
        MirrorLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleMirrorFullscreen() => IsMirrorFullscreen = !IsMirrorFullscreen;

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        if (_suppressSelect || value is null) return;
        if (!value.IsOnline)
        {
            var (title, message) = DeviceListItem.FormatUnavailablePrompt(value.DisplayName, value.State);
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            _suppressSelect = true;
            SelectedDevice = _devices.Current;
            _suppressSelect = false;
            return;
        }

        if (_devices.Current?.Serial != value.Serial)
        {
            _devices.Select(value.Serial);
        }
    }

    partial void OnCurrentPageChanged(AppPage value)
    {
        CurrentPageViewModel = value switch
        {
            AppPage.Device => _devicePage,
            AppPage.Apps => _appsPage,
            AppPage.Files => _filesPage,
            AppPage.Debug => _debugPage,
            AppPage.Console => _consolePage,
            AppPage.Cast => _castPage,
            AppPage.Developer => _developerPage,
            AppPage.Nmea => _nmeaPage,
            AppPage.About => _aboutPage,
            _ => _devicePage,
        };

        if (value == AppPage.Device)
        {
            _ = _devicePage.RefreshAsync();
        }
    }

    partial void OnNavCollapsedChanged(bool value) => _settings.Current.NavCollapsed = value;

    partial void OnMirrorWidthChanged(double value)
    {
        var clamped = Math.Clamp(value, 240, 720);
        if (Math.Abs(clamped - value) > 0.5)
        {
            MirrorWidth = clamped;
            return;
        }

        // 横竖屏自动加宽不写回用户偏好；仅用户拖拽分隔条时持久化
        if (!_adaptingMirrorWidth)
        {
            _userMirrorWidth = clamped;
            _settings.Current.MirrorWidth = (int)clamped;
        }
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        if (Enum.TryParse<AppPage>(page, ignoreCase: true, out var p))
        {
            CurrentPage = p;
        }
    }

    [RelayCommand]
    private void ToggleNav() => NavCollapsed = !NavCollapsed;

    [RelayCommand]
    private void ToggleDeviceMenu() => IsDeviceMenuOpen = !IsDeviceMenuOpen;

    [RelayCommand]
    private void CloseDeviceMenu() => IsDeviceMenuOpen = false;

    [RelayCommand]
    private void SelectDeviceFromMenu(DeviceListItem? item)
    {
        if (item is null) return;
        if (!item.IsOnline)
        {
            var (title, message) = DeviceListItem.FormatUnavailablePrompt(item.ModelName, item.State);
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _devices.Select(item.Serial);
        IsDeviceMenuOpen = false;
    }

    [RelayCommand]
    private async Task StartMirrorAsync()
    {
        if (IsRecording)
        {
            await FinishRecordingAsync(restartMirror: false);
        }

        _preferMirrorRunning = true;
        await StartMirrorCoreAsync(record: false);
    }

    [RelayCommand]
    private async Task StartMirrorRecordAsync()
    {
        if (IsRecording)
        {
            await FinishRecordingAsync(restartMirror: true);
            return;
        }

        _preferMirrorRunning = true;
        await StartMirrorCoreAsync(record: true);
    }

    private async Task StartMirrorCoreAsync(bool record)
    {
        if (_devices.Current is null || !_devices.Current.IsOnline)
        {
            MirrorStatus = "无在线设备";
            return;
        }

        var epoch = ++_mirrorEpoch;
        var serial = _devices.Current.Serial;
        _activeMirrorSerial = serial;
        StopOrientationWatch();
        try
        {
            string? recordPath = null;
            if (record)
            {
                recordPath = _castPage.CreateRecordPath(serial);
                MirrorStatus = "正在启动投屏并录制…";
            }
            else
            {
                MirrorStatus = "正在启动 scrcpy…";
            }

            // dumpsys 在旋转瞬间常滞后；若已有 Texture 确认的比例，禁止被旧 dumpsys 覆盖
            var probed = await QueryAspectRatioAsync(serial).ConfigureAwait(true);
            if (epoch != _mirrorEpoch) return;
            var aspect = ResolveStartAspect(probed);
            if (aspect > 0.05)
            {
                ApplyDeviceAspect(aspect, fromVideo: _aspectTrustedFromVideo);
            }

            // 初始窗口按视频比 letterbox，与嵌入后子窗口尺寸一致，避免 SDL 起步就拉伸
            var (embedW, embedH) = ComputeEmbedSize(aspect > 0.05 ? aspect : _lastMirrorAspect);
            var options = _castPage.ToOptions(recordPath, embedW, embedH);
            await _mirror.StartAsync(_devices.Current, options).ConfigureAwait(true);
            if (epoch != _mirrorEpoch) return;

            _reconnectAttempts = 0;
            if (record && !string.IsNullOrWhiteSpace(recordPath))
            {
                _recordingPath = recordPath;
                IsRecording = true;
            }

            MirrorStatus = _mirror.WindowHandle == IntPtr.Zero
                ? "已启动（等待窗口嵌入）"
                : "投屏运行中";
            MirrorAttachRequested?.Invoke(this, EventArgs.Empty);
            UpdateStatus();
            StartOrientationWatch(serial, epoch);
        }
        catch (Exception ex)
        {
            if (epoch != _mirrorEpoch) return;
            MirrorStatus = "启动失败：" + ex.Message;
            if (record)
            {
                IsRecording = false;
                _recordingPath = null;
            }

            _activeMirrorSerial = null;
            StopOrientationWatch();
            MirrorAttachRequested?.Invoke(this, EventArgs.Empty);
            ScheduleReconnect();
        }
    }

    /// <summary>
    /// 启动/重建时选用的比例：Texture 已确认则优先，避免第二轮竖屏被滞后 dumpsys 写回横屏比。
    /// </summary>
    private double ResolveStartAspect(double probed)
    {
        if (_aspectTrustedFromVideo && _lastMirrorAspect > 0.05)
        {
            if (probed > 0.05 && (probed >= 1.0) == (_lastMirrorAspect >= 1.0))
            {
                // 同向时可用 dumpsys 精修数值
                return probed;
            }

            return _lastMirrorAspect;
        }

        if (probed > 0.05)
        {
            return probed;
        }

        return _lastMirrorAspect;
    }

    private void OnMirrorVideoSize(MirrorVideoSize size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;

        var aspect = size.AspectRatio;
        var prev = _lastMirrorAspect;
        _aspectTrustedFromVideo = true;
        // 横↔竖翻转时 SDL 嵌入窗口易错乱，重建会话最稳
        var flipped = prev > 0.05 && (prev >= 1.0) != (aspect >= 1.0);
        ApplyDeviceAspect(aspect, fromVideo: true);
        if (flipped)
        {
            ScheduleOrientRestart();
        }
    }

    /// <summary>合并快速连续的横竖切换，去掉固定冷却（否则第二轮常被跳过）。</summary>
    private void ScheduleOrientRestart()
    {
        if (!_preferMirrorRunning) return;
        _orientRestartDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _orientRestartDebounceCts = cts;
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (token.IsCancellationRequested || !_preferMirrorRunning) return;
                await RestartMirrorForOrientationAsync();
            });
        }, token);
    }

    private async Task RestartMirrorForOrientationAsync()
    {
        if (_restartingForOrient || !_preferMirrorRunning) return;
        if (_devices.Current is null || !_devices.Current.IsOnline) return;

        _restartingForOrient = true;
        try
        {
            MirrorStatus = "方向已切换，正在重建投屏…";
            await StartMirrorCoreAsync(record: false);
        }
        finally
        {
            _restartingForOrient = false;
        }
    }

    /// <summary>
    /// 按设备画面比例调整右侧栏宽度，并通知 HWND letterbox。
    /// 横屏加宽栏位；竖屏恢复用户宽度。
    /// </summary>
    private void ApplyDeviceAspect(double aspect, bool fromVideo)
    {
        if (aspect <= 0.05)
        {
            return;
        }

        var changed = Math.Abs(aspect - _lastMirrorAspect) >= 0.03;
        _lastMirrorAspect = aspect;
        MirrorAspectChanged?.Invoke(this, aspect);

        var before = MirrorWidth;
        AdaptMirrorColumnWidth(aspect);
        if (changed || Math.Abs(MirrorWidth - before) > 0.5)
        {
            MirrorStatus = aspect >= 1.0
                ? (fromVideo ? "投屏运行中（横屏）" : "投屏运行中（横屏自适应）")
                : (fromVideo ? "投屏运行中（竖屏）" : "投屏运行中（竖屏自适应）");
            MirrorLayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AdaptMirrorColumnWidth(double aspect)
    {
        _adaptingMirrorWidth = true;
        try
        {
            if (aspect >= 1.0)
            {
                // 固定横屏栏宽，避免随舞台高度反馈导致窗宽来回变、投屏闪烁
                MirrorWidth = Math.Clamp(Math.Max(480, (int)_userMirrorWidth), 360, 640);
            }
            else
            {
                MirrorWidth = Math.Clamp(_userMirrorWidth, 240, 520);
            }
        }
        finally
        {
            _adaptingMirrorWidth = false;
        }
    }

    private void StartOrientationWatch(string serial, int epoch)
    {
        StopOrientationWatch();
        var cts = new CancellationTokenSource();
        _orientWatchCts = cts;
        var token = cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1500, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (epoch != _mirrorEpoch || !_preferMirrorRunning)
                {
                    return;
                }

                double aspect;
                var fromVideo = false;
                if (_mirror.VideoSize is { Width: > 0, Height: > 0 } vs)
                {
                    aspect = vs.AspectRatio;
                    fromVideo = true;
                }
                else
                {
                    // 已有 Texture 真源时，不再用可能滞后的 dumpsys 改比例
                    if (_aspectTrustedFromVideo)
                    {
                        continue;
                    }

                    try
                    {
                        aspect = await QueryAspectRatioAsync(serial).ConfigureAwait(false);
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (aspect <= 0.05 || Math.Abs(aspect - _lastMirrorAspect) < 0.03)
                {
                    continue;
                }

                var prev = _lastMirrorAspect;
                var flipped = prev > 0.05 && (prev >= 1.0) != (aspect >= 1.0);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (epoch != _mirrorEpoch || !_preferMirrorRunning)
                    {
                        return;
                    }

                    if (fromVideo)
                    {
                        _aspectTrustedFromVideo = true;
                    }

                    ApplyDeviceAspect(aspect, fromVideo: fromVideo);
                    if (flipped)
                    {
                        ScheduleOrientRestart();
                    }
                });
            }
        }, token);
    }

    private void StopOrientationWatch()
    {
        _orientWatchCts?.Cancel();
        _orientWatchCts = null;
        _orientRestartDebounceCts?.Cancel();
    }

    /// <summary>由 MainWindow 汇报右侧投屏舞台客户区尺寸（DIP）。</summary>
    public void ReportMirrorPanelSize(double width, double height)
    {
        _mirrorPanelW = Math.Max(0, (int)Math.Round(width));
        _mirrorPanelH = Math.Max(0, (int)Math.Round(height));
    }

    private (int W, int H) ComputeEmbedSize(double aspect)
    {
        var hostW = _mirrorPanelW > 0 ? _mirrorPanelW : Math.Max(200, (int)MirrorWidth - 16);
        var hostH = _mirrorPanelH > 0 ? _mirrorPanelH : 560;
        if (aspect <= 0.05)
        {
            return (hostW, hostH);
        }

        var hostAspect = (double)hostW / Math.Max(1, hostH);
        if (hostAspect > aspect)
        {
            var h = hostH;
            var w = Math.Max(1, (int)Math.Round(hostH * aspect));
            return (w, h);
        }

        var ww = hostW;
        var hh = Math.Max(1, (int)Math.Round(hostW / aspect));
        return (ww, hh);
    }

    private void OnMirrorStateChanged()
    {
        UpdateStatus();
        MirrorAttachRequested?.Invoke(this, EventArgs.Empty);
        if (_recordHandoff)
        {
            return;
        }

        if (_mirror.State == MirrorSessionState.Failed && _preferMirrorRunning)
        {
            MirrorStatus = "投屏中断，正在重连…";
            ScheduleReconnect();
        }
        else if (_mirror.State == MirrorSessionState.Running)
        {
            _reconnectAttempts = 0;
        }
    }

    private void ScheduleReconnect()
    {
        if (!_preferMirrorRunning) return;
        if (_reconnectAttempts >= 5) return;
        if (_mirror.State is MirrorSessionState.Starting) return;
        _reconnectCts?.Cancel();
        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;
        var delayMs = Math.Min(8000, 800 * (1 << _reconnectAttempts));
        _reconnectAttempts++;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, token).ConfigureAwait(false);
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (!_preferMirrorRunning || token.IsCancellationRequested) return;
                    if (_mirror.State is MirrorSessionState.Starting or MirrorSessionState.Running) return;
                    await EnsureMirrorForCurrentAsync();
                });
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }, token);
    }

    /// <summary>用户没有手动停止时，投屏应保持运行。</summary>
    public bool MirrorShouldRun => _preferMirrorRunning;

    /// <summary>先关掉当前会话，但不改变「应保持运行」。加载框显示期间用。</summary>
    public async Task PauseMirrorForReloadAsync()
    {
        if (!_preferMirrorRunning) return;

        _reconnectCts?.Cancel();
        StopOrientationWatch();
        _reconnectAttempts = 0;
        if (IsRecording)
        {
            await FinishRecordingAsync(restartMirror: false);
        }
        else if (_mirror.State is MirrorSessionState.Running or MirrorSessionState.Starting)
        {
            _mirrorEpoch++;
            await _mirror.StopAsync();
        }

        if (_preferMirrorRunning)
        {
            MirrorStatus = "正在加载投屏…";
            MirrorAttachRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>按现在的舞台尺寸再启动。调用前应已经停掉旧会话。</summary>
    public async Task RestartMirrorAsync()
    {
        if (!_preferMirrorRunning) return;
        if (_devices.Current is null || !_devices.Current.IsOnline) return;
        await StartMirrorCoreAsync(record: false);
    }

    [RelayCommand]
    private async Task StopMirrorAsync()
    {
        _preferMirrorRunning = false;
        _reconnectCts?.Cancel();
        StopOrientationWatch();
        _reconnectAttempts = 0;
        if (IsRecording)
        {
            await FinishRecordingAsync(restartMirror: false);
        }
        else
        {
            _mirrorEpoch++;
            _activeMirrorSerial = null;
            await _mirror.StopAsync();
        }

        MirrorStatus = "投屏已停止";
        MirrorAttachRequested?.Invoke(this, EventArgs.Empty);
        UpdateStatus();
    }

    /// <summary>结束录屏并等文件写完。需要继续投屏时再无录制地拉起。</summary>
    private async Task FinishRecordingAsync(bool restartMirror)
    {
        var path = _recordingPath;
        _recordHandoff = true;
        _reconnectCts?.Cancel();
        _mirrorEpoch++;
        try
        {
            await _mirror.StopAsync();
        }
        finally
        {
            IsRecording = false;
            _recordingPath = null;
            _recordHandoff = false;
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            SavedFileNotice.Show("录屏", path);
        }

        if (restartMirror && _preferMirrorRunning)
        {
            await StartMirrorCoreAsync(record: false);
        }
    }

    [RelayCommand]
    private async Task SendKeyAsync(string? keycode)
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || string.IsNullOrWhiteSpace(keycode)) return;
        await _adb.RunAsync(["shell", "input", "keyevent", keycode], serial, TimeSpan.FromSeconds(10));
    }

    [RelayCommand]
    private async Task ScreenshotAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TestEngineer", "Exports");
        System.IO.Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, $"screenshot-{serial}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        var result = await _debug.CaptureScreenshotAsync(serial, path);
        if (result.Succeeded)
        {
            MirrorStatus = "投屏运行中";
            SavedFileNotice.Show("截屏", path);
        }
        else
        {
            MirrorStatus = "截屏失败：" + result.FailureMessage;
            SavedFileNotice.Show("截屏", path, failed: true, detail: result.FailureMessage);
        }
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        await _devices.RefreshAsync();
        IsDeviceMenuOpen = true;
    }

    /// <summary>窗口即将关闭：取消重连，避免 scrcpy 退出瞬间又被拉起来。</summary>
    public void StopMirrorForExit()
    {
        _preferMirrorRunning = false;
        _reconnectCts?.Cancel();
        StopOrientationWatch();
        _orientRestartDebounceCts?.Cancel();
        _mirrorEpoch++;
    }

    /// <summary>窗口 Loaded 后调用：有在线设备则默认开投屏。</summary>
    public Task InitializeMirrorAsync() => EnsureMirrorForCurrentAsync();

    private async Task EnsureMirrorForCurrentAsync()
    {
        var cur = _devices.Current;
        if (!_preferMirrorRunning)
        {
            return;
        }

        if (cur is null || !cur.IsOnline)
        {
            if (_mirror.State is MirrorSessionState.Running or MirrorSessionState.Starting)
            {
                _mirrorEpoch++;
                _activeMirrorSerial = null;
                StopOrientationWatch();
                await _mirror.StopAsync();
                MirrorStatus = "无在线设备";
                MirrorAttachRequested?.Invoke(this, EventArgs.Empty);
                UpdateStatus();
            }

            return;
        }

        if (string.Equals(_activeMirrorSerial, cur.Serial, StringComparison.Ordinal)
            && _mirror.State is MirrorSessionState.Running or MirrorSessionState.Starting)
        {
            return;
        }

        await StartMirrorCoreAsync(record: false);
    }

    private async Task<double> QueryAspectRatioAsync(string serial)
    {
        try
        {
            var sizeTask = _adb.RunAsync(["shell", "wm", "size"], serial, TimeSpan.FromSeconds(5));
            var rotTask = _adb.RunAsync(["shell", "dumpsys", "display"], serial, TimeSpan.FromSeconds(5));
            await Task.WhenAll(sizeTask, rotTask).ConfigureAwait(false);

            var sizeResult = await sizeTask.ConfigureAwait(false);
            var rotResult = await rotTask.ConfigureAwait(false);
            var size = WmParser.ParseSize(sizeResult.StandardOutput);
            if (size is not { Width: > 0, Height: > 0 })
            {
                return 0;
            }

            var rotation = WmParser.ParseRotation(rotResult.StandardOutput);
            if (rotation is null)
            {
                // 部分机型 rotation 只在 window dumpsys 里
                var win = await _adb.RunAsync(["shell", "dumpsys", "window", "displays"], serial, TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                rotation = WmParser.ParseRotation(win.StandardOutput);
            }

            var oriented = WmParser.ApplyRotation(size.Value, rotation) ?? size.Value;
            return (double)oriented.Width / oriented.Height;
        }
        catch
        {
            // ignore
        }

        return 0;
    }

    private void ReloadDevices()
    {
        Devices.Clear();
        foreach (var d in _devices.Devices)
        {
            Devices.Add(d);
        }

        _suppressSelect = true;
        SelectedDevice = _devices.Current;
        _suppressSelect = false;
        RebuildDeviceMenu();
        UpdateChip();
        UpdateStatus();
        _ = EnrichDeviceMenuAsync();
    }

    private void OnCurrentChanged()
    {
        _suppressSelect = true;
        SelectedDevice = _devices.Current;
        _suppressSelect = false;
        RebuildDeviceMenu();
        UpdateChip();
        UpdateStatus();
        // 换机后清空方向缓存，避免沿用上一台的 Texture 比例
        _aspectTrustedFromVideo = false;
        _lastMirrorAspect = 0;
        _orientRestartDebounceCts?.Cancel();
        if (CurrentPage == AppPage.Device)
        {
            _ = _devicePage.RefreshAsync();
        }

        // 切换设备时自动跟到新机（用户手动停止后不自动再起）
        _ = EnsureMirrorForCurrentAsync();
    }

    private void RebuildDeviceMenu(IReadOnlyDictionary<string, (string Android, string Build)>? enrich = null)
    {
        DeviceMenuItems.Clear();
        var currentSerial = _devices.Current?.Serial;
        foreach (var d in _devices.Devices)
        {
            string? android = null;
            string? build = null;
            if (enrich is not null && enrich.TryGetValue(d.Serial, out var meta))
            {
                android = meta.Android;
                build = meta.Build;
            }

            DeviceMenuItems.Add(new DeviceListItem(
                d,
                string.Equals(d.Serial, currentSerial, StringComparison.Ordinal),
                android,
                build));
        }
    }

    private async Task EnrichDeviceMenuAsync()
    {
        var epoch = ++_enrichEpoch;
        var list = _devices.Devices.ToList();
        var map = new Dictionary<string, (string Android, string Build)>(StringComparer.Ordinal);
        foreach (var d in list)
        {
            if (epoch != _enrichEpoch) return;
            if (!d.IsOnline) continue;
            try
            {
                var r = await _adb.RunAsync(
                    ["shell", "getprop", "ro.build.version.release"],
                    d.Serial,
                    TimeSpan.FromSeconds(4));
                var android = (r.StandardOutput ?? "").Trim();
                var r2 = await _adb.RunAsync(
                    ["shell", "getprop", "ro.build.type"],
                    d.Serial,
                    TimeSpan.FromSeconds(4));
                var build = (r2.StandardOutput ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(android) || !string.IsNullOrWhiteSpace(build))
                {
                    map[d.Serial] = (android, build);
                }
            }
            catch
            {
                // ignore per-device
            }
        }

        if (epoch != _enrichEpoch) return;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            RebuildDeviceMenu(map);
            UpdateChip(map);
        });
    }

    private void UpdateChip(IReadOnlyDictionary<string, (string Android, string Build)>? enrich = null)
    {
        var cur = _devices.Current;
        if (cur is null)
        {
            ChipPrimaryText = "未连接设备";
            ChipBuildTag = "—";
            ChipStatusText = "离线";
            ChipIsOnline = false;
            return;
        }

        var model = !string.IsNullOrWhiteSpace(cur.Model)
            ? cur.Model.Replace('_', '-')
            : (cur.Product ?? cur.Serial);
        var channel = DeviceListItem.ResolveChannel(cur);
        var android = "?";
        var build = "—";
        if (enrich is not null && enrich.TryGetValue(cur.Serial, out var meta))
        {
            if (!string.IsNullOrWhiteSpace(meta.Android)) android = meta.Android;
            if (!string.IsNullOrWhiteSpace(meta.Build)) build = meta.Build;
        }
        else
        {
            var existing = DeviceMenuItems.FirstOrDefault(x => x.Serial == cur.Serial);
            if (existing is not null)
            {
                android = existing.AndroidVersion;
                build = existing.BuildType;
            }
        }

        ChipPrimaryText = $"{model} · Android {android} · {channel}";
        ChipBuildTag = build;
        ChipIsOnline = cur.IsOnline;
        ChipStatusText = DeviceListItem.FormatStatusLabel(cur.State);
    }

    private void UpdateStatus()
    {
        var online = Devices.Count(d => d.IsOnline);
        var cur = _devices.Current?.DisplayName ?? "未选择设备";
        StatusText = $"在线 {online}/{Devices.Count} · 当前 {cur} · 投屏 {_mirror.State}";
        if (_mirror.State == MirrorSessionState.Running && string.IsNullOrWhiteSpace(MirrorStatus))
        {
            MirrorStatus = "投屏运行中";
        }
    }

}
