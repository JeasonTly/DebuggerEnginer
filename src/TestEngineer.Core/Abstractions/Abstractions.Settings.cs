using TestEngineer.Core.Settings;

namespace TestEngineer.Core.Abstractions;

/// <summary>设置持久化抽象。</summary>
public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>运行期共享的应用设置：读取 <see cref="Current"/>，修改后调用 <see cref="Save"/>。</summary>
public interface IAppSettings
{
    AppSettings Current { get; }

    /// <summary>持久化当前设置并触发 <see cref="Changed"/>。</summary>
    void Save();

    event EventHandler? Changed;
}

/// <summary>控制台命令历史。</summary>
public interface IConsoleHistory
{
    /// <summary>从旧到新。</summary>
    IReadOnlyList<string> Items { get; }

    void Add(string commandLine);

    void Clear();
}
