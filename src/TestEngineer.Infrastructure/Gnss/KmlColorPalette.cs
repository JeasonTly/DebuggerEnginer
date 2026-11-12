namespace TestEngineer.Infrastructure.Gnss;

/// <summary>与 nmea_to_kml.py COLOR_PALETTE / rgb_to_kml 对齐的色板。</summary>
public static class KmlColorPalette
{
    /// <summary>名称 → RRGGBB（不含 #）。</summary>
    public static IReadOnlyDictionary<string, string> Colors { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["red"] = "E53935",
            ["blue"] = "1E88E5",
            ["green"] = "43A047",
            ["orange"] = "FB8C00",
            ["purple"] = "8E24AA",
            ["cyan"] = "00ACC1",
            ["pink"] = "E91E63",
            ["yellow"] = "FDD835",
            ["teal"] = "00897B",
            ["lime"] = "7CB342",
            ["indigo"] = "3949AB",
            ["amber"] = "FFB300",
            ["coral"] = "FF7043",
            ["magenta"] = "D81B60",
            ["turquoise"] = "26C6DA",
            ["violet"] = "7E57C2",
            ["brown"] = "8D6E63",
            ["olive"] = "9E9D24",
            ["sky"] = "039BE5",
            ["crimson"] = "C62828",
        };

    /// <summary>将 RRGGBB 转为 KML AABBGGRR（不透明，小写）。</summary>
    public static string RgbToKml(string rgbHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rgbHex);
        var rgb = rgbHex.Trim().TrimStart('#').ToUpperInvariant();
        if (rgb.Length != 6)
        {
            throw new ArgumentException($"Invalid color hex: '{rgbHex}' (need RRGGBB)", nameof(rgbHex));
        }

        var r = rgb[0..2];
        var g = rgb[2..4];
        var b = rgb[4..6];
        return ("ff" + b + g + r).ToLowerInvariant();
    }

    public static string GetKmlColor(string name)
    {
        if (!Colors.TryGetValue(name, out var rgb))
        {
            throw new KeyNotFoundException($"Unknown palette color: {name}");
        }

        return RgbToKml(rgb);
    }

    /// <summary>随机选一个未在 exclude 中的色板色；若全部用完则重置可选集。</summary>
    public static string RandomUnused(IEnumerable<string>? exclude = null, Random? random = null)
    {
        random ??= Random.Shared;
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (exclude is not null)
        {
            foreach (var c in exclude)
            {
                if (!string.IsNullOrWhiteSpace(c))
                {
                    excluded.Add(c.Trim().ToLowerInvariant());
                }
            }
        }

        var available = Colors
            .Where(kv => !excluded.Contains(RgbToKml(kv.Value)))
            .Select(kv => kv.Key)
            .ToList();

        if (available.Count == 0)
        {
            available = Colors.Keys.ToList();
        }

        var name = available[random.Next(available.Count)];
        return RgbToKml(Colors[name]);
    }

    public static string DefaultTrackColor => RgbToKml(Colors["red"]);
}
