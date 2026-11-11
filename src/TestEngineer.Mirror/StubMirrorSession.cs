using TestEngineer.Core.Models;

namespace TestEngineer.Mirror;

/// <summary>无 scrcpy 时的占位实现，便于 UI 在无设备环境下仍可编译运行。</summary>
public sealed class StubMirrorSession : IMirrorSession
{
    public MirrorSessionState State { get; private set; } = MirrorSessionState.Stopped;

    public IntPtr WindowHandle => IntPtr.Zero;

    public string? LastError { get; private set; }

    public event EventHandler? StateChanged;

    public Task StartAsync(AndroidDevice device, MirrorOptions? options = null, CancellationToken cancellationToken = default)
    {
        State = MirrorSessionState.Failed;
        LastError = "StubMirrorSession：未接入真实 scrcpy。";
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        State = MirrorSessionState.Stopped;
        LastError = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task SendKeyAsync(string keycode, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
