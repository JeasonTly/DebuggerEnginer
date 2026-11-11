using TestEngineer.Core.Settings;

namespace TestEngineer.Desktop.Services;

public interface IThemeService
{
    AppThemeMode Current { get; }

    void Apply(AppThemeMode mode);
}
