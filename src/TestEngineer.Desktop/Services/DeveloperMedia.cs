using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TestEngineer.Core.Media;

namespace TestEngineer.Desktop.Services;

internal static class DeveloperMedia
{
    public readonly record struct Frame(int Width, int Height, byte[] Bgra);

    public static byte[] Transcode(string source, string format)
    {
        var frame = Decode(source);
        return format == "bmp" ? Bmp24.Encode(frame.Bgra, frame.Width, frame.Height) : EncodePng(frame);
    }

    public static (byte[] Zip, string Summary) BuildBoot(
        IReadOnlyList<string> sources,
        int fps,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken cancellationToken)
    {
        if (sources.Count == 0)
        {
            throw new MediaBuildException("还没有选择图片");
        }

        if (fps <= 0)
        {
            fps = 30;
        }

        var frames = sources.ToList();
        frames.Sort(NaturalFileOrder.ComparePaths);
        var items = new List<ZipEntry>(frames.Count + 2);
        var width = 0;
        var height = 0;
        var warned = false;
        byte[]? last = null;
        for (var i = 0; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = Decode(frames[i]);
            if (i == 0)
            {
                width = frame.Width;
                height = frame.Height;
            }
            else if (frame.Width != width || frame.Height != height)
            {
                warned = true;
            }

            var png = EncodePng(frame);
            last = png;
            items.Add(new ZipEntry("part0/" + BootAnimationText.FrameName(i + 1), png));
            progress?.Report((i + 1, frames.Count));
        }

        items.Add(new ZipEntry("part1/00001.png", last!));
        var desc = BootAnimationText.DescFile(width, height, fps);
        items.Insert(0, new ZipEntry("desc.txt", System.Text.Encoding.ASCII.GetBytes(desc)));
        var zip = StoredZip.Build(items);
        var summary = BootAnimationText.Summary(width, height, fps, frames.Count, warned);
        return (zip, summary);
    }

    public static bool IsImageName(string name)
    {
        var ext = Path.GetExtension(name);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static Frame Decode(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                throw new MediaBuildException("无法读取图片 " + path);
            }

            BitmapSource source = decoder.Frames[0];
            if (source.Format != PixelFormats.Bgra32)
            {
                var converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = source;
                converted.DestinationFormat = PixelFormats.Bgra32;
                converted.EndInit();
                converted.Freeze();
                source = converted;
            }

            var width = source.PixelWidth;
            var height = source.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                throw new MediaBuildException("无法读取图片 " + path);
            }

            var stride = width * 4;
            var pixels = new byte[stride * height];
            source.CopyPixels(pixels, stride, 0);
            return new Frame(width, height, pixels);
        }
        catch (MediaBuildException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new MediaBuildException("无法打开图片 " + path);
        }
    }

    private static byte[] EncodePng(Frame frame)
    {
        try
        {
            var source = BitmapSource.Create(
                frame.Width,
                frame.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                frame.Bgra,
                frame.Width * 4);
            source.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            var bytes = stream.ToArray();
            if (bytes.Length == 0)
            {
                throw new MediaBuildException("编码开机动画帧失败");
            }

            return bytes;
        }
        catch (MediaBuildException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new MediaBuildException("编码开机动画帧失败");
        }
    }
}
