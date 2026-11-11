using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Desktop.ViewModels;

public sealed partial class ConsolePageViewModel : ObservableObject
{
    private readonly IDeviceContext _devices;
    private readonly IConsoleService _console;
    private readonly IConsoleHistory _historyStore;
    private readonly IDebugCommandService _debug;
    private readonly List<string> _history;
    private int _historyIndex = -1;
    private CancellationTokenSource? _runCts;

    public ConsolePageViewModel(
        IDeviceContext devices,
        IConsoleService console,
        IConsoleHistory historyStore,
        IDebugCommandService debug)
    {
        _devices = devices;
        _console = console;
        _historyStore = historyStore;
        _debug = debug;
        _history = [.. historyStore.Items];
        foreach (var chip in debug.ConsoleChips)
        {
            Chips.Add(chip);
        }
    }

    public ObservableCollection<ConsoleChipDefinition> Chips { get; } = [];

    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "输入 adb 参数行（可省略开头 adb）";
    [ObservableProperty] private bool _isRunning;

    [RelayCommand]
    private async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(Input) || IsRunning) return;
        Remember(Input);
        _runCts = new CancellationTokenSource();
        IsRunning = true;
        Status = "执行中…";
        var buf = new StringBuilder();
        void Append(string line)
        {
            buf.AppendLine(line);
            Application.Current.Dispatcher.Invoke(() => Output = buf.ToString());
        }

        try
        {
            var result = await _console.ExecuteAsync(Input.Trim(), _devices.Current?.Serial, Append, _runCts.Token);
            if (buf.Length == 0)
            {
                Output = result.CombinedOutput;
            }

            Status = result.Succeeded ? $"完成（{result.Duration.TotalSeconds:0.0}s）" : ("失败：" + result.FailureMessage);
        }
        catch (OperationCanceledException)
        {
            Status = "已取消";
        }
        catch (Exception ex)
        {
            Status = "异常：" + ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _runCts?.Cancel();

    [RelayCommand]
    private void ClearOutput() => Output = "";

    [RelayCommand]
    private void ClearHistory()
    {
        _history.Clear();
        _historyStore.Clear();
        _historyIndex = -1;
        Status = "历史已清除";
    }

    [RelayCommand]
    private void UseChip(ConsoleChipDefinition? chip)
    {
        if (chip is null) return;
        if (chip.RequiresUserdebug || chip.RequiresRoot)
        {
            // Soft warn; still allow attempt so failure message is visible.
            Status = chip.RequiresUserdebug
                ? "该指令通常需要 userdebug/eng"
                : "该指令通常需要 root";
        }

        if (string.Equals(chip.Kind, "logcat-filter", StringComparison.OrdinalIgnoreCase))
        {
            Input = "logcat -d -t 200";
            Status = "已填入近期 logcat；请在输出区用 Ctrl+F 搜关键字（不做 shell 管道）";
            return;
        }

        if (string.IsNullOrWhiteSpace(chip.Line)) return;
        if (chip.IsDangerous)
        {
            if (MessageBox.Show($"确认执行：{chip.Title}？\n{chip.Line}", "高危指令", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Input = chip.Line;
        _ = RunAsync();
    }

    public void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            _ = RunAsync();
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
        _historyStore.Add(line);
        _history.Clear();
        _history.AddRange(_historyStore.Items);
        _historyIndex = -1;
    }
}
