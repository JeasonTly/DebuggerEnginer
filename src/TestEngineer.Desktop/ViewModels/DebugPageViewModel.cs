using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Desktop.ViewModels;

public sealed partial class DebugPageViewModel : ObservableObject
{
    private readonly IDeviceContext _devices;
    private readonly IDebugCommandService _debug;

    public DebugPageViewModel(IDeviceContext devices, IDebugCommandService debug)
    {
        _devices = devices;
        _debug = debug;
        foreach (var c in _debug.Commands)
        {
            Commands.Add(c);
        }

        Categories = [.. Commands.Select(c => c.Category).Distinct()];
        SelectedCategory = Categories.FirstOrDefault() ?? "";
    }

    public ObservableCollection<DebugCommandDefinition> Commands { get; } = [];
    public List<string> Categories { get; }

    [ObservableProperty] private string _selectedCategory = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "选择快捷指令";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _monkeyPackage = "";
    [ObservableProperty] private int _monkeyMinutes = 1;

    public IEnumerable<DebugCommandDefinition> FilteredCommands =>
        Commands.Where(c => string.IsNullOrEmpty(SelectedCategory) || c.Category == SelectedCategory);

    partial void OnSelectedCategoryChanged(string value) => OnPropertyChanged(nameof(FilteredCommands));

    [RelayCommand]
    private async Task RunAsync(DebugCommandDefinition? cmd)
    {
        if (cmd is null) return;
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _devices.Current?.IsOnline != true)
        {
            Status = "设备未连接";
            return;
        }

        if (cmd.IsDangerous)
        {
            if (MessageBox.Show($"确认执行危险指令：{cmd.Title}？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        IsBusy = true;
        Status = $"执行 {cmd.Title}…";
        var buf = new StringBuilder();
        try
        {
            var result = await _debug.RunAsync(serial, cmd.Id, null, line =>
            {
                buf.AppendLine(line);
                Application.Current.Dispatcher.Invoke(() => Output = buf.ToString());
            });
            if (buf.Length == 0) Output = result.CombinedOutput;
            Status = result.Succeeded ? "完成" : ("失败：" + result.FailureMessage);
        }
        catch (Exception ex)
        {
            Status = "异常：" + ex.Message;
            Output = ex.ToString();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunMonkeyAppAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        if (string.IsNullOrWhiteSpace(MonkeyPackage))
        {
            Status = "请填写包名";
            return;
        }

        if (MessageBox.Show($"对 {MonkeyPackage} 跑 Monkey {MonkeyMinutes} 分钟？", "Monkey", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        Status = "Monkey 运行中…";
        try
        {
            var result = await _debug.RunMonkeyAsync(serial, new MonkeyOptions(MonkeyPackage, Math.Max(1, MonkeyMinutes) * 60));
            Output = result.Command.CombinedOutput;
            Status = $"Monkey 结束 · crash={result.CrashCount} anr={result.AnrCount} byDuration={result.StoppedByDuration}";
        }
        catch (Exception ex)
        {
            Status = "Monkey 失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportBugreportAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Zip|*.zip",
            InitialDirectory = DefaultExportDirectory(),
            FileName = $"bugreport-{serial}-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
        };
        if (dlg.ShowDialog() != true) return;
        IsBusy = true;
        Status = "导出 bugreport（可能数分钟）…";
        try
        {
            var result = await _debug.CaptureBugreportAsync(serial, dlg.FileName);
            Status = result.Succeeded ? ("已导出：" + dlg.FileName) : ("失败：" + result.FailureMessage);
            Output = result.CombinedOutput;
        }
        catch (Exception ex)
        {
            Status = "bugreport 异常：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportLogcatAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _devices.Current?.IsOnline != true)
        {
            Status = "设备未连接";
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出日志 (logcat)",
            Filter = "Zip|*.zip",
            InitialDirectory = DefaultExportDirectory(),
            FileName = $"logcat-{serial}-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        Status = "导出 logcat…";
        try
        {
            var result = await _debug.ExportLogcatZipAsync(serial, dlg.FileName);
            Status = result.Succeeded ? ("已导出：" + dlg.FileName) : ("失败：" + result.FailureMessage);
            Output = result.CombinedOutput;
        }
        catch (Exception ex)
        {
            Status = "logcat 导出异常：" + ex.Message;
            Output = ex.ToString();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string DefaultExportDirectory()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TestEngineer",
            "Exports");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }
}
