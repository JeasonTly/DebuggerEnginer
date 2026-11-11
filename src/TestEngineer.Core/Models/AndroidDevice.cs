namespace TestEngineer.Core.Models;

/// <summary>设备连接状态（对应 <c>adb devices</c> 的状态列）。</summary>
public enum DeviceState
{
    Unknown = 0,
    Device,
    Offline,
    Unauthorized,
    Authorizing,
    Connecting,
    Recovery,
    Sideload,
    Bootloader,
    NoPermissions,
}

/// <summary>一台由 adb 枚举到的设备。</summary>
/// <param name="Serial">序列号。</param>
/// <param name="State">归一化后的状态。</param>
/// <param name="RawState">adb 原始状态文本（如 "device"、"unauthorized"）。</param>
/// <param name="Model"><c>model:</c> 字段（adb 会把 '-' 替换为 '_'）。</param>
/// <param name="Product"><c>product:</c> 字段。</param>
/// <param name="DeviceName"><c>device:</c> 字段。</param>
/// <param name="TransportId"><c>transport_id:</c> 字段。</param>
/// <param name="Usb"><c>usb:</c> 字段。</param>
public sealed record AndroidDevice(
    string Serial,
    DeviceState State,
    string RawState,
    string? Model = null,
    string? Product = null,
    string? DeviceName = null,
    string? TransportId = null,
    string? Usb = null)
{
    /// <summary>是否处于可执行命令的 device 状态。</summary>
    public bool IsOnline => State == DeviceState.Device;

    /// <summary>用于列表展示的名称：型号优先，其次产品名，最后序列号。</summary>
    public string DisplayName
    {
        get
        {
            var name = !string.IsNullOrWhiteSpace(Model) ? Model.Replace('_', '-') : Product;
            return string.IsNullOrWhiteSpace(name) ? Serial : $"{name} ({Serial})";
        }
    }

    /// <summary>把 adb 状态文本映射为枚举。</summary>
    public static DeviceState ParseState(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "device" => DeviceState.Device,
        "offline" => DeviceState.Offline,
        "unauthorized" => DeviceState.Unauthorized,
        "authorizing" => DeviceState.Authorizing,
        "connecting" => DeviceState.Connecting,
        "recovery" => DeviceState.Recovery,
        "sideload" or "rescue" => DeviceState.Sideload,
        "bootloader" => DeviceState.Bootloader,
        "no permissions" => DeviceState.NoPermissions,
        _ => DeviceState.Unknown,
    };
}
