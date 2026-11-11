using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Settings;

namespace TestEngineer.Infrastructure.Settings;

/// <summary>应用启动时加载一次、全程共享的设置实例，<see cref="Save"/> 写回磁盘。</summary>
public sealed class AppSettingsService : IAppSettings
{
    private readonly ISettingsStore _store;
    private readonly object _gate = new();

    public AppSettingsService(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Current = store.Load();
    }

    public AppSettings Current { get; }

    public event EventHandler? Changed;

    public void Save()
    {
        lock (_gate)
        {
            _store.Save(Current);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
