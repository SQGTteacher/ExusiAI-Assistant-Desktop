using System.Windows;
using ExusiAI.Extension.Runtime;
using ExusiAI.Extension.Wpf;
using ExusiAI.Infrastructure;
using ExusiAI.Marketplace;
using ExusiAI.Theme;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Desktop;

public sealed class PageFactory(ExtensionRuntime runtime, WpfNavigationRegistry registry, IThemeService theme, ISettingsService settings, IPackageCatalog catalog, ILoggerFactory loggerFactory)
{
    public FrameworkElement Create(string route) => route switch
    {
        "home" => new HomePage { DataContext = new HomeViewModel(runtime) },
        "marketplace" => new MarketplacePage { DataContext = new MarketplaceViewModel(catalog) },
        "extensions" => new PluginManagerPage { DataContext = new PluginManagerViewModel(runtime) },
        "theme" => new ThemePage { DataContext = new ThemeViewModel(theme, settings, loggerFactory.CreateLogger<ThemeViewModel>()) },
        "settings" => new SoftwareSettingsPage { DataContext = new SoftwareSettingsViewModel() },
        _ => registry.Pages.FirstOrDefault(x => string.Equals(x.Page.Route, route, StringComparison.OrdinalIgnoreCase))?.Page.CreateView()
             ?? new HomePage { DataContext = new HomeViewModel(runtime) }
    };
}
