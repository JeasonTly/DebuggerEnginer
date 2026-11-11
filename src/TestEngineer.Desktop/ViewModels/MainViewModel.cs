using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    About,
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
    private readonly AboutPageViewModel _aboutPage;
    private bool _suppressSelect;

    public event EventHandler? MirrorAttachRequested;

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
        _aboutPage = aboutPage;

        NavCollapsed = settings.Current.NavCollapsed;
        MirrorWidth = settings.Current.MirrorWidth is >= 240 and <= 520
            ? settings.Current.MirrorWidth
            : 320;

        _devices.DevicesChanged += (_, _) => Application.Current.Dispatcher.Invoke(ReloadDevices);
        _devices.CurrentChanged += (_, _) => Application.Current.Dispatcher.Invoke(OnCurrentChanged);
        _mirror.StateChanged += (_, _) => Application.Current.Dispatcher.Invoke(UpdateStatus);

        ReloadDevices();
        CurrentPage = AppPage.Device;
    }

    public ObservableCollection<AndroidDevice> Devices { get; } = [];

    [ObservableProperty] private AndroidDevice? _selectedDevice;
    [ObservableProperty] private AppPage _currentPage;
    [ObservableProperty] private bool _navCollapsed;
    [ObservableProperty] private double _mirrorWidth = 320;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private string _mirrorStatus = "投屏未启动";
    [ObservableProperty] private object? _currentPageViewModel;

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        if (_suppressSelect || value is null) return;
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
        var clamped = Math.Clamp(value, 240, 520);
        if (Math.Abs(clamped - value) > 0.5)
        {
            MirrorWidth = clamped;
            return;
        }

        _settings.Current.MirrorWidth = (int)clamped;
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
    private async Task StartMirrorAsync() => await StartMirrorCoreAsync(record: false);

    [RelayCommand]
    private async Task StartMirrorRecordAsync() => await StartMirrorCoreAsync(record: true);

    private async Task StartMirrorCoreAsync(bool record)
    {
        if (_devices.Current is null || !_devices.Current.IsOnline)
        {
            MirrorStatus = "无在线设备";
            return;
        }

        try
        {
            string? recordPath = null;
            if (record)
            {
                recordPath = _castPage.CreateRecordPath(_devices.Current.Serial);
                MirrorStatus = "正在启动投屏并录制…";
            }
            else
            {
                MirrorStatus = "正在启动 scrcpy…";
            }

            var options = _castPage.ToOptions(recordPath);
            await _mirror.StartAsync(_devices.Current, options);
            var suffix = recordPath is not null ? "（录制：" + recordPath + "）" : "";
            MirrorStatus = _mirror.WindowHandle == IntPtr.Zero
                ? "已启动（等待窗口嵌入）" + suffix
                : "投屏运行中" + suffix;
            MirrorAttachRequested?.Invoke(this, EventArgs.Empty);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MirrorStatus = "启动失败：" + ex.Message;
            MirrorAttachRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task StopMirrorAsync()
    {
        await _mirror.StopAsync();
        MirrorStatus = "投屏已停止";
        MirrorAttachRequested?.Invoke(this, EventArgs.Empty);
        UpdateStatus();
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
        MirrorStatus = result.Succeeded ? ("截屏已保存：" + path) : ("截屏失败：" + result.FailureMessage);
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync() => await _devices.RefreshAsync();

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
        UpdateStatus();
    }

    private void OnCurrentChanged()
    {
        _suppressSelect = true;
        SelectedDevice = _devices.Current;
        _suppressSelect = false;
        UpdateStatus();
        if (CurrentPage == AppPage.Device)
        {
            _ = _devicePage.RefreshAsync();
        }
    }

    private void UpdateStatus()
    {
        var online = Devices.Count(d => d.IsOnline);
        var cur = _devices.Current?.DisplayName ?? "未选择设备";
        StatusText = $"在线 {online}/{Devices.Count} · 当前 {cur} · 投屏 {_mirror.State}";
    }
}
