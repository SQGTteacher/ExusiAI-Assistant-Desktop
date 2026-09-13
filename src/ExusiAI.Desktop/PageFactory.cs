using System.Windows;
using ExusiAI.Extension.Runtime;
using ExusiAI.Extension.Wpf;
using ExusiAI.Infrastructure;
using ExusiAI.Marketplace;
using ExusiAI.Theme;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Desktop;

public sealed class PageFactory(ExtensionRuntime runtime, WpfNavigationRegistry registry, IThemeService theme, IWindowBackdropService backdrop, ISettingsService settings, IAppPaths paths, IPackageCatalog catalog, ILoggerFactory loggerFactory, ICrashReporter crashReporter)
{
    public FrameworkElement Create(string route)
    {
        try
        {
            return route switch
            {
                "home" => new HomePage { DataContext = new HomeViewModel(runtime) },
                "workspace" => new PluginWorkspacePage { DataContext = new PluginWorkspaceViewModel(registry, crashReporter) },
                "marketplace" => new MarketplacePage { DataContext = new MarketplaceViewModel(catalog) },
                "extensions" => new PluginManagerPage { DataContext = new PluginManagerViewModel(runtime, settings) },
                "theme" => new ThemePage { DataContext = new ThemeViewModel(theme, backdrop, settings, loggerFactory.CreateLogger<ThemeViewModel>()) },
                "settings" => new SoftwareSettingsPage { DataContext = new SoftwareSettingsViewModel(paths, runtime) },
                _ => new HomePage { DataContext = new HomeViewModel(runtime) }
            };
        }
        catch (Exception exception) { return crashReporter.CreateErrorPage(exception, $"创建页面 {route}"); }
    }
}
