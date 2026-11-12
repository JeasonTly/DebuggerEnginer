using System.Windows.Media;
using InfraPalette = TestEngineer.Infrastructure.Gnss.KmlColorPalette;

namespace TestEngineer.Desktop.Gnss;

/// <summary>
/// Desktop UI 色板视图：数据来自 Infrastructure.KmlColorPalette。
/// </summary>
public static class KmlColorUiPalette
{
    public static IReadOnlyList<KmlColorEntry> Entries { get; } = BuildEntries();

    public static string DefaultColorName => "red";

    public static string DefaultKmlColor => InfraPalette.DefaultTrackColor;

    public static KmlColorEntry? FindByName(string? name) =>
        Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    public static KmlColorEntry? FindByKml(string? kml) =>
        Entries.FirstOrDefault(e => string.Equals(e.KmlAabbggrr, kml, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> AllocateDistinct(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = Entries[i % Entries.Count].KmlAabbggrr;
        }

        return result;
    }

    public static string PickRandom(ISet<string>? exclude = null) =>
        InfraPalette.RandomUnused(exclude);

    public static Color ToWpfColor(string kmlAabbggrr)
    {
        var hex = kmlAabbggrr.Trim().TrimStart('#');
        if (hex.Length != 8)
        {
            return Colors.Gray;
        }

        var a = Convert.ToByte(hex[..2], 16);
        var b = Convert.ToByte(hex[2..4], 16);
        var g = Convert.ToByte(hex[4..6], 16);
        var r = Convert.ToByte(hex[6..8], 16);
        return Color.FromArgb(a, r, g, b);
    }

    private static IReadOnlyList<KmlColorEntry> BuildEntries()
    {
        var list = new List<KmlColorEntry>(InfraPalette.Colors.Count);
        foreach (var (name, rgb) in InfraPalette.Colors)
        {
            list.Add(new KmlColorEntry(name, rgb, InfraPalette.RgbToKml(rgb)));
        }

        return list;
    }
}

public sealed record KmlColorEntry(string Name, string RgbHex, string KmlAabbggrr)
{
    public string DisplayLabel => Name;

    public SolidColorBrush PreviewBrush { get; } = CreateBrush(KmlAabbggrr);

    private static SolidColorBrush CreateBrush(string kml)
    {
        var brush = new SolidColorBrush(KmlColorUiPalette.ToWpfColor(kml));
        brush.Freeze();
        return brush;
    }
}
