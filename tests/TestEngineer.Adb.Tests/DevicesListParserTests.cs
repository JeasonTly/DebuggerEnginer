using TestEngineer.Adb.Parsing;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb.Tests;

public sealed class DevicesListParserTests
{
    [Fact]
    public void Parse_RealOutputFromTwoDevices_ReturnsBothWithAttributes()
    {
        var devices = DevicesListParser.Parse(Fixture.Read("devices-l.txt"));

        Assert.Equal(2, devices.Count);

        var h29t = devices[0];
        Assert.Equal("99990000888800", h29t.Serial);
        Assert.Equal(DeviceState.Device, h29t.State);
        Assert.True(h29t.IsOnline);
        Assert.Equal("H29T", h29t.Model);
        Assert.Equal("H29T", h29t.Product);
        Assert.Equal("H29T", h29t.DeviceName);
        Assert.Equal("1", h29t.TransportId);

        var dsj = devices[1];
        Assert.Equal("KKSTBE045026", dsj.Serial);
        Assert.Equal("DSJ_TOPX1A1", dsj.Model);
        Assert.Equal("HIS60A1", dsj.Product);
        Assert.Equal("2", dsj.TransportId);
        Assert.Equal("DSJ-TOPX1A1 (KKSTBE045026)", dsj.DisplayName);
    }

    [Fact]
    public void Parse_SkipsDaemonNoiseAndHandlesAllStates()
    {
        var devices = DevicesListParser.Parse(Fixture.Read("devices-l-edge.txt"));

        Assert.Equal(4, devices.Count);
        Assert.Equal("emulator-5554", devices[0].Serial);
        Assert.Equal("sdk_gphone64_x86_64", devices[0].Model);

        Assert.Equal(DeviceState.Unauthorized, devices[1].State);
        Assert.False(devices[1].IsOnline);
        Assert.Equal("1-4", devices[1].Usb);
        Assert.Null(devices[1].Model);

        Assert.Equal("192.168.1.20:5555", devices[2].Serial);
        Assert.Equal(DeviceState.Offline, devices[2].State);
        Assert.Equal("5", devices[2].TransportId);

        // "no permissions" 状态含空格与 URL 中的冒号，不能被当作属性。
        Assert.Equal(DeviceState.NoPermissions, devices[3].State);
        Assert.Equal("no permissions", devices[3].RawState);
        Assert.Equal("6", devices[3].TransportId);
        Assert.Equal("1-2", devices[3].Usb);
    }

    [Theory]
    [InlineData("")]
    [InlineData("List of devices attached\n\n")]
    [InlineData("List of devices attached\r\n\r\n")]
    public void Parse_NoDevices_ReturnsEmpty(string output)
    {
        Assert.Empty(DevicesListParser.Parse(output));
    }

    [Fact]
    public void Parse_CrLfLineEndings_AreHandled()
    {
        var output = "List of devices attached\r\nABC123\tdevice product:p model:m device:d transport_id:9\r\n\r\n";

        var device = Assert.Single(DevicesListParser.Parse(output));

        Assert.Equal("ABC123", device.Serial);
        Assert.Equal("9", device.TransportId);
    }

    [Fact]
    public void Parse_TrackDevicesStyleTabSeparated_ReturnsStateOnly()
    {
        var device = Assert.Single(DevicesListParser.Parse("KKSTBE045026\tdevice\n"));

        Assert.Equal(DeviceState.Device, device.State);
        Assert.Null(device.Model);
    }
}
