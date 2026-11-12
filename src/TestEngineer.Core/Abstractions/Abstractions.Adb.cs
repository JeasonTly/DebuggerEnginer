using TestEngineer.Core.Models;

namespace TestEngineer.Core.Abstractions;

/// <summary>
/// 执行 adb 命令的统一入口。参数始终以列表传递（ArgumentList），禁止拼接命令行字符串。
/// </summary>
public interface IAdbProcessRunner
{
    /// <param name="arguments">adb 子命令及参数（不含 adb 可执行文件本身与 -s 选项）。</param>
    /// <param name="serial">目标设备序列号；为 null 时不附加 -s。</param>
    /// <param name="timeout">超时；null 使用实现默认值，<see cref="Timeout.InfiniteTimeSpan"/> 表示不超时。</param>
    /// <param name="onStdout">stdout 流式回调（按行；不完整的末行在短暂空闲后也会送出）。</param>
    /// <param name="onStderr">stderr 流式回调。</param>
    Task<AdbCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? serial = null,
        TimeSpan? timeout = null,
        Action<string>? onStdout = null,
        Action<string>? onStderr = null,
        CancellationToken cancellationToken = default);
}

/// <summary>定位外部工具（adb、scrcpy、apksigner）。</summary>
public interface IToolLocator
{
    /// <summary>找到 adb.exe 的完整路径；未找到返回 null。</summary>
    string? FindAdb();

    /// <summary>找到 scrcpy.exe 的完整路径；未找到返回 null。</summary>
    string? FindScrcpy();

    /// <summary>找到 apksigner.bat 的完整路径；未找到返回 null。</summary>
    string? FindApksigner();
}

/// <summary>设备列表变化通知参数。</summary>
public sealed class DevicesChangedEventArgs(IReadOnlyList<AndroidDevice> devices) : EventArgs
{
    public IReadOnlyList<AndroidDevice> Devices { get; } = devices;
}

/// <summary>设备枚举与热插拔跟踪。事件可能在后台线程触发。</summary>
public interface IDeviceTracker : IAsyncDisposable
{
    IReadOnlyList<AndroidDevice> Devices { get; }

    /// <summary>当前使用的跟踪方式描述（track-devices / polling）。</summary>
    string Mode { get; }

    /// <summary>最近一次错误（如未找到 adb）；正常为 null。</summary>
    string? LastError { get; }

    event EventHandler<DevicesChangedEventArgs>? DevicesChanged;

    /// <summary>启动后台跟踪；完成首次枚举后返回。重复调用无副作用。</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();

    /// <summary>立即重新枚举一次。</summary>
    Task<IReadOnlyList<AndroidDevice>> RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>当前设备上下文：所有页面通过它获取目标设备。事件可能在后台线程触发。</summary>
public interface IDeviceContext
{
    AndroidDevice? Current { get; }

    IReadOnlyList<AndroidDevice> Devices { get; }

    /// <summary>当前设备变化（切换、断开、状态变化）。</summary>
    event EventHandler? CurrentChanged;

    event EventHandler? DevicesChanged;

    /// <summary>切换当前设备；serial 为 null 清除选择。返回是否成功（设备必须存在）。</summary>
    bool Select(string? serial);

    Task StartAsync(CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
