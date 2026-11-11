using System.Windows;

namespace TestEngineer.UI;

public enum ThemeVariant
{
    Dark,
    Light,
}

/// <summary>在应用资源中加载或切换 Dark.xaml / Light.xaml 令牌字典。</summary>
public static class ThemeResources
{
    private const string BasePack = "pack://application:,,,/TestEngineer.UI;component/Themes/";

    public static Uri CommonUri { get; } = new(BasePack + "Common.xaml", UriKind.Absolute);

    public static Uri GetThemeUri(ThemeVariant variant) =>
        new(BasePack + (variant == ThemeVariant.Dark ? "Dark.xaml" : "Light.xaml"), UriKind.Absolute);

    /// <summary>替换 <paramref name="resources"/> 中已有的主题字典；不存在时追加。</summary>
    public static void Apply(ResourceDictionary resources, ThemeVariant variant)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var merged = resources.MergedDictionaries;
        var next = new ResourceDictionary { Source = GetThemeUri(variant) };

        for (var i = 0; i < merged.Count; i++)
        {
            if (IsThemeDictionary(merged[i]))
            {
                merged[i] = next;
                return;
            }
        }

        merged.Add(next);
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return source is not null
            && source.StartsWith(BasePack, StringComparison.OrdinalIgnoreCase)
            && (source.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase));
    }
}
