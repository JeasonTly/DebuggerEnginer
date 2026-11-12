using System.IO;
using System.IO.Compression;
using System.Text;

namespace TestEngineer.Core.Media;

public sealed class MediaBuildException : Exception
{
    public MediaBuildException(string message) : base(message)
    {
    }
}

public static class WallpaperNames
{
    public static string? SmallCompanion(string fileName)
    {
        var stem = fileName;
        var ext = "";
        var dot = fileName.LastIndexOf('.');
        if (dot > 0)
        {
            stem = fileName[..dot];
            ext = fileName[dot..];
        }

        if (!stem.StartsWith("wallpaper_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (stem.EndsWith("_small", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return stem + "_small" + ext;
    }
}

public static class NaturalFileOrder
{
    public static int ComparePaths(string leftPath, string rightPath)
        => CompareNames(Path.GetFileName(leftPath), Path.GetFileName(rightPath));

    public static int CompareNames(string left, string right)
    {
        var i = 0;
        var j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (IsDigit(left[i]) && IsDigit(right[j]))
            {
                var i0 = i;
                var j0 = j;
                while (i < left.Length && IsDigit(left[i]))
                {
                    i++;
                }

                while (j < right.Length && IsDigit(right[j]))
                {
                    j++;
                }

                var ln = TrimZeros(left[i0..i]);
                var rn = TrimZeros(right[j0..j]);
                if (ln.Length != rn.Length)
                {
                    return ln.Length.CompareTo(rn.Length);
                }

                var cmp = string.CompareOrdinal(ln, rn);
                if (cmp != 0)
                {
                    return cmp;
                }

                continue;
            }

            var cl = char.ToLowerInvariant(left[i]);
            var cr = char.ToLowerInvariant(right[j]);
            if (cl != cr)
            {
                return cl.CompareTo(cr);
            }

            i++;
            j++;
        }

        return left.Length.CompareTo(right.Length);
    }

    private static bool IsDigit(char ch) => ch is >= '0' and <= '9';

    private static string TrimZeros(string value)
    {
        var index = 0;
        while (index + 1 < value.Length && value[index] == '0')
        {
            index++;
        }

        return value[index..];
    }
}

public static class Bmp24
{
    public static byte[] Encode(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new MediaBuildException("图片尺寸无效");
        }

        var stride = (width * 3 + 3) & ~3;
        var pixelBytes = stride * height;
        var file = new byte[54 + pixelBytes];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        Write32(file, 2, (uint)file.Length);
        Write32(file, 10, 54);
        Write32(file, 14, 40);
        Write32(file, 18, (uint)width);
        Write32(file, 22, (uint)height);
        file[26] = 1;
        file[28] = 24;
        var srcStride = width * 4;
        if (bgra.Length < srcStride * height)
        {
            throw new MediaBuildException("图片像素不足");
        }

        for (var y = 0; y < height; y++)
        {
            var src = (height - 1 - y) * srcStride;
            var dst = 54 + y * stride;
            for (var x = 0; x < width; x++)
            {
                file[dst] = bgra[src];
                file[dst + 1] = bgra[src + 1];
                file[dst + 2] = bgra[src + 2];
                src += 4;
                dst += 3;
            }
        }

        return file;
    }

    private static void Write32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }
}

public readonly record struct ZipEntry(string Name, byte[] Data);

public static class StoredZip
{
    public static byte[] Build(IReadOnlyList<ZipEntry> items)
    {
        var now = DateTime.Now;
        var dosTime = (ushort)((now.Hour << 11) | (now.Minute << 5) | (now.Second / 2));
        var dosDate = (ushort)(((now.Year - 1980) << 9) | (now.Month << 5) | now.Day);
        var crcs = new uint[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            crcs[i] = Crc32(items[i].Data);
        }

        using var stream = new MemoryStream();
        var offsets = new uint[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var name = Encoding.ASCII.GetBytes(items[i].Name);
            offsets[i] = checked((uint)stream.Length);
            var size = checked((uint)items[i].Data.Length);
            Write32(stream, 0x04034b50);
            Write16(stream, 20);
            Write16(stream, 0);
            Write16(stream, 0);
            Write16(stream, dosTime);
            Write16(stream, dosDate);
            Write32(stream, crcs[i]);
            Write32(stream, size);
            Write32(stream, size);
            Write16(stream, (ushort)name.Length);
            Write16(stream, 0);
            stream.Write(name);
            stream.Write(items[i].Data);
        }

        var central = checked((uint)stream.Length);
        for (var i = 0; i < items.Count; i++)
        {
            var name = Encoding.ASCII.GetBytes(items[i].Name);
            var size = checked((uint)items[i].Data.Length);
            Write32(stream, 0x02014b50);
            Write16(stream, 20);
            Write16(stream, 20);
            Write16(stream, 0);
            Write16(stream, 0);
            Write16(stream, dosTime);
            Write16(stream, dosDate);
            Write32(stream, crcs[i]);
            Write32(stream, size);
            Write32(stream, size);
            Write16(stream, (ushort)name.Length);
            Write16(stream, 0);
            Write16(stream, 0);
            Write16(stream, 0);
            Write16(stream, 0);
            Write32(stream, 0);
            Write32(stream, offsets[i]);
            stream.Write(name);
        }

        var centralSize = checked((uint)stream.Length) - central;
        Write32(stream, 0x06054b50);
        Write16(stream, 0);
        Write16(stream, 0);
        Write16(stream, (ushort)items.Count);
        Write16(stream, (ushort)items.Count);
        Write32(stream, centralSize);
        Write32(stream, central);
        Write16(stream, 0);
        return stream.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xffffffffu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                var mask = (uint)-(int)(crc & 1);
                crc = (crc >> 1) ^ (0xedb88320u & mask);
            }
        }

        return ~crc;
    }

    private static void Write16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
    }

    private static void Write32(Stream stream, uint value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
    }
}

public static class BootAnimationText
{
    public static string DescFile(int width, int height, int fps)
        => $"{width} {height} {fps}\np 1 0 part0\np 0 0 part1\n";

    public static string FrameName(int index1Based) => $"{index1Based:00000}.png";

    public static string Summary(int width, int height, int fps, int frameCount, bool sizeMismatch)
    {
        var text = $"开机动画 {width}x{height}，{fps} 帧/秒。part0 按文件名数字顺序播放一次，共 {frameCount} 帧；part1 为最后一帧并保持。压缩方式为仅存储";
        if (sizeMismatch)
        {
            text += "。有图片分辨率和第一张不同，描述文件使用第一张的尺寸";
        }

        return text;
    }
}
