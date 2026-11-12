using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Desktop.ViewModels;

public sealed partial class ConsolePageViewModel : ObservableObject
{
    private static readonly HashSet<string> FillOnlyChipIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "logcat-error",
        "avbctl",
    };

    private readonly IDeviceContext _devices;
    private readonly IConsoleService _console;
    private readonly IConsoleHistory _historyStore;
    private readonly List<string> _history;
    private int _historyIndex = -1;
    private CancellationTokenSource? _runCts;

    /// <summary>客户端输出过滤（logcat 关键字，| 分隔）；null/空表示不过滤。</summary>
    private string[]? _outputFilterTerms;

    private readonly object _streamGate = new();
    private StringBuilder? _streamBuf;
    private int _streamedChunks;
    private int _flushScheduled;
    private string? _pendingPrefix;

    public ConsolePageViewModel(
        IDeviceContext devices,
        IConsoleService console,
        IConsoleHistory historyStore,
        IDebugCommandService debug)
    {
        _devices = devices;
        _console = console;
        _historyStore = historyStore;
        _history = [.. historyStore.Items];
        foreach (var chip in debug.ConsoleChips)
        {
            Chips.Add(chip);
        }

        foreach (var line in _history)
        {
            HistoryItems.Add(line);
        }

        Output = "TestEngineer Console · ready";
        _devices.CurrentChanged += (_, _) => RefreshInputState();
        RefreshInputState();
    }

    public ObservableCollection<ConsoleChipDefinition> Chips { get; } = [];
    public ObservableCollection<string> HistoryItems { get; } = [];

    public event EventHandler? OutputAppended;

    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "自由 adb · 常用权限/日志快捷填入 · ↑↓ 历史";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isHistoryVisible;
    [ObservableProperty] private bool _isInputEnabled = true;
    [ObservableProperty] private string? _inputToolTip;
    [ObservableProperty] private string _runButtonLabel = "运行";

    public bool IsHistoryEmpty => HistoryItems.Count == 0;

    partial void OnIsRunningChanged(bool value) => RunButtonLabel = value ? "中止" : "运行";

    [RelayCommand]
    private async Task RunOrCancelAsync()
    {
        if (IsRunning)
        {
            try
            {
                _runCts?.Cancel();
            }
            catch
            {
                // ignore
            }

            Status = "正在中止…";
            return;
        }

        await RunAsync();
    }

    private async Task RunAsync()
    {
        var command = (Input ?? string.Empty).Trim();
        if (command.Length == 0 || IsRunning)
        {
            if (command.Length == 0)
            {
                Status = "请输入 adb 命令后再运行";
            }

            return;
        }

        Input = command;
        Remember(command);
        _runCts = new CancellationTokenSource();
        IsRunning = true;
        Status = "执行中…（可点「中止」）";

        // 保留既有输出；命令之间空一行，并回显 `$ 命令`，便于对应输入/输出
        var prefix = string.IsNullOrWhiteSpace(Output)
            ? ""
            : (Output.EndsWith('\n') ? Output : Output + "\n");
        if (prefix.Length > 0 && !prefix.EndsWith("\n\n", StringComparison.Ordinal))
        {
            prefix += "\n";
        }

        _pendingPrefix = prefix;
        lock (_streamGate)
        {
            _streamBuf = new StringBuilder();
            _streamBuf.AppendLine("$ " + command);
            _streamedChunks = 0;
        }

        Interlocked.Exchange(ref _flushScheduled, 0);
        FlushOutputToUi(force: true);

        void OnLine(string line)
        {
            if (_outputFilterTerms is { Length: > 0 } terms
                && !terms.Any(t => line.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            lock (_streamGate)
            {
                _streamBuf?.AppendLine(line);
                _streamedChunks++;
            }

            ScheduleOutputFlush();
        }

        try
        {
            var serial = _devices.Current?.Serial;
            var result = await _console.ExecuteAsync(command, serial, OnLine, _runCts.Token)
                .ConfigureAwait(true);

            FlushOutputToUi(force: true);

            if (result.Cancelled || _runCts.IsCancellationRequested)
            {
                AppendSystemLine("← 已中止");
                Status = "已中止";
                return;
            }

            if (result.TimedOut)
            {
                AppendSystemLine("← 超时 — " + result.FailureMessage);
                Status = "超时：" + result.FailureMessage;
                return;
            }

            var streamed = 0;
            lock (_streamGate)
            {
                streamed = _streamedChunks;
            }

            if (streamed == 0 && !string.IsNullOrWhiteSpace(result.CombinedOutput))
            {
                foreach (var line in result.CombinedOutput.Replace("\r\n", "\n", StringComparison.Ordinal)
                             .Split('\n'))
                {
                    OnLine(line.TrimEnd('\r'));
                }

                FlushOutputToUi(force: true);
                streamed = _streamedChunks;
            }

            if (streamed == 0)
            {
                AppendSystemLine("← (无输出)");
            }

            AppendSystemLine(result.Succeeded
                ? $"← exit {result.ExitCode} · {result.Duration.TotalSeconds:0.0}s"
                : $"← 失败 · {result.FailureMessage}");

            Status = result.Succeeded
                ? $"完成（{result.Duration.TotalSeconds:0.0}s）"
                : ("失败：" + result.FailureMessage);
        }
        catch (OperationCanceledException)
        {
            FlushOutputToUi(force: true);
            AppendSystemLine("← 已中止");
            Status = "已中止";
        }
        catch (Exception ex)
        {
            FlushOutputToUi(force: true);
            AppendSystemLine("← 异常 — " + ex.Message);
            Status = "异常：" + ex.Message;
        }
        finally
        {
            _outputFilterTerms = null;
            IsRunning = false;
            FlushOutputToUi(force: true);
            lock (_streamGate)
            {
                _streamBuf = null;
            }
        }
    }

    private void ScheduleOutputFlush()
    {
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 1)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            Interlocked.Exchange(ref _flushScheduled, 0);
            return;
        }

        // BeginInvoke：不堵后台读管道，中止点击才能进 UI 队列
        _ = dispatcher.BeginInvoke(
            () =>
            {
                Interlocked.Exchange(ref _flushScheduled, 0);
                FlushOutputToUi(force: false);
            },
            DispatcherPriority.Background);
    }

    private void FlushOutputToUi(bool force)
    {
        string text;
        lock (_streamGate)
        {
            if (_streamBuf is null)
            {
                return;
            }

            if (!force && _streamBuf.Length == 0)
            {
                return;
            }

            text = (_pendingPrefix ?? "") + _streamBuf;
        }

        if (string.Equals(Output, text, StringComparison.Ordinal))
        {
            return;
        }

        Output = text;
        OutputAppended?.Invoke(this, EventArgs.Empty);
    }

    private void AppendSystemLine(string line)
    {
        lock (_streamGate)
        {
            _streamBuf ??= new StringBuilder();
            _streamBuf.AppendLine(line);
        }

        FlushOutputToUi(force: true);
    }

    [RelayCommand]
    private void ClearOutput() => Output = "";

    [RelayCommand]
    private void ClearHistory()
    {
        if (MessageBox.Show("确认清除命令历史？", "清除历史", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _history.Clear();
        _historyStore.Clear();
        HistoryItems.Clear();
        _historyIndex = -1;
        OnPropertyChanged(nameof(IsHistoryEmpty));
        Status = "命令历史已清空";
        MessageBox.Show("命令历史已清空", "控制台", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ToggleHistory() => IsHistoryVisible = !IsHistoryVisible;

    [RelayCommand]
    private void FillHistory(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Input = line;
    }

    [RelayCommand]
    private void RemoveHistory(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (!_historyStore.Remove(line))
        {
            return;
        }

        SyncHistoryUi();
        Status = "已从历史移除";
    }

    [RelayCommand]
    private void UseChip(ConsoleChipDefinition? chip)
    {
        if (chip is null) return;
        if (chip.RequiresUserdebug || chip.RequiresRoot)
        {
            Status = chip.RequiresUserdebug
                ? "该指令通常需要 userdebug/eng"
                : "该指令通常需要 root";
        }

        if (string.Equals(chip.Kind, "logcat-filter", StringComparison.OrdinalIgnoreCase))
        {
            OpenLogcatFilter();
            return;
        }

        if (string.IsNullOrWhiteSpace(chip.Line)) return;

        var fillOnly = FillOnlyChipIds.Contains(chip.Id)
                       || string.Equals(chip.Title, "logcat 仅 Error", StringComparison.Ordinal)
                       || string.Equals(chip.Title, "禁用验证 (AVB)", StringComparison.Ordinal)
                       || string.Equals(chip.Title, "禁用验证 AVB", StringComparison.Ordinal);

        if (chip.IsDangerous && !fillOnly)
        {
            if (MessageBox.Show($"确认执行：{chip.Title}？\n{chip.Line}", "高危指令", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Input = chip.Line;
        if (!fillOnly)
        {
            _ = RunOrCancelAsync();
        }
    }

    private void OpenLogcatFilter()
    {
        var dialog = new Window
        {
            Title = "logcat 关键字过滤",
            Width = 480,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)Application.Current!.FindResource("Brush.Surface"),
        };

        var kw = new System.Windows.Controls.TextBox
        {
            Text = "AndroidRuntime|FATAL|ANR",
            Height = 32,
            Margin = new Thickness(0, 4, 0, 8),
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
        };
        var level = new System.Windows.Controls.ComboBox
        {
            Height = 32,
            Margin = new Thickness(0, 4, 0, 0),
            ItemsSource = new[] { "*:V", "*:D", "*:I", "*:W", "*:E" },
            SelectedIndex = 2,
        };

        var ok = false;
        var root = new System.Windows.Controls.DockPanel { Margin = new Thickness(16) };
        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Bottom);
        var cancelBtn = new System.Windows.Controls.Button { Content = "取消", Margin = new Thickness(0, 0, 8, 0), MinWidth = 72 };
        cancelBtn.Click += (_, _) => dialog.Close();
        var runBtn = new System.Windows.Controls.Button
        {
            Content = "填入并运行",
            MinWidth = 96,
            Style = (Style)Application.Current.FindResource("Button.Primary"),
        };
        runBtn.Click += (_, _) => { ok = true; dialog.Close(); };
        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(runBtn);

        var body = new System.Windows.Controls.StackPanel();
        body.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "关键字（多关键字用 | 分隔，客户端过滤）",
            FontSize = 12,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Brush.TextSecondary"),
        });
        body.Children.Add(kw);
        body.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "级别",
            FontSize = 12,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Brush.TextSecondary"),
        });
        body.Children.Add(level);
        root.Children.Add(buttons);
        root.Children.Add(body);
        dialog.Content = root;
        dialog.ShowDialog();
        if (!ok) return;

        var keyword = kw.Text.Trim();
        var lv = level.SelectedItem?.ToString() ?? "*:I";
        _outputFilterTerms = string.IsNullOrEmpty(keyword)
            ? null
            : keyword.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // 不用管道；关键字在输出回调里按 | 或匹配。-d 抓缓冲，可中止。
        Input = $"logcat -d -v threadtime {lv}";
        Status = _outputFilterTerms is null
            ? "已填入 logcat -d"
            : $"logcat -d；输出仅保留含「{keyword}」的行";
        _ = RunOrCancelAsync();
    }

    public void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if ((key is Key.Enter or Key.Return) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // 只走这一条路径，避免与 IsDefault 按钮连点把刚启动的任务立刻中止
            e.Handled = true;
            if (!IsRunning)
            {
                _ = RunAsync();
            }

            return;
        }

        if (e.Key == Key.Up && _history.Count > 0)
        {
            e.Handled = true;
            _historyIndex = _historyIndex < 0 ? _history.Count - 1 : Math.Max(0, _historyIndex - 1);
            Input = _history[_historyIndex];
        }
        else if (e.Key == Key.Down && _history.Count > 0)
        {
            e.Handled = true;
            if (_historyIndex < 0) return;
            _historyIndex++;
            if (_historyIndex >= _history.Count)
            {
                _historyIndex = -1;
                Input = "";
            }
            else
            {
                Input = _history[_historyIndex];
            }
        }
    }

    private void Remember(string line)
    {
        // ConsoleService 也会 Add；HistoryStore 会合并相邻重复
        _historyStore.Add(line);
        SyncHistoryUi();
    }

    private void SyncHistoryUi()
    {
        _history.Clear();
        _history.AddRange(_historyStore.Items);
        _historyIndex = -1;
        HistoryItems.Clear();
        foreach (var item in _history)
        {
            HistoryItems.Add(item);
        }

        OnPropertyChanged(nameof(IsHistoryEmpty));
    }

    private void RefreshInputState()
    {
        var online = _devices.Current?.IsOnline == true;
        IsInputEnabled = online;
        InputToolTip = online ? null : "设备已断开";
    }
}
