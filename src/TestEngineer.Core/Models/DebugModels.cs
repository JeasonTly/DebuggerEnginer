namespace TestEngineer.Core.Models;

/// <summary>调试命令的参数定义。</summary>
/// <param name="Name">占位符名，命令参数模板中以 <c>{Name}</c> 引用。</param>
/// <param name="Label">界面标签。</param>
/// <param name="DefaultValue">默认值。</param>
/// <param name="ValidationPattern">值必须匹配的正则，防止注入 shell 元字符。</param>
public sealed record DebugCommandParameter(
    string Name,
    string Label,
    string? DefaultValue = null,
    string ValidationPattern = DebugCommandParameter.SafeTokenPattern)
{
    public const string SafeTokenPattern = @"^[A-Za-z0-9._:/\-]+$";
}

/// <summary>数据驱动的调试命令定义。</summary>
/// <param name="Id">稳定标识。</param>
/// <param name="AdbArgs">adb 参数模板（不含 -s），元素中的 <c>{name}</c> 会被参数值替换。</param>
/// <param name="IsDangerous">危险操作，界面需二次确认。</param>
/// <param name="Kind">特殊类型（screencap / export-logs / bugreport / monkey-app / monkey-system 等），由服务专门处理；普通命令为 null。</param>
/// <param name="WhenPackageExists">仅当设备上存在该包名时才可用（OEM 专用入口）。</param>
public sealed record DebugCommandDefinition(
    string Id,
    string Title,
    string Category,
    string Description,
    IReadOnlyList<string> AdbArgs,
    IReadOnlyList<DebugCommandParameter>? Parameters = null,
    bool IsDangerous = false,
    TimeSpan? Timeout = null,
    string? Kind = null,
    string? WhenPackageExists = null)
{
    public IReadOnlyList<DebugCommandParameter> ParameterList => Parameters ?? [];
}

/// <summary>Monkey 压测参数。</summary>
/// <param name="Package">限定包名；null 表示全系统。</param>
/// <param name="DurationSeconds">运行时长，到点后强制结束 monkey。</param>
/// <param name="ThrottleMs">事件间隔毫秒。</param>
/// <param name="Seed">随机种子；null 则不指定。</param>
/// <param name="EventCount">事件数上限，默认取极大值，由时长控制结束。</param>
public sealed record MonkeyOptions(
    string? Package,
    int DurationSeconds,
    int ThrottleMs = 300,
    long? Seed = null,
    long EventCount = 100_000_000,
    bool IgnoreCrashes = true,
    bool IgnoreTimeouts = true);

/// <summary>Monkey 运行结果。</summary>
public sealed record MonkeyRunResult(
    AdbCommandResult Command,
    TimeSpan Elapsed,
    bool StoppedByDuration,
    int CrashCount,
    int AnrCount);

/// <summary>控制台快捷指令（点击后填入命令行）。</summary>
/// <param name="Line">要填入的 adb 参数行；Kind 为特殊类型（如 logcat-filter）时可为空。</param>
public sealed record ConsoleChipDefinition(
    string Id,
    string Title,
    string? Line = null,
    string? Kind = null,
    bool RequiresRoot = false,
    bool RequiresUserdebug = false,
    bool IsDangerous = false);
