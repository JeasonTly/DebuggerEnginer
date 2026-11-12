using TestEngineer.Core.Models;

namespace TestEngineer.Mirror;

public enum MirrorSessionState
{
    Stopped = 0,
    Starting,
    Running,
    Failed,
}

/// <summary>scrcpy 解码出的画面尺寸（来自官方日志 <c>Texture: WxH</c>）。</summary>
public readonly record struct MirrorVideoSize(int Width, int Height)
{
    public double AspectRatio => Height > 0 ? (double)Width / Height : 0;
}

/// <summary>一次投屏会话的生命周期抽象（方案 A：启动 scrcpy 并暴露 HWND 供嵌入）。</summary>
public interface IMirrorSession : IAsyncDisposable
{
    MirrorSessionState State { get; }

    /// <summary>scrcpy 主窗口句柄；未就绪时为 <see cref="IntPtr.Zero"/>。</summary>
    IntPtr WindowHandle { get; }

    string? LastError { get; }

    /// <summary>最近一次解析到的视频尺寸（旋转后会更新）。</summary>
    MirrorVideoSize? VideoSize { get; }

    event EventHandler? StateChanged;

    /// <summary>scrcpy 输出 <c>Texture: WxH</c> 时触发（含横竖屏切换）。</summary>
    event EventHandler<MirrorVideoSize>? VideoSizeChanged;

    /// <summary>启动针对指定设备的 scrcpy 会话。</summary>
    Task StartAsync(AndroidDevice device, MirrorOptions? options = null, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>进程退出前同步结束本会话拉起的 scrcpy，不再回到界面线程。</summary>
    void ForceStop();

    Task SendKeyAsync(string keycode, CancellationToken cancellationToken = default);
}

public sealed class MirrorOptions
{
    /// <summary>scrcpy --max-size；0 表示不限制（按设备分辨率）。</summary>
    public int MaxSize { get; init; }

    public int BitRate { get; init; } = 8_000_000;

    public int MaxFps { get; init; } = 30;

    public string? RecordPath { get; init; }

    public bool StayAwake { get; init; } = true;

    public bool TurnScreenOff { get; init; }

    /// <summary>为 true 时 scrcpy 以无边框窗口启动，便于 SetParent 嵌入。</summary>
    public bool Borderless { get; init; } = true;

    /// <summary>关闭音频转发，降低嵌入时卡顿。</summary>
    public bool NoAudio { get; init; } = true;

    /// <summary>scrcpy --video-buffer（毫秒）；略缓冲可减少卡顿。</summary>
    public int VideoBufferMs { get; init; } = 50;

    /// <summary>嵌入区初始窗口宽（逻辑像素）；&gt;0 时传给 --window-width。</summary>
    public int WindowWidth { get; init; }

    /// <summary>嵌入区初始窗口高（逻辑像素）；&gt;0 时传给 --window-height。</summary>
    public int WindowHeight { get; init; }
}
