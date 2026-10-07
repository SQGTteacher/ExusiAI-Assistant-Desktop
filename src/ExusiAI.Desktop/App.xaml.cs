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
    private string startupStage = "进入 WPF 启动";
    private StartupWindow? startupWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Initialization awaits I/O before the first window exists. Keep the WPF
        // dispatcher alive until the main window has actually been shown.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        if (e.Args is ["--verify-classisland", var classIslandOutput])
        {
            try
            {
                await ClassIslandVerification.RunAsync(classIslandOutput);
                Shutdown(0);
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(classIslandOutput);
                File.WriteAllText(Path.Combine(classIslandOutput, "failure.txt"), exception.ToString());
                Shutdown(1);
            }
            return;
        }
        if (e.Args is ["--verify-ui", var outputDirectory])
        {
            try
            {
                host = BuildHost();
                await UiVerification.RunAsync(host.Services, outputDirectory);
                Shutdown(0);
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "failure.txt"), exception.ToString());
                Shutdown(1);
            }
            return;
        }
        try
        {
            startupWindow = new StartupWindow();
            MainWindow = startupWindow;
            startupWindow.Show();
            // Give WPF a render turn before any host construction or disk I/O.
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            TraceStartup(startupStage);
            host = BuildHost();
            TraceStartup("Host 已创建");
            crashReporter = host.Services.GetRequiredService<ICrashReporter>();
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            await host.StartAsync();
            TraceStartup("Host 已启动");
            var theme = host.Services.GetRequiredService<IThemeService>();
            var settings = await host.Services.GetRequiredService<ISettingsService>().LoadAsync();
            TraceStartup("设置已加载");
            theme.Apply(settings.Theme);
            ApplyTheme(theme.Current);
            theme.Changed += (_, _) => Dispatcher.InvokeAsync(() => ApplyTheme(theme.Current));
            var backdrop = host.Services.GetRequiredService<IWindowBackdropService>();
            if (!Enum.TryParse<WindowBackdropKind>(settings.Backdrop, true, out var backdropSelection)) backdropSelection = WindowBackdropKind.Mica;
            backdrop.Apply(backdropSelection);
            runtime = host.Services.GetRequiredService<ExtensionRuntime>();
            var paths = host.Services.GetRequiredService<IAppPaths>();
            TraceStartup("正在同步内置插件");
            RemoveLegacyBundledPackages(paths);
            await Task.Run(() => host.Services.GetRequiredService<BundledPackageSynchronizer>().SynchronizeAsync());
            TraceStartup("内置插件包已同步");

            TraceStartup("正在创建主窗口");
            var window = host.Services.GetRequiredService<MainWindow>();
            window.DataContext = host.Services.GetRequiredService<ShellViewModel>();
            MainWindow = window;
            window.SourceInitialized += (_, _) => ApplyAdaptiveMetrics(window);
            window.DpiChanged += (_, _) => ApplyAdaptiveMetrics(window);
            window.Show();
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            TraceStartup("主窗口已显示");
            startupWindow.Close();
            startupWindow = null;
            ShutdownMode = ShutdownMode.OnLastWindowClose;

            extensionStartupCancellation = new CancellationTokenSource();
            extensionStartupTask = InitializeExtensionsAsync(
                paths,
                settings.DisabledPackages ?? [],
                extensionStartupCancellation.Token);
        }
        catch (Exception exception)
        {
            var failedStage = startupStage;
            TraceStartup($"启动失败：{exception.GetType().Name}，阶段：{failedStage}");
            TraceStartupException(exception);
            host?.Services.GetService<ILogger<App>>()?.LogCritical(exception, "Application startup failed at {Stage}.", failedStage);
            if (crashReporter is not null)
                crashReporter.Report(exception, "应用启动失败", showDialog: false);
            if (startupWindow is { IsVisible: true })
            {
                var cause = exception;
                while (cause.InnerException is { } inner) cause = inner;
                startupWindow.ShowFailure(failedStage, cause.Message, StartupLogPath);
                return;
            }
            else
                MessageBox.Show("ExusiAI 无法启动。请检查本地日志后重试。", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void RemoveLegacyBundledPackages(IAppPaths paths)
    {
        foreach (var path in new[]
                 {
                     Path.Combine(paths.ApplicationDirectory, "packages", "exusiai.sample"),
                     Path.Combine(paths.ApplicationDirectory, "packages", "exusiai.misha-showcase"),
                     Path.Combine(paths.PackagesDirectory, "exusiai.misha-showcase")
                 })
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A locked obsolete bundled package must not block host startup. It will be retried
                // on the next clean startup before extension discovery.
            }
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        TraceStartup($"进程退出，代码 {e.ApplicationExitCode}");
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

    private void TraceStartup(string stage)
    {
        startupStage = stage;
        startupWindow?.SetStage(stage);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StartupLogPath)!);
            File.AppendAllText(StartupLogPath,
                $"{DateTimeOffset.Now:O} | {stage} | PID={Environment.ProcessId} | {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} | WorkingSet={Environment.WorkingSet / 1024 / 1024} MiB{Environment.NewLine}");
        }
        catch (Exception) { /* Diagnostics must never prevent startup. */ }
    }

    private static string StartupLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExusiAI", "logs", "startup.log");

    private static void TraceStartupException(Exception exception)
    {
        try
        {
            File.AppendAllText(StartupLogPath, $"{exception}{Environment.NewLine}");
        }
        catch (Exception) { /* Diagnostics must never prevent startup. */ }
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

            await runtime.DiscoverAsync(paths.PackagesDirectory, cancellationToken);
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
        builder.Services.AddSingleton<PackageArchiveService>();
        builder.Services.AddSingleton<ExtensionRuntime>();
        builder.Services.AddSingleton<BundledPackageSynchronizer>();
        builder.Services.AddSingleton<PluginPackageManager>();
        builder.Services.AddSingleton<WpfNavigationRegistry>();
        builder.Services.AddSingleton<WpfTrayRegistry>();
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

    private static void ApplyAdaptiveMetrics(Window window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var pixelWidth = SystemParameters.PrimaryScreenWidth * dpi.DpiScaleX;
        var pixelHeight = SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY;
        var scale = pixelWidth >= 3200 || pixelHeight >= 1800 ? 1.35
            : pixelWidth >= 2200 || pixelHeight >= 1250 ? 1.18
            : 1.0;

        Current.Resources["TouchTargetHeight"] = Math.Round(44 * scale);
        Current.Resources["TouchCompactTargetHeight"] = Math.Round(40 * scale);
        Current.Resources["TouchCardWidth"] = Math.Round(225 * Math.Min(scale, 1.2));
        Current.Resources["TouchCardHeight"] = Math.Round(70 * scale);
        Current.Resources["TouchSidebarWidth"] = Math.Round(218 * Math.Min(scale, 1.2));
        var titleBarHeight = Math.Round(48 * scale);
        Current.Resources["TouchTitleBarHeight"] = titleBarHeight;
        if (window is MainWindow mainWindow) mainWindow.SetTitleBarHeight(titleBarHeight);
        Current.Resources["TouchDragThreshold"] = Math.Round(8 * scale);
        Current.Resources["TouchScrollBarThickness"] = Math.Round(12 * scale);
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
        FluentThemeBridge.Apply(palette);
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
        SetBrush("NavRailBrush", palette.Background);
        SetBrush("WindowTopBarBrush", palette.Background);
    }

    private static void SetBrush(string key, string color) => Current.Resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    internal static string GetContrastingForeground(string color)
    {
        var value = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
        var luminance = (0.2126 * value.R + 0.7152 * value.G + 0.0722 * value.B) / 255d;
        return luminance > 0.58 ? "#161922" : "#FFFFFF";
    }
}
