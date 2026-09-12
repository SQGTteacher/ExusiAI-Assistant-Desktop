using System.IO;
using System.Windows;
using System.Windows.Media;
using ExusiAI.Extension.Runtime;
using ExusiAI.Extension.Wpf;
using ExusiAI.Infrastructure;
using ExusiAI.Marketplace;
using ExusiAI.Theme;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Desktop;

public partial class App : Application
{
    private IHost? host;
    private ExtensionRuntime? runtime;
    private WpfExtensionCoordinator? wpfExtensions;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            host = BuildHost();
            await host.StartAsync();
            var theme = host.Services.GetRequiredService<IThemeService>();
            var settings = await host.Services.GetRequiredService<ISettingsService>().LoadAsync();
            theme.Apply(settings.Theme);
            ApplyTheme(theme.Current);
            theme.Changed += (_, _) => Dispatcher.InvokeAsync(() => ApplyTheme(theme.Current));
            var backdrop = host.Services.GetRequiredService<IWindowBackdropService>();
            if (!Enum.TryParse<WindowBackdropKind>(settings.Backdrop, true, out var backdropSelection)) backdropSelection = WindowBackdropKind.Mica;
            backdrop.Apply(backdropSelection);
            runtime = host.Services.GetRequiredService<ExtensionRuntime>();
            var paths = host.Services.GetRequiredService<IAppPaths>();
            await runtime.DiscoverAsync(Path.Combine(paths.ApplicationDirectory, "packages"));
            await runtime.StartAsync(settings.DisabledPackages ?? []);
            wpfExtensions = host.Services.GetRequiredService<WpfExtensionCoordinator>();
            wpfExtensions.Start();

            var window = host.Services.GetRequiredService<MainWindow>();
            window.DataContext = host.Services.GetRequiredService<ShellViewModel>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            host?.Services.GetService<ILogger<App>>()?.LogCritical(exception, "Application startup failed.");
            MessageBox.Show("ExusiAI 无法启动。请检查本地日志后重试。", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            wpfExtensions?.Dispose();
            if (runtime is not null) await runtime.DisposeAsync();
            if (host is not null)
            {
                await host.StopAsync(TimeSpan.FromSeconds(5));
                host.Dispose();
            }
        }
        catch (Exception exception)
        {
            host?.Services.GetService<ILogger<App>>()?.LogError(exception, "Application shutdown was incomplete.");
        }
        finally { base.OnExit(e); }
    }

    private static IHost BuildHost()
    {
        var paths = new AppPaths();
        paths.EnsureDirectories();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders().AddExusiAIFileLogging();
        builder.Services.AddSingleton<IAppPaths>(paths);
        builder.Services.AddSingleton<ManifestParser>();
        builder.Services.AddSingleton<ManifestValidator>();
        builder.Services.AddSingleton<PackageDiscoveryService>();
        builder.Services.AddSingleton<ExtensionRuntime>();
        builder.Services.AddSingleton<WpfNavigationRegistry>();
        builder.Services.AddSingleton<WpfExtensionCoordinator>();
        builder.Services.AddSingleton<SystemThemeProvider>();
        builder.Services.AddSingleton<ISystemThemeProvider>(x => x.GetRequiredService<SystemThemeProvider>());
        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<IWindowBackdropService, WindowBackdropService>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IPackageCatalog, PlaceholderPackageCatalog>();
        builder.Services.AddSingleton<PageFactory>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        return builder.Build();
    }

    public static void ApplyTheme(ThemePalette palette)
    {
        SetBrush("AppBackgroundBrush", palette.Background);
        SetBrush("SurfaceBrush", palette.Surface);
        SetBrush("SurfaceAltBrush", palette.SurfaceAlternative);
        SetBrush("TextPrimaryBrush", palette.TextPrimary);
        SetBrush("TextSecondaryBrush", palette.TextSecondary);
        SetBrush("BorderBrush", palette.Border);
        SetBrush("AccentBrush", palette.Accent);
        SetBrush("AccentSoftBrush", palette.AccentSoft);
        SetBrush("SuccessBrush", palette.Success);
        SetBrush("WarningBrush", palette.Warning);
        SetBrush("DangerBrush", palette.Danger);
    }

    private static void SetBrush(string key, string color) => Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
