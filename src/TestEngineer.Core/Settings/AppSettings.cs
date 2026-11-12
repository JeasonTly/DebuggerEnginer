using TestEngineer.Core.Apk;

namespace TestEngineer.Core.Settings;

public enum AppThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>外部工具路径覆盖。为空表示按 随包 → PATH 自动查找。</summary>
public sealed class ToolPathSettings
{
    public string? Adb { get; set; }

    public string? Scrcpy { get; set; }
}

/// <summary>settings.json 的内存形态，直接用于 JSON 序列化。</summary>
public sealed class AppSettings
{
    /// <summary>当前代码支持的最新设置版本。</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public AppThemeMode Theme { get; set; } = AppThemeMode.System;

    public bool NavCollapsed { get; set; }

    /// <summary>投屏最大宽度（像素），0 表示不限制。</summary>
    public int MirrorWidth { get; set; } = 1024;

    public ToolPathSettings ToolPaths { get; set; } = new();

    /// <summary>控制台历史上限条数。</summary>
    public int ConsoleHistoryLimit { get; set; } = 500;

    /// <summary>上次选中的设备序列号，启动时优先恢复。</summary>
    public string? LastDeviceSerial { get; set; }

    /// <summary>重新签名用过的签名文件，最近使用的在前。密码字段是本机保护后的内容。</summary>
    public List<SigningProfile> SigningProfiles { get; set; } = [];
}
