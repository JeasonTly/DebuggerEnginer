using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestEngineer.Adb.Parsing;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;
using TestEngineer.Desktop.Services;
using TestEngineer.Desktop.Views;

namespace TestEngineer.Desktop.ViewModels;

/// <summary>调试页分类轨项。</summary>
public sealed partial class DebugCategoryItem : ObservableObject
{
    public DebugCategoryItem(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>调试指令行（标题 + 命令摘要 + 执行/配置）。</summary>
public sealed partial class DebugCommandItemViewModel : ObservableObject
{
    public DebugCommandItemViewModel(DebugCommandDefinition definition)
    {
        Definition = definition;
        Title = definition.Title;
        CommandSummary = BuildSummary(definition);
        ActionLabel = definition.Kind is "monkey-stop" ? "停止" : NeedsConfig(definition) ? "配置" : "执行";
    }

    public DebugCommandDefinition Definition { get; }
    public string Title { get; }
    public string CommandSummary { get; private set; }
    public string ActionLabel { get; }

    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string? _disabledReason;

    public void SetUnavailable(string reason)
    {
        IsEnabled = false;
        DisabledReason = reason;
        if (!CommandSummary.Contains(reason, StringComparison.Ordinal))
        {
            CommandSummary = reason;
            OnPropertyChanged(nameof(CommandSummary));
        }
    }

    public void SetAvailable(string summary)
    {
        IsEnabled = true;
        DisabledReason = null;
        CommandSummary = summary;
        OnPropertyChanged(nameof(CommandSummary));
    }

    public static bool NeedsConfig(DebugCommandDefinition d)
    {
        if (d.ParameterList.Count > 0) return true;
        return d.Kind is "monkey-app" or "monkey-system" or "export-logs" or "bugreport"
            or "am-force-stop" or "pm-path" or "pm-clear";
    }

    public static string BuildSummary(DebugCommandDefinition d)
    {
        if (!string.IsNullOrWhiteSpace(d.Description))
        {
            return d.Description;
        }

        return d.Kind switch
        {
            "screencap" => "screencap + pull",
            "export-logs" => "logcat + bugreport + 平台日志 → 型号_日期.zip",
            "bugreport" => "adb bugreport <path.zip>",
            "monkey-app" => "从全部应用中选择 · 有图标优先",
            "monkey-system" => "monkey --throttle 300 -v <events>",
            "am-force-stop" => "am force-stop <pkg>",
            "pm-path" => "pm path <pkg>",
            "pm-clear" => "pm clear <pkg>",
            _ when d.AdbArgs.Count > 0 => string.Join(' ', d.AdbArgs),
            _ => d.Id,
        };
    }
}

public sealed partial class DebugPageViewModel : ObservableObject
{
    private static readonly string[] PreferredCategoryOrder =
        ["诊断查询", "日志管理", "现场条件", "应用", "显示按键", "设置菜单", "Monkey"];

    private const int MaxResultChars = 100_000;

    private readonly IDeviceContext _devices;
    private readonly IDebugCommandService _debug;
    private readonly AppsPageViewModel _apps;
    private readonly List<DebugCommandItemViewModel> _allItems = [];
    private readonly DispatcherTimer _taskTimer;
    private readonly List<ActiveRun> _runs = [];

    public DebugPageViewModel(IDeviceContext devices, IDebugCommandService debug, AppsPageViewModel apps)
    {
        _devices = devices;
        _debug = debug;
        _apps = apps;
        _taskTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _taskTimer.Tick += (_, _) => RefreshTaskClock();

        foreach (var c in _debug.Commands)
        {
            _allItems.Add(new DebugCommandItemViewModel(c));
        }

        Categories = BuildCategories(_allItems.Select(i => i.Definition.Category));
        SelectedCategory = Categories.FirstOrDefault()?.Key ?? "";
        SyncCategorySelection();
        RefreshFiltered();
        _ = RefreshAvailabilityAsync();
        _devices.CurrentChanged += (_, _) => _ = RefreshAvailabilityAsync();
    }

    public ObservableCollection<DebugCategoryItem> Categories { get; }
    public ObservableCollection<DebugCommandItemViewModel> FilteredCommands { get; } = [];

    [ObservableProperty] private string _selectedCategory = "";
    [ObservableProperty] private string _status = "快捷操作 · 结果弹窗 · 作用于当前设备";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _taskTitle = "";
    [ObservableProperty] private string _taskDetail = "";
    [ObservableProperty] private double _taskProgress;
    [ObservableProperty] private bool _taskIndeterminate = true;
    [ObservableProperty] private string _monkeyPackage = "";
    [ObservableProperty] private int _monkeyMinutes = 1;
    [ObservableProperty] private bool _isEmptyCategory;

    partial void OnSelectedCategoryChanged(string value)
    {
        SyncCategorySelection();
        RefreshFiltered();
    }

    [RelayCommand]
    private void SelectCategory(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        SelectedCategory = key;
    }

    [RelayCommand]
    private void CancelRunningTask()
    {
        if (_runs.Count == 0 || _runs[0].Cts.IsCancellationRequested)
        {
            return;
        }

        var run = _runs[0];
        run.HoldDetail = true;
        run.Detail = "正在取消…";
        TaskDetail = run.Detail;
        run.Cts.Cancel();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RunAsync(DebugCommandItemViewModel? item)
    {
        if (item is null || !item.IsEnabled) return;
        if (item.Definition.Kind is "monkey-stop")
        {
            await StopDeviceMonkeyAsync();
            return;
        }

        var cmd = item.Definition;
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _devices.Current?.IsOnline != true)
        {
            Status = "设备已断开";
            ModalSession.Alert("设备已断开，无法执行指令。", "调试指令", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (cmd.Kind is "export-logs")
        {
            await ExportLogcatAsync();
            return;
        }

        if (cmd.Kind is "monkey-app" or "monkey-system")
        {
            await ConfigureAndRunMonkeyAsync(cmd.Kind == "monkey-system");
            return;
        }

        IReadOnlyDictionary<string, string>? parameters = null;
        if (cmd.ParameterList.Count > 0)
        {
            var prompted = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var parameter in cmd.ParameterList)
            {
                string text;
                if (parameter.Name == "package")
                {
                    var picked = await PickMonkeyAppAsync();
                    if (picked is null)
                    {
                        return;
                    }

                    text = picked.PackageName;
                    MonkeyPackage = text;
                }
                else
                {
                    var entered = PromptText(cmd.Title, parameter.Label, parameter.DefaultValue);
                    if (entered is null)
                    {
                        return;
                    }

                    text = entered.Trim();
                    if (text.Length == 0)
                    {
                        text = parameter.DefaultValue?.Trim() ?? "";
                    }

                    if (text.Length == 0)
                    {
                        Status = "缺少" + parameter.Label;
                        return;
                    }
                }

                prompted[parameter.Name] = text;
            }

            parameters = prompted;
        }
        else if (cmd.Kind is "am-force-stop" or "pm-path" or "pm-clear")
        {
            var picked = await PickMonkeyAppAsync();
            if (picked is null) return;
            MonkeyPackage = picked.PackageName;
            parameters = new Dictionary<string, string> { ["package"] = MonkeyPackage };
        }

        if (cmd.IsDangerous)
        {
            if (ModalSession.Alert($"确认执行危险指令：{cmd.Title}？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var run = BeginTask($"执行 {cmd.Title}", indeterminate: true);
        var token = run.Cts.Token;
        Status = $"执行 {cmd.Title}…";
        var capture = new Capture { Run = run };
        var echo = BuildEcho(serial, cmd, parameters);
        AppendCaptured(capture, echo);
        try
        {
            var result = await _debug.RunAsync(serial, cmd.Id, parameters, line => AppendCaptured(capture, line), token);
            if (capture.Buffer.Length <= echo.Length + 2)
            {
                AppendCaptured(capture, string.IsNullOrWhiteSpace(result.CombinedOutput) ? "(no output)" : result.CombinedOutput);
            }

            Status = result.Cancelled
                ? "已取消"
                : result.Succeeded
                    ? (cmd.SilentResult ? $"已执行 {cmd.Title}" : "完成")
                    : ("失败：" + result.FailureMessage);
            var shown = cmd.Id == "top-activity"
                ? ForegroundComponentParser.Format(string.IsNullOrWhiteSpace(result.CombinedOutput) ? capture.Buffer.ToString() : result.CombinedOutput)
                : capture.Buffer.ToString();
            ShowIfNeeded(cmd, cmd.Title, shown);
        }
        catch (OperationCanceledException)
        {
            Status = "已取消";
            ShowIfNeeded(cmd, cmd.Title, capture.Buffer.ToString() + Environment.NewLine + "已取消");
        }
        catch (Exception ex)
        {
            Status = "异常：" + ex.Message;
            ShowIfNeeded(cmd, cmd.Title, echo + Environment.NewLine + ex);
        }
        finally
        {
            EndTask(run);
        }
    }

    private async Task ConfigureAndRunMonkeyAsync(bool systemWide)
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial)) return;

        string? package = null;
        string? displayName = null;
        if (!systemWide)
        {
            var picked = await PickMonkeyAppAsync();
            if (picked is null) return;
            package = picked.PackageName;
            displayName = picked.DisplayName;
            MonkeyPackage = package;
        }

        var minutesText = PromptText(systemWide ? "系统 Monkey" : "应用 Monkey", "时长（分钟）", MonkeyMinutes.ToString());
        if (!int.TryParse(minutesText, out var minutes) || minutes < 1)
        {
            Status = "时长无效";
            return;
        }

        MonkeyMinutes = minutes;
        var seedText = PromptText(systemWide ? "系统 Monkey" : "应用 Monkey", "Seed（可空）", "42");
        long? seed = long.TryParse(seedText, out var s) ? s : null;

        var confirm = systemWide
            ? $"确认对整机运行 Monkey {minutes} 分钟？（高危）"
            : $"对 {displayName}（{package}）跑 Monkey {minutes} 分钟？";
        if (ModalSession.Alert(confirm, "Monkey", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (_runs.Any(r => r.IsMonkey && !r.Cts.IsCancellationRequested))
        {
            Status = "已有 Monkey 在运行";
            return;
        }

        var run = BeginTask($"Monkey {minutes} 分钟", indeterminate: false, monkey: true);
        var token = run.Cts.Token;
        Status = "Monkey 运行中…";
        var progress = new Progress<RunningTaskState>(state => ApplyTaskProgress(run, state));
        var capture = new Capture { Run = run };
        try
        {
            var result = await _debug.RunMonkeyAsync(
                serial,
                new MonkeyOptions(package, Math.Max(1, minutes) * 60, Seed: seed),
                progress,
                line => AppendCaptured(capture, line),
                token);
            if (capture.Buffer.Length == 0) AppendCaptured(capture, result.Command.CombinedOutput);
            AppendCaptured(capture, $"crash={result.CrashCount} anr={result.AnrCount} byDuration={result.StoppedByDuration}");
            Status = $"Monkey 结束 · crash={result.CrashCount} anr={result.AnrCount}";
            ShowResult(systemWide ? "系统 Monkey" : "应用 Monkey", capture.Buffer.ToString());
        }
        catch (OperationCanceledException)
        {
            Status = "Monkey 已取消";
            ShowResult("Monkey", capture.Buffer.ToString() + Environment.NewLine + "已取消");
        }
        catch (Exception ex)
        {
            Status = "Monkey 失败：" + ex.Message;
            ShowResult("Monkey", ex.ToString());
        }
        finally
        {
            EndTask(run);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RunMonkeyAppAsync() => await ConfigureAndRunMonkeyAsync(false);

    private async Task<AppPackage?> PickMonkeyAppAsync()
    {
        var run = BeginTask("读取应用列表", indeterminate: true);
        var token = run.Cts.Token;
        IReadOnlyList<AppPackage> apps;
        try
        {
            apps = await _apps.GetAllPackagesAsync(token);
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Status = "应用列表加载失败：" + ex.Message;
            ModalSession.Alert("读取应用列表失败：" + ex.Message, "选择应用", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        finally
        {
            EndTask(run);
        }

        if (apps.Count == 0)
        {
            Status = "没有可选择的应用";
            ModalSession.Alert("当前设备没有可选择的应用。", "选择应用", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var dialog = new MonkeyAppPickerWindow(apps, MonkeyPackage);
        if (ModalSession.Show(dialog) != true || string.IsNullOrWhiteSpace(dialog.SelectedPackageName))
        {
            return null;
        }

        return apps.FirstOrDefault(p => p.PackageName == dialog.SelectedPackageName);
    }

    private async Task StopDeviceMonkeyAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _devices.Current?.IsOnline != true)
        {
            Status = "设备已断开";
            ModalSession.Alert("设备已断开，无法停止 Monkey。", "停止 Monkey", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var local = _runs.FirstOrDefault(r => r.IsMonkey && !r.Cts.IsCancellationRequested);
        var hadLocal = local is not null;
        if (local is not null)
        {
            local.HoldDetail = true;
            local.Detail = "正在停止 Monkey…";
            if (_runs.Count > 0 && ReferenceEquals(_runs[0], local))
            {
                TaskDetail = local.Detail;
            }

            local.Cts.Cancel();
        }

        try
        {
            var result = await _debug.StopRunningMonkeyAsync(serial);
            if (hadLocal)
            {
                return;
            }

            Status = result.Succeeded ? "Monkey 已停止" : ("停止失败：" + result.FailureMessage);
        }
        catch (Exception ex)
        {
            Status = "停止 Monkey 失败：" + ex.Message;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ExportLogcatAsync()
    {
        var serial = _devices.Current?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || _devices.Current?.IsOnline != true)
        {
            Status = "设备未连接";
            return;
        }

        var model = string.IsNullOrWhiteSpace(_devices.Current?.Model)
            ? "device"
            : TestEngineer.Adb.DebugCommandService.SanitizeFileToken(_devices.Current!.Model);
        var stamp = DateTime.Now.ToString("yyyyMMdd");
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出日志包（logcat / bugreport / 平台日志）",
            Filter = "Zip|*.zip",
            InitialDirectory = DefaultExportDirectory(),
            FileName = $"{model}_{stamp}.zip",
        };
        if (ModalSession.Show(dlg) != true) return;

        var run = BeginTask("导出日志包", indeterminate: true);
        var token = run.Cts.Token;
        Status = "导出日志包（含 bugreport 与平台日志，可能较久）…";
        var progress = new Progress<RunningTaskState>(state => ApplyTaskProgress(run, state));
        try
        {
            var result = await _debug.ExportLogcatZipAsync(serial, dlg.FileName, progress, token);
            Status = result.Cancelled ? "已取消" : result.Succeeded ? ("已导出：" + dlg.FileName) : ("失败：" + result.FailureMessage);
            ShowResult("导出日志包并压缩",
                $"$ adb -s {serial} … → {dlg.FileName}\n\n{result.CombinedOutput}");
        }
        catch (OperationCanceledException)
        {
            Status = "日志包已取消";
            ShowResult("导出日志包", "已取消");
        }
        catch (Exception ex)
        {
            Status = "日志包导出异常：" + ex.Message;
            ShowResult("导出日志包", ex.ToString());
        }
        finally
        {
            EndTask(run);
        }
    }

    private ActiveRun BeginTask(string title, bool indeterminate, bool monkey = false)
    {
        var run = new ActiveRun
        {
            Title = title,
            Cts = new CancellationTokenSource(),
            IsMonkey = monkey,
            Indeterminate = indeterminate,
        };
        _runs.Add(run);
        IsBusy = true;
        if (_runs.Count == 1)
        {
            _taskTimer.Start();
        }

        PublishRun(_runs[0]);
        return run;
    }

    private void ApplyTaskProgress(ActiveRun run, RunningTaskState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Title))
        {
            run.Title = state.Title;
        }

        if (state.Progress is { } fraction)
        {
            run.Indeterminate = false;
            run.Progress = Math.Clamp(fraction * 100, 0, 100);
        }

        if (!run.HoldDetail && !string.IsNullOrWhiteSpace(state.Message))
        {
            run.Detail = state.Message;
        }

        if (_runs.Count > 0 && ReferenceEquals(_runs[0], run))
        {
            PublishRun(run);
        }
    }

    private void PublishRun(ActiveRun run)
    {
        TaskTitle = _runs.Count <= 1 ? run.Title : $"{run.Title} · 另有 {_runs.Count - 1} 条";
        TaskIndeterminate = run.Indeterminate;
        TaskProgress = run.Progress;
        TaskDetail = run.Detail;
        IsBusy = true;
    }

    private void RefreshTaskClock()
    {
        if (_runs.Count == 0)
        {
            return;
        }

        var run = _runs[0];
        if (run.HoldDetail)
        {
            run.HoldDetail = false;
            return;
        }

        if (!run.Indeterminate)
        {
            return;
        }

        var elapsed = DateTime.UtcNow - run.StartedUtc;
        run.Detail = string.IsNullOrEmpty(run.LastLine)
            ? $"已运行 {elapsed:mm\\:ss}"
            : $"已运行 {elapsed:mm\\:ss} · {run.LastLine}";
        TaskDetail = run.Detail;
    }

    private static void AppendCaptured(Capture capture, string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        if (capture.Buffer.Length > MaxResultChars)
        {
            if (!capture.Truncated)
            {
                capture.Truncated = true;
                capture.Buffer.AppendLine("…后续输出已省略");
            }
        }
        else
        {
            capture.Buffer.AppendLine(line);
        }

        var text = line.Trim();
        if (text.Length == 0 || capture.Run is null)
        {
            return;
        }

        if (text.Length > 96)
        {
            text = text[..96];
        }

        var run = capture.Run;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            run.LastLine = text;
            return;
        }

        dispatcher.BeginInvoke(() => run.LastLine = text);
    }

    private void EndTask(ActiveRun run)
    {
        _runs.Remove(run);
        run.Cts.Dispose();
        if (_runs.Count == 0)
        {
            _taskTimer.Stop();
            IsBusy = false;
            TaskProgress = 0;
            return;
        }

        PublishRun(_runs[0]);
    }

    private sealed class ActiveRun
    {
        public required string Title { get; set; }
        public required CancellationTokenSource Cts { get; init; }
        public bool IsMonkey { get; init; }
        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        public string LastLine = "";
        public bool Indeterminate { get; set; } = true;
        public double Progress { get; set; }
        public string Detail { get; set; } = "已运行 00:00";
        public bool HoldDetail { get; set; }
    }

    private sealed class Capture
    {
        public StringBuilder Buffer { get; } = new();
        public bool Truncated;
        public ActiveRun? Run;
    }

    private async Task RefreshAvailabilityAsync()
    {
        var serial = _devices.Current?.Serial;
        var online = _devices.Current?.IsOnline == true && !string.IsNullOrWhiteSpace(serial);
        foreach (var item in _allItems)
        {
            var def = item.Definition;
            var baseSummary = DebugCommandItemViewModel.BuildSummary(def);
            if (!online)
            {
                item.IsEnabled = false;
                item.DisabledReason = "设备已断开";
                continue;
            }

            if (string.IsNullOrWhiteSpace(def.WhenPackageExists))
            {
                item.SetAvailable(baseSummary);
                continue;
            }

            try
            {
                var ok = await _debug.IsAvailableAsync(serial!, def);
                if (ok)
                {
                    item.SetAvailable(baseSummary);
                }
                else
                {
                    item.SetUnavailable($"OEM={def.WhenPackageExists} 未检测到");
                }
            }
            catch
            {
                item.SetUnavailable($"OEM={def.WhenPackageExists} 未检测到");
            }
        }
    }

    private void RefreshFiltered()
    {
        FilteredCommands.Clear();
        foreach (var item in _allItems.Where(i =>
                     string.IsNullOrEmpty(SelectedCategory) || i.Definition.Category == SelectedCategory))
        {
            FilteredCommands.Add(item);
        }

        IsEmptyCategory = FilteredCommands.Count == 0;
    }

    private void SyncCategorySelection()
    {
        foreach (var cat in Categories)
        {
            cat.IsSelected = string.Equals(cat.Key, SelectedCategory, StringComparison.Ordinal);
        }
    }

    private static ObservableCollection<DebugCategoryItem> BuildCategories(IEnumerable<string> raw)
    {
        var present = raw.Distinct(StringComparer.Ordinal).ToList();
        var ordered = new List<DebugCategoryItem>();
        foreach (var key in PreferredCategoryOrder)
        {
            var match = present.FirstOrDefault(c => NormalizeCat(c) == NormalizeCat(key));
            if (match is null) continue;
            present.Remove(match);
            ordered.Add(new DebugCategoryItem(match, DisplayCat(match)));
        }

        foreach (var rest in present.OrderBy(c => c, StringComparer.Ordinal))
        {
            ordered.Add(new DebugCategoryItem(rest, DisplayCat(rest)));
        }

        return new ObservableCollection<DebugCategoryItem>(ordered);
    }

    private static string NormalizeCat(string c) =>
        c.Replace(" ", "", StringComparison.Ordinal).Replace("·", "/", StringComparison.Ordinal).Replace("／", "/", StringComparison.Ordinal);

    private static string DisplayCat(string c) => c;

    private static string BuildEcho(string serial, DebugCommandDefinition cmd, IReadOnlyDictionary<string, string>? parameters)
    {
        try
        {
            var args = cmd.AdbArgs.Count > 0
                ? DebugCommandCatalogEcho(cmd, parameters)
                : (cmd.Kind ?? cmd.Id);
            return $"$ adb -s {serial} {args}";
        }
        catch
        {
            return $"$ adb -s {serial} {cmd.Id}";
        }
    }

    private static string DebugCommandCatalogEcho(DebugCommandDefinition cmd, IReadOnlyDictionary<string, string>? parameters)
    {
        // 避免 Desktop 直接依赖 Adb 程序集的 Expand：简单替换展示。
        var parts = cmd.AdbArgs.Select(a =>
        {
            if (parameters is null) return a;
            var result = a;
            foreach (var (k, v) in parameters)
            {
                result = result.Replace("{" + k + "}", v, StringComparison.Ordinal);
            }

            return result;
        });
        return string.Join(' ', parts);
    }

    private static void ShowIfNeeded(DebugCommandDefinition cmd, string title, string output)
    {
        if (!cmd.SilentResult)
        {
            ShowResult(title, output);
        }
    }

    private static void ShowResult(string title, string output)
    {
        if (output.Length > MaxResultChars)
        {
            output = output[..MaxResultChars] + Environment.NewLine + "…输出过长，已截断显示。";
        }

        ModalSession.Show(new CommandResultWindow(title, output));
    }

    private static string? PromptText(string title, string label, string? initial)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)Application.Current!.FindResource("Brush.Surface"),
        };
        var input = new System.Windows.Controls.TextBox
        {
            Text = initial ?? "",
            Height = 32,
            Margin = new Thickness(0, 8, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
        };
        var ok = false;
        var panel = new System.Windows.Controls.DockPanel { Margin = new Thickness(16) };
        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Bottom);
        var cancelBtn = new System.Windows.Controls.Button { Content = "取消", Margin = new Thickness(0, 0, 8, 0), MinWidth = 72 };
        cancelBtn.Click += (_, _) => dialog.Close();
        var okBtn = new System.Windows.Controls.Button { Content = "开始", MinWidth = 72, Style = (Style)Application.Current.FindResource("Button.Primary") };
        okBtn.Click += (_, _) => { ok = true; dialog.Close(); };
        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(okBtn);
        panel.Children.Add(buttons);
        panel.Children.Add(new System.Windows.Controls.StackPanel
        {
            Children =
            {
                new System.Windows.Controls.TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Brush.TextSecondary"),
                },
                input,
            },
        });
        dialog.Content = panel;
        ModalSession.Show(dialog);
        return ok ? input.Text : null;
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
