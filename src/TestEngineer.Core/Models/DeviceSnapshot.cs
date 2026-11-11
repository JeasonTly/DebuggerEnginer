namespace TestEngineer.Core.Models;

/// <summary>
/// 设备信息快照。任一字段都可能因 user 版本权限限制而缺失（null / 0），
/// 采集过程中的非致命问题记录在 <see cref="Warnings"/>。
/// </summary>
public sealed record DeviceSnapshot
{
    public required string Serial { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    // 基本信息
    public string? Manufacturer { get; init; }

    public string? Brand { get; init; }

    public string? Model { get; init; }

    public string? AndroidVersion { get; init; }

    public int SdkLevel { get; init; }

    public string? BuildType { get; init; }

    public string? BuildDisplayId { get; init; }

    public bool? Debuggable { get; init; }

    public string? SecurityPatch { get; init; }

    public string? Kernel { get; init; }

    public string? Baseband { get; init; }

    // 电池
    public int? BatteryLevel { get; init; }

    public string? BatteryStatus { get; init; }

    public bool? IsCharging { get; init; }

    /// <summary>电池温度（摄氏度）。</summary>
    public double? BatteryTemperatureC { get; init; }

    public int? BatteryVoltageMv { get; init; }

    public string? PowerSource { get; init; }

    // 存储（/data，字节）
    public long StorageTotalBytes { get; init; }

    public long StorageUsedBytes { get; init; }

    public long StorageFreeBytes { get; init; }

    // 内存（字节）
    public long MemoryTotalBytes { get; init; }

    public long MemoryAvailableBytes { get; init; }

    // CPU
    public string? CpuModel { get; init; }

    public int CpuCores { get; init; }

    public string? CpuAbi { get; init; }

    // 屏幕
    public int ScreenWidth { get; init; }

    public int ScreenHeight { get; init; }

    public int Density { get; init; }

    // 网络
    public string? NetworkType { get; init; }

    public string? WifiSsid { get; init; }

    public string? WifiMac { get; init; }

    public string? IpAddress { get; init; }

    // 运行时间
    public long UptimeSec { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public string Resolution => ScreenWidth > 0 && ScreenHeight > 0 ? $"{ScreenWidth}x{ScreenHeight}" : "-";
}
