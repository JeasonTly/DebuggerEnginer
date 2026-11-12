using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TestEngineer.Adb;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;
using TestEngineer.Desktop.Views;

namespace TestEngineer.Desktop.ViewModels;

public sealed partial class AppsPageViewModel : ObservableObject
{
    private readonly IDeviceContext _devices;
    private readonly IAppManagerService _apps;
    private readonly List<AppPackage> _cache = [];
    private string? _cacheSerial;

    public AppsPageViewModel(IDeviceContext devices, IAppManagerService apps)
    {
        _devices = devices;
        _apps = apps;
        _devices.CurrentChanged += (_, _) => Application.Current.Dispatcher.Invoke(() =>
        {
            _cache.Clear();
            _cacheSerial = null;
            _ = RefreshAsync();
        });
    }

    public ObservableCollection<AppPackage> Packages { get; } = [];

    [ObservableProperty] private string _filterText = "";
    /// <summary>默认「有图标」：首屏与刷新后都按此分段展示缓存结果。</summary>
    [ObservableProperty] private string _segment = "HasIcon";
    [ObservableProperty] private string _status = "等待加载";
    [ObservableProperty] private string _loadingMessage = "正在加载应用列表…";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyDetail = "首次拉取全量后，筛选将使用本地缓存";
    [ObservableProperty] private AppPackage? _selectedPackage;
    [ObservableProperty] private bool _isEmpty;

    private const string DefaultBusyDetail = "首次拉取全量后，筛选将使用本地缓存";

    partial void OnFilterTextChanged(string value) => ApplyCacheFilter();

    partial void OnSegmentChanged(string value)
    {
        // 有缓存则只切本地筛选，避免反复打 adb 卡顿
        if (_cache.Count > 0 && string.Equals(_cacheSerial, _devices.Current?.Serial, StringComparison.Ordinal))
        {
            ApplyCacheFilter();
            return;
        }

        _ = RefreshAsync();
    }

    private AppListFilter CurrentFilter => Segment switch
    {
        "System" => AppListFilter.System,
        "All" => AppListFilter.All,
        "HasIcon" => AppListFilter.HasIcon,
        _ => AppListFilter.ThirdParty,
    };

    private void ApplyCacheFilter()
    {
        var filtered = AppManagerService.ApplyClientFilter(_cache, CurrentFilter);
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            filtered = filtered
                .Where(p =>
                    p.PackageName.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
                    || p.DisplayName.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        filtered = filtered
            .OrderByDescending(x => x.HasLauncher)
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Packages.Clear();
        foreach (var p in filtered)
        {
            Packages.Add(p);
        }

        IsEmpty = !IsBusy && Packages.Count == 0;
        var segLabel = Segment switch
        {
            "HasIcon" => "有图标",
            "ThirdParty" => "第三方",
            "System" => "系统",
            _ => "全部",
        };
        Status = _cache.Count == 0
            ? "等待加载"
            : $"缓存 {_cache.Count} · {segLabel} {Packages.Count}";
    }

    /// <summary>应用管理的全量列表。有图标的在前，供 Monkey 等选择。已缓存则不再打设备。</summary>
    public async Task<IReadOnlyList<AppPackage>> GetAllPackagesAsync(CancellationToken cancellationToken = default)
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _devices.Current?.IsOnline != true)
        {
            return [];
        }

        if (_cache.Count == 0 || !string.Equals(_cacheSerial, serial, StringComparison.Ordinal))
        {
            var list = await _apps.ListAsync(serial, AppListFilter.All, cancellationToken);
            _cache.Clear();
            _cache.AddRange(list);
            _cacheSerial = serial;
            ApplyCacheFilter();
        }

