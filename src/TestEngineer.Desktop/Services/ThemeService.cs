using System.Windows;
using TestEngineer.Core.Settings;
using TestEngineer.UI;
using Wpf.Ui.Appearance;

namespace TestEngineer.Desktop.Services;

/// <summary>同时切换 TestEngineer.UI 令牌字典与 WPF-UI 控件主题。</summary>
public sealed class ThemeService : IThemeService
{
    public AppThemeMode Current { get; private set; } = AppThemeMode.Dark;

    public void Apply(AppThemeMode mode)
    {
        var variant = mode == AppThemeMode.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        ThemeResources.Apply(Application.Current.Resources, variant);
        ApplicationThemeManager.Apply(mode == AppThemeMode.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        Current = mode;
    }
}
