using TestEngineer.Adb.Parsing;

namespace TestEngineer.Adb.Tests;

public sealed class DeviceOutputParsersTests
{
    [Theory]
    [InlineData("H29T", "H29T", "H29T", "user", "0", "13", "33", "2023-08-05")]
    [InlineData("DSJ", "Hisense", "DSJ-TOPX1A1", "user", "0", "13", "33", "2023-08-05")]
    public void GetProp_RealDevices(string device, string manufacturer, string model, string buildType, string debuggable, string release, string sdk, string patch)
    {
        var props = GetPropParser.Parse(Fixture.Read($"{device}-getprop.txt"));

        Assert.Equal(manufacturer, GetPropParser.Get(props, "ro.product.manufacturer"));
        Assert.Equal(model, GetPropParser.Get(props, "ro.product.model"));
        Assert.Equal(buildType, GetPropParser.Get(props, "ro.build.type"));
        Assert.Equal(debuggable, GetPropParser.Get(props, "ro.debuggable"));
        Assert.Equal(release, GetPropParser.Get(props, "ro.build.version.release"));
        Assert.Equal(sdk, GetPropParser.Get(props, "ro.build.version.sdk"));
        Assert.Equal(patch, GetPropParser.Get(props, "ro.build.version.security_patch"));
        Assert.Null(GetPropParser.Get(props, "ro.does.not.exist"));
    }

    [Fact]
    public void Battery_RealDevice()
    {
        var battery = BatteryParser.Parse(Fixture.Read("H29T-battery.txt"));

        Assert.Equal(89, battery.Level);
        Assert.Equal("充电中", battery.Status);
        Assert.True(battery.IsCharging);
        Assert.Equal(29.9, battery.TemperatureC);
        Assert.Equal(4176, battery.VoltageMv);
        Assert.Equal("USB", battery.PowerSource);
    }

    [Fact]
    public void Df_RealDevices_ConvertsKilobytesToBytes()
    {
        var h29t = DfParser.Parse(Fixture.Read("H29T-df-data.txt"));
        var dsj = DfParser.Parse(Fixture.Read("DSJ-df-data.txt"));

        Assert.NotNull(h29t);
        Assert.Equal(235226112L * 1024, h29t.TotalBytes);
        Assert.Equal(3890016L * 1024, h29t.UsedBytes);
        Assert.Equal(231205024L * 1024, h29t.FreeBytes);
        Assert.Equal(6329344L * 1024, dsj?.TotalBytes);
        Assert.Null(DfParser.Parse("df: /data: Permission denied"));
    }

    [Fact]
    public void MemInfo_RealDevice()
    {
        var memory = MemInfoParser.Parse(Fixture.Read("H29T-meminfo.txt"));

        Assert.NotNull(memory);
        Assert.True(memory.TotalBytes > 1L * 1024 * 1024 * 1024);
        Assert.True(memory.AvailableBytes > 0);
        Assert.True(memory.AvailableBytes <= memory.TotalBytes);
    }

    [Fact]
    public void Wm_RealDevices()
    {
        Assert.Equal((720, 1440), WmParser.ParseSize(Fixture.Read("H29T-wm-size.txt")));
        Assert.Equal(320, WmParser.ParseDensity(Fixture.Read("H29T-wm-density.txt")));
        Assert.Equal((480, 640), WmParser.ParseSize(Fixture.Read("DSJ-wm-size.txt")));
        Assert.Equal(240, WmParser.ParseDensity(Fixture.Read("DSJ-wm-density.txt")));
    }

    [Fact]
    public void Wm_OverrideWinsOverPhysical()
    {
        Assert.Equal((1080, 1920), WmParser.ParseSize("Physical size: 720x1440\nOverride size: 1080x1920\n"));
        Assert.Equal(480, WmParser.ParseDensity("Physical density: 320\nOverride density: 480\n"));
        Assert.Null(WmParser.ParseSize("garbage"));
    }

    [Fact]
    public void Wm_ParseRotationAndApply()
    {
        Assert.Equal(1, WmParser.ParseRotation("mCurrentRotation=1"));
        Assert.Equal(3, WmParser.ParseRotation("mDisplayRotation: ROTATION_3"));
        Assert.Equal(0, WmParser.ParseRotation("SurfaceOrientation: 0"));
        Assert.Null(WmParser.ParseRotation("no rotation here"));

        Assert.Equal((1440, 720), WmParser.ApplyRotation((720, 1440), 1));
        Assert.Equal((720, 1440), WmParser.ApplyRotation((720, 1440), 0));
        Assert.Equal((1440, 720), WmParser.ApplyRotation((720, 1440), 3));
    }

    [Fact]
    public void CpuInfo_RealDevice_CountsCoresAndReadsHardware()
    {
        var cpu = CpuInfoParser.Parse(Fixture.Read("DSJ-cpuinfo.txt"));

        Assert.Equal(8, cpu.Cores);
        Assert.Equal("Unisoc T760", cpu.Model);
    }

    [Fact]
    public void IpAddr_RealDevice_FindsWlanMacWithoutIp()
    {
        var interfaces = IpAddrParser.Parse(Fixture.Read("H29T-ip-addr.txt"));

        var wlan = Assert.Single(interfaces, i => i.Name == "wlan0");
        Assert.Equal("d4:f9:21:6d:66:4e", wlan.Mac);
        Assert.Null(wlan.Ipv4); // Wi-Fi 关闭，仅有 IPv6 链路地址。

        var loopback = Assert.Single(interfaces, i => i.Name == "lo");
        Assert.Equal("127.0.0.1", loopback.Ipv4);

        // 带 @NONE 后缀的接口名要被正确截断。
        Assert.Contains(interfaces, i => i.Name == "tunl0");
        Assert.Equal("wlan0", IpAddrParser.PickPrimary(interfaces)?.Name);
    }

