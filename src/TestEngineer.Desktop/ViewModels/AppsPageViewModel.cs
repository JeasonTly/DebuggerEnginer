using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Desktop.ViewModels;

public sealed partial class AppsPageViewModel : ObservableObject
{
    private readonly IDeviceContext _devices;
    private readonly IAppManagerService _apps;

    public AppsPageViewModel(IDeviceContext devices, IAppManagerService apps)
    {
        _devices = devices;
        _apps = apps;
        _devices.CurrentChanged += (_, _) => Application.Current.Dispatcher.Invoke(() => _ = RefreshAsync());
    }

    public ObservableCollection<AppPackage> Packages { get; } = [];

    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _segment = "ThirdParty";
    [ObservableProperty] private string _status = "等待加载";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private AppPackage? _selectedPackage;
    [ObservableProperty] private string _detailText = "";

    public IEnumerable<AppPackage> FilteredPackages =>
        string.IsNullOrWhiteSpace(FilterText)
            ? Packages
            : Packages.Where(p =>
                p.PackageName.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
                || p.DisplayName.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

    partial void OnFilterTextChanged(string value) => OnPropertyChanged(nameof(FilteredPackages));

    partial void OnSegmentChanged(string value) => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _devices.Current?.IsOnline != true)
        {
            Packages.Clear();
            Status = "设备未连接";
            OnPropertyChanged(nameof(FilteredPackages));
            return;
        }

        IsBusy = true;
        Status = "加载应用列表…";
        try
        {
            var filter = Segment switch
            {
                "System" => AppListFilter.System,
                "All" => AppListFilter.All,
                "HasIcon" => AppListFilter.HasIcon,
                _ => AppListFilter.ThirdParty,
            };
            var list = await _apps.ListAsync(serial, filter);
            Packages.Clear();
            // Launcher 入口置顶，再按显示名。
            foreach (var p in list
                         .OrderByDescending(x => x.HasLauncher)
                         .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                Packages.Add(p);
            }

            Status = $"{Packages.Count} 个应用";
            OnPropertyChanged(nameof(FilteredPackages));
        }
        catch (Exception ex)
        {
            Status = "加载失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
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
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        Status = "安装中…";
        try
        {
            var result = await _apps.InstallAsync(serial, dlg.FileName, replace: true);
            Status = result.Succeeded ? "安装成功" : ("安装失败：" + result.FailureMessage);
            if (result.Succeeded) await RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = "安装异常：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UninstallAsync(AppPackage? pkg)
    {
        pkg ??= SelectedPackage;
        if (pkg is null || pkg.IsSystem) return;
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;

        if (MessageBox.Show($"确认卸载 {pkg.PackageName}？", "卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await _apps.UninstallAsync(serial, pkg.PackageName);
        Status = result.Succeeded ? "卸载成功" : ("卸载失败：" + result.FailureMessage);
        if (result.Succeeded) await RefreshAsync();
    }

    [RelayCommand]
    private async Task ClearDataAsync(AppPackage? pkg)
    {
        pkg ??= SelectedPackage;
        if (pkg is null) return;
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        if (MessageBox.Show($"确认清除 {pkg.PackageName} 的用户数据？", "清数据", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
        Status = result.Succeeded ? "已启动" : ("启动失败：" + result.FailureMessage);
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
            var lines = new List<string>();
            foreach (var perm in detail.Permissions.Where(p => p.IsRuntime))
            {
                var r = await _apps.GrantPermissionAsync(serial, pkg.PackageName, perm.Name);
                lines.Add($"{(r.Succeeded ? "OK" : "FAIL")}  {perm.Name}  {r.FailureMessage}".Trim());
            }

            DetailText = lines.Count == 0 ? "无可授予的运行时权限" : string.Join(Environment.NewLine, lines);
            Status = "一键授权完成";
        }
        catch (Exception ex)
        {
            Status = "授权失败：" + ex.Message;
        }
    }
}
