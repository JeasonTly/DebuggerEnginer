using TestEngineer.Adb.Parsing;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Models;

namespace TestEngineer.Adb.Tests;

public sealed class DeviceHelperOutputParserTests
{
    [Fact]
    public void Parse_ReadsLabelVersionFlagsAndIcon()
    {
        const string output = """
            TE_HELPER_OK
            TE1	com.litedo	LiteDo	V1.8.6	86	0	1	aGVsbG8=
            TE1	com.android.settings	设置	14	1	1	1	-
            TE_HELPER_DONE
            """;

        Assert.True(DeviceHelperOutputParser.LooksSuccessful(output));
        var map = DeviceHelperOutputParser.Parse(output);

        Assert.Equal(2, map.Count);
        var lite = map["com.litedo"];
        Assert.Equal("LiteDo", lite.Label);
        Assert.Equal("V1.8.6", lite.VersionName);
        Assert.Equal(86, lite.VersionCode);
        Assert.False(lite.IsSystem);
        Assert.True(lite.HasLauncher);
        Assert.Equal(Convert.FromBase64String("aGVsbG8="), lite.IconData);

        var settings = map["com.android.settings"];
        Assert.Equal("设置", settings.Label);
        Assert.True(settings.IsSystem);
        Assert.Null(settings.IconData);
    }

    [Fact]
    public void Merge_EnrichesMatchingPackages()
    {
        var packages = new[]
        {
            new AppPackage("com.litedo", "/data/app/base.apk", IsSystem: false),
            new AppPackage("com.missing", "/data/app/x.apk", IsSystem: false),
        };
        var helper = DeviceHelperOutputParser.Parse("""
            TE_HELPER_OK
            TE1	com.litedo	LiteDo	V1	1	0	1	aGVsbG8=
            TE_HELPER_DONE
            """);

        var merged = DeviceHelperOutputParser.Merge(packages, helper);
        Assert.Equal("LiteDo", merged[0].DisplayName);
        Assert.True(merged[0].HasIcon);
        Assert.Equal("missing", merged[1].DisplayName);
        Assert.False(merged[1].HasIcon);
    }
}

public sealed class AppPackageModelTests
{
    [Theory]
    [InlineData("com.android.chrome", "chrome")]
    [InlineData("single", "single")]
    [InlineData("com.example.", "com.example.")]
    public void FallbackLabel_UsesLastSegment(string packageName, string expected)
        => Assert.Equal(expected, AppPackage.FallbackLabel(packageName));

    [Fact]
    public void DisplayName_PrefersLabel_ThenFallback()
    {
        var withLabel = new AppPackage("com.litedo", "/a.apk", false, Label: "LiteDo");
        Assert.Equal("LiteDo", withLabel.DisplayName);
        Assert.Equal("L", withLabel.Initial);

        var fallback = new AppPackage("com.litedo", "/a.apk", false);
        Assert.Equal("litedo", fallback.DisplayName);
        Assert.Equal("L", fallback.Initial);
        Assert.Equal("第三方", fallback.KindLabel);
        Assert.Equal("系统", new AppPackage("com.android.settings", "/s.apk", true).KindLabel);
    }
}

public sealed class AppManagerEnrichmentTests
{
    [Fact]
    public async Task ListAsync_WhenHelperFails_ReturnsPmListWithoutIcons()
    {
        var jar = Path.Combine(Path.GetTempPath(), "te-helper-miss-" + Guid.NewGuid().ToString("N") + ".jar");
        await File.WriteAllBytesAsync(jar, [1]);
        try
        {
            var runner = new FakeAdbRunner()
                .WhenShellContains(
                    "pm list packages -f -3",
                    "package:/data/app/base.apk=com.litedo\n")
                .WhenShellContains("pm list packages -f -s", "")
                .WhenShellContains("query-activities", "")
                .When(args => args.Count >= 1 && args[0] == "push", _ => new AdbCommandResult(1, "", "push failed"));

            var service = new AppManagerService(runner, new DeviceHelperClient(runner, () => jar));
            var list = await service.ListAsync("S1", AppListFilter.ThirdParty);

            var pkg = Assert.Single(list);
            Assert.Equal("com.litedo", pkg.PackageName);
            Assert.False(pkg.HasIcon);
            Assert.Equal("litedo", pkg.DisplayName);
        }
        finally
        {
            File.Delete(jar);
        }
    }

    [Fact]
    public async Task ListAsync_HasIconFilter_OnlyReturnsLauncherPackages()
    {
        var helperStdout = """
            TE_HELPER_OK
            TE1	com.litedo	LiteDo	V1	1	0	1	aGVsbG8=
            TE1	com.noscreen	NoScreen	V1	1	0	0	-
            TE_HELPER_DONE
            """;

        var jar = Path.Combine(Path.GetTempPath(), "te-helper-test-" + Guid.NewGuid().ToString("N") + ".jar");
        await File.WriteAllBytesAsync(jar, [0x50, 0x4B]); // minimal non-empty
        try
        {
            var runner = new FakeAdbRunner()
                .WhenShellContains(
                    "pm list packages -f -3",
                    "package:/a.apk=com.litedo\npackage:/b.apk=com.noscreen\n")
                .WhenShellContains(
                    "pm list packages -f -s",
                    "")
                .When(args => args.Count >= 1 && args[0] == "push", _ => new AdbCommandResult(0, "pushed", ""))
                .WhenShellContains("app_process", helperStdout);

            var service = new AppManagerService(runner, new DeviceHelperClient(runner, () => jar));
            var list = await service.ListAsync("S1", AppListFilter.HasIcon);

            var pkg = Assert.Single(list);
            Assert.Equal("com.litedo", pkg.PackageName);
            Assert.True(pkg.HasLauncher);
            Assert.True(pkg.HasIcon);
            Assert.Equal("LiteDo", pkg.Label);
        }
        finally
        {
            File.Delete(jar);
        }
    }
}
