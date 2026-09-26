using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
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
    private ICrashReporter? crashReporter;
    private CancellationTokenSource? extensionStartupCancellation;
    private Task? extensionStartupTask;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            host = BuildHost();
            crashReporter = host.Services.GetRequiredService<ICrashReporter>();
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
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
            RemoveLegacyBundledSample(paths.ApplicationDirectory);

            var window = host.Services.GetRequiredService<MainWindow>();
            window.DataContext = host.Services.GetRequiredService<ShellViewModel>();
            MainWindow = window;
            window.Show();

            extensionStartupCancellation = new CancellationTokenSource();
            extensionStartupTask = InitializeExtensionsAsync(
                paths,
                settings.DisabledPackages ?? [],
                extensionStartupCancellation.Token);
        }
        catch (Exception exception)
        {
            host?.Services.GetService<ILogger<App>>()?.LogCritical(exception, "Application startup failed.");
            if (crashReporter is not null)
                crashReporter.Report(exception, "应用启动失败");
            else
                MessageBox.Show("ExusiAI 无法启动。请检查本地日志后重试。", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void RemoveLegacyBundledSample(string applicationDirectory)
    {
        var samplePath = Path.Combine(applicationDirectory, "packages", "exusiai.sample");
        try
        {
            if (Directory.Exists(samplePath))
                Directory.Delete(samplePath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked legacy package must not prevent the host from starting. It is no longer
            // produced or registered and will be removed by the next clean install/build.
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            extensionStartupCancellation?.Cancel();
            if (extensionStartupTask is not null)
                await extensionStartupTask;

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
        finally
        {
            extensionStartupCancellation?.Dispose();
            extensionStartupCancellation = null;
            DispatcherUnhandledException -= App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
            base.OnExit(e);
        }
    }

    private async Task InitializeExtensionsAsync(
        IAppPaths paths,
        IEnumerable<string> disabledPackages,
        CancellationToken cancellationToken)
    {
        if (runtime is null || host is null)
            return;

        try
        {
            // Let the shell render and become interactive before package discovery/initialization.
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await runtime.DiscoverAsync(
                [Path.Combine(paths.ApplicationDirectory, "packages"), paths.PackagesDirectory],
                cancellationToken);
            await runtime.StartAsync(disabledPackages, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await Dispatcher.InvokeAsync(
                () =>
                {
                    if (cancellationToken.IsCancellationRequested || host is null)
                        return;
                    wpfExtensions = host.Services.GetRequiredService<WpfExtensionCoordinator>();
                    wpfExtensions.Start();
                },
                DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            host.Services.GetService<ILogger<App>>()?.LogError(exception, "Deferred extension startup failed.");
            crashReporter?.Report(exception, "扩展延迟加载失败");
        }
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
        builder.Services.AddSingleton<ICrashReporter, CrashReporter>();
        builder.Services.AddSingleton<IPackageCatalog>(services =>
            new LocalPackageCatalog(() => services.GetRequiredService<ExtensionRuntime>().Entries.Select(x => x.Package.Manifest)));
        builder.Services.AddSingleton<PageFactory>();
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        return builder.Build();
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        crashReporter?.Report(e.Exception, "UI 线程未处理异常");
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) crashReporter?.Report(exception, "进程级未处理异常", false);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        crashReporter?.Report(e.Exception, "后台任务未观察异常");
        e.SetObserved();
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
        SetBrush("AccentForegroundBrush", GetContrastingForeground(palette.Accent));
        SetBrush("AccentSoftBrush", palette.AccentSoft);
        SetBrush("SuccessBrush", palette.Success);
        SetBrush("WarningBrush", palette.Warning);
        SetBrush("AccentWarmBrush", palette.Warning);
        SetBrush("DangerBrush", palette.Danger);
        Current.Resources["WindowTopBarBrush"] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Surface));
        Current.Resources["NavRailBrush"] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette.Surface));
    }

    private static void SetBrush(string key, string color) => Current.Resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private static string GetContrastingForeground(string color)
    {
        var value = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
        var luminance = (0.2126 * value.R + 0.7152 * value.G + 0.0722 * value.B) / 255d;
        return luminance > 0.58 ? "#161922" : "#FFFFFF";
    }
}
