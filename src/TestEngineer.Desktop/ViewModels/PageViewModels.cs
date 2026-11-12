using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;
using TestEngineer.Core.Settings;
using TestEngineer.Desktop.Services;
using TestEngineer.Mirror;

namespace TestEngineer.Desktop.ViewModels;

public sealed partial class DevicePageViewModel : ObservableObject
{
    private readonly IDeviceContext _devices;
    private readonly IDeviceInfoService _info;

    public DevicePageViewModel(IDeviceContext devices, IDeviceInfoService info)
    {
        _devices = devices;
        _info = info;
    }

    [ObservableProperty] private DeviceSnapshot? _snapshot;
    [ObservableProperty] private string _uptimeText = "—";
    [ObservableProperty] private string _status = "等待设备";
    [ObservableProperty] private bool _isBusy;

    private CancellationTokenSource? _uptimeCts;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _devices.Current?.IsOnline != true)
        {
            Snapshot = null;
            Status = "设备未连接";
            UptimeText = "—";
            return;
        }

        IsBusy = true;
        Status = "采集中…";
        try
        {
            Snapshot = await _info.GetSnapshotAsync(serial);
            Status = Snapshot.Warnings.Count == 0
                ? "已刷新"
                : $"已刷新（{Snapshot.Warnings.Count} 项警告）";
            StartUptimeTicker(Snapshot.UptimeSec);
        }
        catch (Exception ex)
        {
            Status = "采集失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartUptimeTicker(long baseSec)
    {
        _uptimeCts?.Cancel();
        _uptimeCts = new CancellationTokenSource();
        var token = _uptimeCts.Token;
        var started = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var elapsed = (long)(DateTime.UtcNow - started).TotalSeconds;
                var text = FormatUptime(baseSec + elapsed);
                Application.Current?.Dispatcher.Invoke(() => UptimeText = text);
                try { await Task.Delay(1000, token); }
                catch (OperationCanceledException) { break; }
            }
        }, token);
    }

    private static string FormatUptime(long sec)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, sec));
        return t.TotalDays >= 1
            ? $"{(int)t.TotalDays}天 {t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }
}

public sealed partial class CastPageViewModel : ObservableObject
{
    [ObservableProperty] private string _hint = "右侧常驻画面 · 本页仅参数；右侧工具栏也可启停。";
    /// <summary>0 = 不限制（按设备分辨率）；大于 0 时传给 scrcpy --max-size。</summary>
    [ObservableProperty] private int _maxSize;
    [ObservableProperty] private int _bitRate = 8_000_000;
    [ObservableProperty] private int _maxFps = 30;
    [ObservableProperty] private bool _stayAwake = true;
    [ObservableProperty] private bool _turnScreenOff;
    [ObservableProperty] private string _recordDirectory =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TestEngineer", "Recordings");

    /// <summary>界面用 Mbps；内部仍为 BitRate（bps）。</summary>
    public double BitRateMbps
    {
        get => BitRate / 1_000_000d;
        set
        {
            var bps = (int)Math.Round(Math.Clamp(value, 0.5, 64) * 1_000_000);
            if (BitRate == bps)
            {
                return;
            }

            BitRate = bps;
            OnPropertyChanged();
        }
    }

    partial void OnBitRateChanged(int value) => OnPropertyChanged(nameof(BitRateMbps));

    [RelayCommand]
    private void PickRecordDirectory()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择录屏输出目录" };
        if (!string.IsNullOrWhiteSpace(RecordDirectory) && System.IO.Directory.Exists(RecordDirectory))
        {
            dlg.InitialDirectory = RecordDirectory;
        }

        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            RecordDirectory = dlg.FolderName;
        }
    }

    public string CreateRecordPath(string? serial = null)
    {
        System.IO.Directory.CreateDirectory(RecordDirectory);
        var id = string.IsNullOrWhiteSpace(serial) ? "device" : serial;
        return System.IO.Path.Combine(RecordDirectory, $"mirror-{id}-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");
    }

    public MirrorOptions ToOptions(string? recordPath = null, int windowWidth = 0, int windowHeight = 0) => new()
    {
        MaxSize = MaxSize is > 0 and <= 8192 ? MaxSize : 0,
        BitRate = BitRate is > 0 ? BitRate : 8_000_000,
        MaxFps = MaxFps is > 0 and <= 120 ? MaxFps : 30,
        StayAwake = StayAwake,
        TurnScreenOff = TurnScreenOff,
        Borderless = true,
        NoAudio = true,
        // 过低缓冲易花屏；过高会拖慢滑动手势手感
        VideoBufferMs = 30,
        WindowWidth = windowWidth,
        WindowHeight = windowHeight,
        RecordPath = recordPath,
    };
}

public sealed partial class AboutPageViewModel : ObservableObject
{
    private readonly IAppSettings _settings;
    private readonly IThemeService _theme;

    public AboutPageViewModel(IAppSettings settings, IThemeService theme)
    {
        _settings = settings;
        _theme = theme;
        SelectedTheme = settings.Current.Theme;
        var version = typeof(AboutPageViewModel).Assembly.GetName().Version;
        VersionText = version is null ? "0.1.7" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }

    public ObservableCollection<AppThemeMode> Themes { get; } =
    [
        AppThemeMode.System,
        AppThemeMode.Light,
        AppThemeMode.Dark,
    ];

    [ObservableProperty] private AppThemeMode _selectedTheme;
    [ObservableProperty] private string _versionText;

    partial void OnSelectedThemeChanged(AppThemeMode value)
    {
        _settings.Current.Theme = value;
        var resolved = value == AppThemeMode.System
            ? (_theme.Current == AppThemeMode.Light ? AppThemeMode.Light : AppThemeMode.Dark)
            : value;
        // Re-resolve system via applying light/dark preference simply:
        if (value == AppThemeMode.System)
        {
            try
            {
                var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var v = key?.GetValue("AppsUseLightTheme");
                resolved = v is int i && i == 0 ? AppThemeMode.Dark : AppThemeMode.Light;
            }
            catch { resolved = AppThemeMode.Dark; }
        }

        _theme.Apply(resolved);
        _settings.Save();
    }
}
