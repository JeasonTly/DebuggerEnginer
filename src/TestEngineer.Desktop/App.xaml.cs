using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TestEngineer.Adb;
using TestEngineer.Core.Abstractions;
using TestEngineer.Core.Settings;
using TestEngineer.Desktop.Services;
using TestEngineer.Desktop.ViewModels;
using TestEngineer.Infrastructure.History;
using TestEngineer.Infrastructure.Paths;
using TestEngineer.Infrastructure.Settings;
using TestEngineer.Infrastructure.Tools;
using TestEngineer.Mirror;

namespace TestEngineer.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = AppPaths.Resolve();
        try
        {
            paths.EnsureDirectories();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "TestEngineer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _host = BuildHost(paths);
        await _host.StartAsync();

        DispatcherUnhandledException += (_, args) =>
        {
            _host.Services.GetRequiredService<ILogger<App>>().LogError(args.Exception, "未处理的 UI 线程异常");
            MessageBox.Show(args.Exception.Message, "TestEngineer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var appSettings = _host.Services.GetRequiredService<IAppSettings>();
        _host.Services.GetRequiredService<IThemeService>().Apply(ResolveTheme(appSettings.Current.Theme));

        await _host.Services.GetRequiredService<IDeviceContext>().StartAsync();

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                _host.Services.GetRequiredService<IAppSettings>().Save();
            }
            catch (Exception ex)
            {
                _host.Services.GetRequiredService<ILogger<App>>().LogError(ex, "退出时保存设置失败");
            }

            try
            {
                var mirror = _host.Services.GetService<IMirrorSession>();
                if (mirror is not null)
                {
                    await mirror.DisposeAsync();
                }
            }
            catch { /* ignore */ }

            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static AppThemeMode ResolveTheme(AppThemeMode mode)
    {
        if (mode != AppThemeMode.System)
        {
            return mode;
        }

        try
        {
            // Prefer dark when system apps use dark (Windows personalization).
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0 ? AppThemeMode.Dark : AppThemeMode.Light;
        }
        catch
        {
            return AppThemeMode.Dark;
        }
    }

    private static IHost BuildHost(AppPaths paths)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();

        var services = builder.Services;
        services.AddSingleton(paths);
        services.AddSingleton<ISettingsStore>(sp =>
            new JsonSettingsStore(paths.SettingsFilePath, sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
        services.AddSingleton<IAppSettings, AppSettingsService>();
        services.AddSingleton(sp => sp.GetRequiredService<IAppSettings>().Current);

        services.AddSingleton<IToolLocator>(sp =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            return new ToolLocator(
                startDirectories: [paths.ProgramDirectory, AppContext.BaseDirectory],
                overrides: () => settings.ToolPaths);
        });

        services.AddSingleton<IAdbProcessRunner, AdbProcessRunner>();
        services.AddSingleton<IDeviceTracker, DeviceTracker>();
        services.AddSingleton<IDeviceContext, DeviceContext>();
        services.AddSingleton<IDeviceInfoService, DeviceInfoService>();
        services.AddSingleton<IAppManagerService, AppManagerService>();
        services.AddSingleton<IFileManagerService, FileManagerService>();
        services.AddSingleton<IDebugCommandService, DebugCommandService>();
        services.AddSingleton<IConsoleHistory>(sp =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            return new ConsoleHistoryStore(paths.ConsoleHistoryFilePath, () => settings.ConsoleHistoryLimit);
        });
        services.AddSingleton<IConsoleService>(sp =>
            new ConsoleService(
                sp.GetRequiredService<IAdbProcessRunner>(),
                sp.GetRequiredService<IConsoleHistory>()));
        services.AddSingleton<IMirrorSession, ScrcpyMirrorSession>();

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<DevicePageViewModel>();
        services.AddSingleton<AppsPageViewModel>();
        services.AddSingleton<FilesPageViewModel>();
        services.AddSingleton<DebugPageViewModel>();
        services.AddSingleton<ConsolePageViewModel>();
        services.AddSingleton<CastPageViewModel>();
        services.AddSingleton<AboutPageViewModel>();
        services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
