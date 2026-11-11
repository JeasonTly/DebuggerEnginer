using TestEngineer.Adb.Parsing;

namespace TestEngineer.Adb.Tests;

public sealed class LsOutputParserTests
{
    [Fact]
    public void Parse_RealSdcardListing_IncludesChineseDirectory()
    {
        var entries = LsOutputParser.Parse(Fixture.Read("H29T-ls-sdcard.txt"), "/sdcard");

        Assert.Equal(19, entries.Count);
        Assert.DoesNotContain(entries, e => e.Name.StartsWith("total"));

        var camera = Assert.Single(entries, e => e.Name == "相机");
        Assert.True(camera.IsDirectory);
        Assert.Equal("/sdcard/相机", camera.AbsolutePath);
        Assert.Equal("drwxrws---", camera.Mode);
        Assert.Equal("u0_a100", camera.Owner);
        Assert.Equal("media_rw", camera.Group);
        Assert.Equal(3452, camera.Size);
        Assert.Equal(new DateTime(2026, 11, 3, 3, 46, 0), camera.MTime);

        Assert.Contains(entries, e => e.Name == "LiteDo" && e.AbsolutePath == "/sdcard/LiteDo");
    }

    [Fact]
    public void Parse_RealRootListing_HandlesSymlinksAndUnstattableEntries()
    {
        var entries = LsOutputParser.Parse(Fixture.Read("H29T-ls-root.txt"), "/");

        // "." 与 ".." 被忽略。
        Assert.DoesNotContain(entries, e => e.Name is "." or "..");

        var sdcard = Assert.Single(entries, e => e.Name == "sdcard");
        Assert.True(sdcard.IsSymlink);
        Assert.Equal("/storage/self/primary", sdcard.LinkTarget);
        Assert.Equal("/sdcard", sdcard.AbsolutePath);

        var blackbox = Assert.Single(entries, e => e.Name == "blackbox");
        Assert.True(blackbox.IsDirectory);
        Assert.True(blackbox.IsUnknownMetadata);
        Assert.Equal(-1, blackbox.Size);
        Assert.Null(blackbox.MTime);

        var initRc = Assert.Single(entries, e => e.Name == "init.environ.rc");
        Assert.False(initRc.IsDirectory);
        Assert.True(initRc.IsUnknownMetadata);

        var data = Assert.Single(entries, e => e.Name == "data");
        Assert.Equal("drwxrwx--x", data.Mode);
        Assert.Equal("/data", data.AbsolutePath);
    }

    [Fact]
    public void Parse_SpacesChineseArrowsAndSpecialFiles()
    {
        var entries = LsOutputParser.Parse(Fixture.Read("ls-special.txt"), "/sdcard/Download");

        var withSpaces = Assert.Single(entries, e => e.Name == "My Documents");
        Assert.True(withSpaces.IsDirectory);
        Assert.Equal("/sdcard/Download/My Documents", withSpaces.AbsolutePath);

        var report = Assert.Single(entries, e => e.Name == "测试 报告 final v2.pdf");
        Assert.False(report.IsDirectory);
        Assert.Equal(1048576, report.Size);
        Assert.Equal("/sdcard/Download/测试 报告 final v2.pdf", report.AbsolutePath);

        // 普通文件名里的 " -> " 不能被当成符号链接。
        var arrow = Assert.Single(entries, e => e.Name == "a -> b.txt");
        Assert.False(arrow.IsSymlink);
        Assert.Null(arrow.LinkTarget);

        var spacedLink = Assert.Single(entries, e => e.Name == "my link");
        Assert.True(spacedLink.IsSymlink);
        Assert.Equal("/system/bin", spacedLink.LinkTarget);

        var node = Assert.Single(entries, e => e.Name == "null");
        Assert.Equal('c', node.Mode[0]);
        Assert.Equal(-1, node.Size);

        var brokenLink = Assert.Single(entries, e => e.Name == "init");
        Assert.True(brokenLink.IsSymlink);
        Assert.True(brokenLink.IsUnknownMetadata);

        // total 行、错误行、. / .. 均不产生条目。
        Assert.DoesNotContain(entries, e => e.Name.Contains("Permission denied"));
        Assert.DoesNotContain(entries, e => e.Name is "." or "..");
        Assert.Equal(10, entries.Count);
    }

    [Theory]
    [InlineData("total 57")]
    [InlineData("ls: /sdcard/x: Permission denied")]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseLine_NonEntryLines_ReturnNull(string line)
    {
        Assert.Null(LsOutputParser.ParseLine(line, "/sdcard"));
    }

    [Fact]
    public void ParseLine_CrLfTerminated_IsTrimmed()
    {
        var entry = LsOutputParser.ParseLine("-rw-rw---- 1 u0_a100 media_rw 12 2026-11-03 03:17 报告.txt\r", "/sdcard");

        Assert.NotNull(entry);
        Assert.Equal("报告.txt", entry.Name);
        Assert.Equal(12, entry.Size);
    }

    [Fact]
    public void ParseLine_TimeWithSeconds_IsSupported()
    {
        var entry = LsOutputParser.ParseLine("-rw-r--r-- 1 root root 5 2026-01-02 03:04:05 a.txt", "/");

        Assert.NotNull(entry);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), entry.MTime);
        Assert.Equal("/a.txt", entry.AbsolutePath);
    }
}