    [Fact]
    public void IpAddr_PrefersWlanIpv4()
    {
        const string output = """
            2: wlan0: <BROADCAST,MULTICAST,UP,LOWER_UP> mtu 1500 qdisc mq state UP
                link/ether 5e:12:34:56:78:9a brd ff:ff:ff:ff:ff:ff
                inet 192.168.1.23/24 brd 192.168.1.255 scope global wlan0
            """;

        var primary = IpAddrParser.PickPrimary(IpAddrParser.Parse(output));

        Assert.Equal("192.168.1.23", primary?.Ipv4);
        Assert.Equal("5e:12:34:56:78:9a", primary?.Mac);
    }

    [Fact]
    public void WifiInfo_ConnectedSynthetic()
    {
        var info = WifiInfoParser.Parse(Fixture.Read("wifi-connected-synthetic.txt"));

        Assert.NotNull(info);
        Assert.Equal("Office-5G", info.Ssid);
        Assert.Equal("5e:12:34:56:78:9a", info.Mac);
        Assert.Equal("192.168.1.23", info.Ip);
        Assert.True(info.Connected);
    }

    [Fact]
    public void WifiInfo_DisconnectedRealDevice_HasPlaceholderMacAndNoSsid()
    {
        var info = WifiInfoParser.Parse(Fixture.Read("wifi-disconnected-DSJ.txt"));

        Assert.NotNull(info);
        Assert.Null(info.Ssid);
        Assert.Null(info.Ip);
        Assert.Equal(WifiInfoParser.PlaceholderMac, info.Mac);
        Assert.False(info.Connected);
        Assert.Null(WifiInfoParser.Parse(""));
    }

    [Theory]
    [InlineData("H29T-connectivity.txt")]
    [InlineData("DSJ-connectivity.txt")]
    public void Connectivity_RealDevicesWithoutNetwork_ReturnNull(string fixture)
    {
        Assert.Null(ConnectivityParser.ParseActiveNetworkType(Fixture.Read(fixture)));
    }

    [Fact]
    public void Connectivity_ActiveWifiNetwork()
    {
        const string output = """
            Active default network: 100
            NetworkAgentInfo{ ni{[type: WIFI[], state: CONNECTED/CONNECTED, reason: (unspecified), extra: "x", failover: false, available: true, roaming: false, metered: false]}  network{100}  nethandle{1} }
            """;

        Assert.Equal("WIFI", ConnectivityParser.ParseActiveNetworkType(output));
    }

    [Fact]
    public void PackageList_RealDevice_SplitsPathAndName()
    {
        var packages = PackageListParser.Parse(Fixture.Read("H29T-pm-3.txt"), isSystem: false);

        Assert.Equal(12, packages.Count);
        var litedo = Assert.Single(packages, p => p.PackageName == "com.litedo");
        Assert.EndsWith("/base.apk", litedo.ApkPath, StringComparison.Ordinal);
        Assert.StartsWith("/data/app/", litedo.ApkPath, StringComparison.Ordinal);
        Assert.All(packages, p => Assert.False(p.IsSystem));
        Assert.Contains(packages, p => p.PackageName == "com.android.soundrecorder" && p.ApkPath.StartsWith("/system/preload/", StringComparison.Ordinal));
    }

    [Fact]
    public void PackageDump_Synthetic_ParsesVersionFlagsAndPermissions()
    {
        var detail = PackageDumpParser.Parse("com.example.demo", Fixture.Read("dumpsys-package-synthetic.txt"));

        Assert.Equal("1.2.3", detail.VersionName);
        Assert.Equal(42, detail.VersionCode);
        Assert.Equal(24, detail.MinSdk);
        Assert.Equal(33, detail.TargetSdk);
        Assert.Equal("10150", detail.UserId);
        Assert.True(detail.IsDebuggable);
        Assert.False(detail.IsSystem);
        Assert.Equal("2026-11-09 13:32:22", detail.FirstInstallTime);

        var byName = detail.Permissions.ToDictionary(p => p.Name);
        Assert.Equal(4, byName.Count);
        Assert.True(byName["android.permission.INTERNET"].Granted);
        Assert.False(byName["android.permission.INTERNET"].IsRuntime);
        Assert.True(byName["android.permission.CAMERA"].Granted);
        Assert.True(byName["android.permission.CAMERA"].IsRuntime);
        Assert.False(byName["android.permission.ACCESS_FINE_LOCATION"].Granted);
        Assert.Null(byName["com.example.demo.permission.PRIVATE"].Granted);
    }
}

public sealed class ForegroundComponentParserTests
{
    [Fact]
    public void Format_PrefersResumedActivityAndExpandsRelativeClass()
    {
        const string output = """
            mCurrentFocus=Window{a u0 com.android.systemui/com.android.systemui.wallpapers.ImageWallpaper}
            mResumedActivity: ActivityRecord{53d3a48 u0 com.android.settings/.Settings t92}
            """;

        var text = ForegroundComponentParser.Format(output);

        Assert.Equal($"包名  com.android.settings{Environment.NewLine}组件  com.android.settings.Settings", text);
    }

    [Fact]
    public void Format_UsesFocusWhenResumeMissing()
    {
        const string output = "mCurrentFocus=Window{abc u0 com.example.app/com.example.app.MainActivity}";

        var text = ForegroundComponentParser.Format(output);

        Assert.Contains("com.example.app.MainActivity", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_Empty_SaysNotFound()
    {
        Assert.Equal("未找到前台 Activity", ForegroundComponentParser.Format("no activity here"));
    }
}
