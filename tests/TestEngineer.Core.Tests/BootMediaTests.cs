using System.IO.Compression;
using System.Text;
using TestEngineer.Core.Media;

namespace TestEngineer.Core.Tests;

public sealed class BootMediaTests
{
    [Theory]
    [InlineData("wallpaper_01.png", "wallpaper_01_small.png")]
    [InlineData("Wallpaper_02.PNG", "Wallpaper_02_small.PNG")]
    [InlineData("wallpaper_01_small.png", null)]
    [InlineData("logo.bmp", null)]
    public void SmallCompanion_OnlyForWallpaperStem(string name, string? expected)
    {
        Assert.Equal(expected, WallpaperNames.SmallCompanion(name));
    }

    [Fact]
    public void NaturalOrder_SortsEmbeddedNumbers()
    {
        var names = new[] { @"D:\a\frame10.png", @"D:\a\frame2.png", @"D:\a\frame1.png" };
        Array.Sort(names, NaturalFileOrder.ComparePaths);
        Assert.Equal(
            [@"D:\a\frame1.png", @"D:\a\frame2.png", @"D:\a\frame10.png"],
            names);
    }

    [Fact]
    public void Bmp24_WritesBottomUp24Bit()
    {
        var bgra = new byte[] { 0x10, 0x20, 0x30, 0xFF };
        var bmp = Bmp24.Encode(bgra, 1, 1);
        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(24, bmp[28]);
        Assert.Equal(0x10, bmp[54]);
        Assert.Equal(0x20, bmp[55]);
        Assert.Equal(0x30, bmp[56]);
    }

    [Fact]
    public void StoredZip_RoundTripsUncompressedEntries()
    {
        var zip = StoredZip.Build(
        [
            new ZipEntry("desc.txt", Encoding.ASCII.GetBytes("8 8 30\np 1 0 part0\n")),
            new ZipEntry("part0/00001.png", [1, 2, 3, 4]),
        ]);
        using var stream = new MemoryStream(zip);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var desc = archive.GetEntry("desc.txt");
        var frame = archive.GetEntry("part0/00001.png");
        Assert.NotNull(desc);
        Assert.NotNull(frame);
        using var descStream = desc.Open();
        using var reader = new StreamReader(descStream, Encoding.ASCII);
        Assert.StartsWith("8 8 30", reader.ReadToEnd(), StringComparison.Ordinal);
        Assert.Equal(desc.Length, desc.CompressedLength);
        Assert.Equal(frame.Length, frame.CompressedLength);
    }

    [Fact]
    public void DescFile_PlaysPart0OnceAndHoldsPart1()
    {
        Assert.Equal("1080 1920 30\np 1 0 part0\np 0 0 part1\n", BootAnimationText.DescFile(1080, 1920, 30));
        Assert.Contains("part1 为最后一帧并保持", BootAnimationText.Summary(1080, 1920, 30, 4, false), StringComparison.Ordinal);
        Assert.Contains("描述文件使用第一张的尺寸", BootAnimationText.Summary(1080, 1920, 30, 4, true), StringComparison.Ordinal);
    }
}