        return _cache
            .OrderByDescending(p => p.HasIcon)
            .ThenByDescending(p => p.HasLauncher)
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _devices.Current?.IsOnline != true)
        {
            _cache.Clear();
            _cacheSerial = null;
            Packages.Clear();
            Status = "设备未连接";
            IsEmpty = true;
            return;
        }

        IsBusy = true;
        IsEmpty = false;
        BusyDetail = DefaultBusyDetail;
        LoadingMessage = "正在读取设备应用…";
        Status = "加载中…";
        try
        {
            // 一次拉全量（含图标富化），之后分段/搜索只走缓存
            LoadingMessage = "正在同步包名与系统标记…";
            var list = await _apps.ListAsync(serial, AppListFilter.All);
            LoadingMessage = "正在整理图标与列表…";
            _cache.Clear();
            _cache.AddRange(list);
            _cacheSerial = serial;
            // 保证默认展示「有图标」分段
            if (!string.Equals(Segment, "HasIcon", StringComparison.Ordinal))
            {
                Segment = "HasIcon";
            }
            else
            {
                ApplyCacheFilter();
            }

            Status = $"已缓存 {_cache.Count} 个应用 · 默认有图标 {Packages.Count}";
        }
        catch (Exception ex)
        {
            Status = "加载失败：" + ex.Message;
            IsEmpty = Packages.Count == 0;
        }
        finally
        {
            IsBusy = false;
            IsEmpty = Packages.Count == 0;
        }
    }

    [RelayCommand]
    private async Task InstallApkAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;

        var dlg = new OpenFileDialog
        {
            Filter = "Android Package|*.apk",
            Title = "选择要安装的 APK",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;
        await InstallLocalApksAsync(dlg.FileNames);
    }

    public async Task InstallLocalApksAsync(IReadOnlyList<string> paths)
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        var apks = paths.Where(p => p.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (apks.Length == 0)
        {
            Status = "未找到 APK 文件";
            return;
        }

        IsBusy = true;
        LoadingMessage = apks.Length == 1 ? "正在安装 APK…" : $"正在安装 {apks.Length} 个 APK…";
        try
        {
            var result = await _apps.InstallAsync(serial, apks[0], replace: true);
            for (var i = 1; i < apks.Length && result.Succeeded; i++)
            {
                LoadingMessage = $"正在安装 ({i + 1}/{apks.Length})…";
                result = await _apps.InstallAsync(serial, apks[i], replace: true);
            }

            Status = result.Succeeded ? "安装成功" : ("安装失败：" + result.FailureMessage);
            if (result.Succeeded)
            {
                _cache.Clear();
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            Status = "安装异常：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            BusyDetail = DefaultBusyDetail;
        }
    }

    [RelayCommand]
    private async Task UninstallAsync(AppPackage? pkg)
    {
        pkg ??= SelectedPackage;
        if (pkg is null || pkg.IsSystem) return;
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;

        if (MessageBox.Show(
                $"确认卸载「{pkg.DisplayName}」？\n{pkg.PackageName}",
                "卸载",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await _apps.UninstallAsync(serial, pkg.PackageName);
        Status = result.Succeeded ? "卸载成功" : ("卸载失败：" + result.FailureMessage);
        if (result.Succeeded)
        {
            _cache.Clear();
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task ForceStopAsync(AppPackage? pkg)
    {
        pkg ??= SelectedPackage;
        if (pkg is null) return;
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        if (MessageBox.Show(
                $"确认强制停止「{pkg.DisplayName}」？\n{pkg.PackageName}",
                "强制停止",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await _apps.ForceStopAsync(serial, pkg.PackageName);
        Status = result.Succeeded ? $"已强制停止 {pkg.DisplayName}" : ("强制停止失败：" + result.FailureMessage);
    }

    [RelayCommand]
    private async Task ClearDataAsync(AppPackage? pkg)
    {
        pkg ??= SelectedPackage;
        if (pkg is null) return;
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        if (MessageBox.Show(
                $"确认清除「{pkg.DisplayName}」的用户数据？\n{pkg.PackageName}",
                "清数据",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await _apps.ClearDataAsync(serial, pkg.PackageName);
        Status = result.Succeeded ? "已清数据" : ("清数据失败：" + result.FailureMessage);
    }

    [RelayCommand]
    private async Task LaunchAsync(AppPackage? pkg)
    {
        pkg ??= SelectedPackage;
        if (pkg is null) return;
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        var result = await _apps.LaunchAsync(serial, pkg.PackageName);
        Status = result.Succeeded ? $"已启动 {pkg.DisplayName}" : ("启动失败：" + result.FailureMessage);
    }

    [RelayCommand]
    private async Task GrantAllAsync(AppPackage? pkg)
    {
        pkg ??= SelectedPackage;
        if (pkg is null || pkg.IsSystem) return;
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;

        Status = "读取权限…";
        try
        {
            var detail = await _apps.GetDetailAsync(serial, pkg.PackageName);
            var runtime = detail.Permissions.Where(p => p.IsRuntime).ToList();
            if (runtime.Count == 0)
            {
                Status = "无可授予的运行时权限";
                ShowResult($"一键授权 · {pkg.DisplayName}", "无可授予的运行时权限（APK 未声明运行时权限）。");
                return;
            }

            var confirm = MessageBox.Show(
                $"设备 {serial}\n将为「{pkg.DisplayName}」授予 {runtime.Count} 项运行时权限。\n\n" +
                string.Join("\n", runtime.Select(p => "· " + p.Name)),
                $"一键授权 · {pkg.DisplayName}",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) return;

            var lines = new List<string> { $"$ adb -s {serial} shell pm grant …" };
            foreach (var perm in runtime)
            {
                var r = await _apps.GrantPermissionAsync(serial, pkg.PackageName, perm.Name);
                lines.Add(r.Succeeded
                    ? $"{perm.Name} → Success"
                    : $"{perm.Name} → 失败：{r.FailureMessage}");
            }

            Status = "一键授权完成";
            ShowResult($"授权结果 · {pkg.DisplayName}", string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            Status = "授权失败：" + ex.Message;
            ShowResult($"授权结果 · {pkg.DisplayName}", ex.ToString());
        }
    }

    private static void ShowResult(string title, string output)
    {
        var win = new CommandResultWindow(title, output)
        {
            Owner = Application.Current.MainWindow,
        };
        win.ShowDialog();
    }
}
