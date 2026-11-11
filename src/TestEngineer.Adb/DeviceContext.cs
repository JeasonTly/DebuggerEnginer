using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb;

/// <summary>
/// 包装 <see cref="IDeviceTracker"/> 的当前设备上下文。选择会写入 <see cref="IAppSettings"/>（LastDeviceSerial），
/// 下次启动或设备重新接入时自动恢复。设备断开后 <see cref="Current"/> 置空；
/// 此后若上次选择的设备重新上线，或恰好只有一台在线设备，则自动选中。
/// </summary>
public sealed class DeviceContext : IDeviceContext, IDisposable
{
    private readonly IDeviceTracker _tracker;
    private readonly IAppSettings? _settings;
    private readonly object _gate = new();
    private AndroidDevice? _current;
    private bool _disposed;

    public DeviceContext(IDeviceTracker tracker, IAppSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        _tracker = tracker;
        _settings = settings;
        _tracker.DevicesChanged += OnTrackerDevicesChanged;
    }

    public event EventHandler? CurrentChanged;

    public event EventHandler? DevicesChanged;

    public AndroidDevice? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public IReadOnlyList<AndroidDevice> Devices => _tracker.Devices;

    public bool Select(string? serial)
    {
        AndroidDevice? target = null;
        if (serial is not null)
        {
            target = _tracker.Devices.FirstOrDefault(d => d.Serial == serial);
            if (target is null)
            {
                return false;
            }
        }

        SetCurrent(target);
        PersistSelection(serial);
        return true;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _tracker.StartAsync(cancellationToken).ConfigureAwait(false);
        Reconcile(_tracker.Devices);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var devices = await _tracker.RefreshAsync(cancellationToken).ConfigureAwait(false);
        Reconcile(devices);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tracker.DevicesChanged -= OnTrackerDevicesChanged;
    }

    private void OnTrackerDevicesChanged(object? sender, DevicesChangedEventArgs e)
    {
        Reconcile(e.Devices);
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Reconcile(IReadOnlyList<AndroidDevice> devices)
    {
        AndroidDevice? next;
        lock (_gate)
        {
            var current = _current;
            if (current is not null)
            {
                // 刷新当前设备对象（状态可能变化）；断开则置空。
                next = devices.FirstOrDefault(d => d.Serial == current.Serial);
            }
            else
            {
                var preferred = _settings?.Current.LastDeviceSerial;
                var online = devices.Where(d => d.IsOnline).ToList();
                // 有上次选择优先；否则选第一台在线设备（多机时也不留空，避免「在线但未选择」）。
                next = online.FirstOrDefault(d => d.Serial == preferred)
                    ?? online.FirstOrDefault();
            }
        }

        SetCurrent(next);
    }

    private bool SetCurrent(AndroidDevice? device)
    {
        lock (_gate)
        {
            if (Equals(_current, device))
            {
                return false;
            }

            _current = device;
        }

        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void PersistSelection(string? serial)
    {
        if (_settings is null || serial is null || _settings.Current.LastDeviceSerial == serial)
        {
            return;
        }

        _settings.Current.LastDeviceSerial = serial;
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 持久化失败不影响本次选择。
        }
    }
}
