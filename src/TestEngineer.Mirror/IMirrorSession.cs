using TestEngineer.Core.Models;

namespace TestEngineer.Mirror;

public enum MirrorSessionState
{
    Stopped = 0,
    Starting,
    Running,
    Failed,
}

/// <summary>一次投屏会话的生命周期抽象（方案 A：启动 scrcpy 并暴露 HWND 供嵌入）。</summary>
public interface IMirrorSession : IAsyncDisposable
{
    MirrorSessionState State { get; }

    /// <summary>scrcpy 主窗口句柄；未就绪时为 <see cref="IntPtr.Zero"/>。</summary>
    IntPtr WindowHandle { get; }

    string? LastError { get; }

    event EventHandler? StateChanged;

    /// <summary>启动针对指定设备的 scrcpy 会话。</summary>
    Task StartAsync(AndroidDevice device, MirrorOptions? options = null, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task SendKeyAsync(string keycode, CancellationToken cancellationToken = default);
}

public sealed class MirrorOptions
{
    public int MaxSize { get; init; } = 1024;

    public int BitRate { get; init; } = 8_000_000;

    public int MaxFps { get; init; } = 60;

    public string? RecordPath { get; init; }

    public bool StayAwake { get; init; } = true;

    public bool TurnScreenOff { get; init; }

    /// <summary>为 true 时 scrcpy 以无边框窗口启动，便于 SetParent 嵌入。</summary>
    public bool Borderless { get; init; } = true;
}
