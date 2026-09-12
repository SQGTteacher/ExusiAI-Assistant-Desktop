using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExusiAI.Core;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.Runtime;
using ExusiAI.Extension.Wpf;
using ExusiAI.Infrastructure;
using ExusiAI.Marketplace;
using ExusiAI.Theme;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Desktop;

public sealed class HomeViewModel
{
    public HomeViewModel(ExtensionRuntime runtime)
    {
        InstalledCount = runtime.Entries.Count;
        RunningCount = runtime.Entries.Count(x => x.State == PackageState.Running);
        FailedCount = runtime.Entries.Count(x => x.State == PackageState.Failed) + runtime.DiscoveryFailures.Length;
    }
    public string ProductName => ApplicationInfo.ProductName;
    public string Version => ApplicationInfo.Version;
    public string BuildNumber => ApplicationInfo.BuildNumber;
    public int InstalledCount { get; }
    public int RunningCount { get; }
    public int FailedCount { get; }
}

public sealed class MarketplaceViewModel(IPackageCatalog catalog)
{
    public string Status => catalog is PlaceholderPackageCatalog ? "市场服务尚未接入" : "扩展市场";
    public string Detail => "第二阶段保留安全边界；联网、签名校验和安装将在后续迭代接入。";
}

public sealed partial class PluginManagerViewModel : ObservableObject, IDisposable
{
    private readonly ExtensionRuntime runtime;
    private readonly ISettingsService settings;

    public PluginManagerViewModel(ExtensionRuntime runtime, ISettingsService settings)
    {
        this.runtime = runtime;
        this.settings = settings;
        Entries = new(runtime.Entries.Select(CreateEntry));
        Failures = new(runtime.DiscoveryFailures.Select(x => new DiscoveryFailureViewModel(x)));
        runtime.EntriesChanged += Runtime_OnEntriesChanged;
    }

    public ObservableCollection<PluginEntryViewModel> Entries { get; }
    public ObservableCollection<DiscoveryFailureViewModel> Failures { get; }
    public bool HasFailures => Failures.Count > 0;

    public void Dispose()
    {
        runtime.EntriesChanged -= Runtime_OnEntriesChanged;
        GC.SuppressFinalize(this);
    }

    private PluginEntryViewModel CreateEntry(ExtensionRuntimeEntry entry) => new(entry, runtime, settings);

    private void Runtime_OnEntriesChanged(object? sender, EventArgs e)
    {
        void Refresh()
        {
            foreach (var snapshot in runtime.Entries)
            {
                var existing = Entries.FirstOrDefault(x => string.Equals(x.PackageId, snapshot.Package.Manifest.Id, StringComparison.OrdinalIgnoreCase));
                if (existing is null) Entries.Add(CreateEntry(snapshot));
                else existing.Refresh(snapshot);
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Refresh();
        else _ = dispatcher.InvokeAsync(Refresh);
    }
}

public sealed partial class PluginEntryViewModel : ObservableObject
{
    private readonly ExtensionRuntime runtime;
    private readonly ISettingsService settings;
    private ExtensionRuntimeEntry entry;

    [ObservableProperty] private string state = string.Empty;
    [ObservableProperty] private string actionText = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool canToggle = true;
    [ObservableProperty] private string? errorMessage;

    public PluginEntryViewModel(ExtensionRuntimeEntry entry, ExtensionRuntime runtime, ISettingsService settings)
    {
        this.entry = entry;
        this.runtime = runtime;
        this.settings = settings;
        Refresh(entry);
    }

    public string Name => entry.Package.Manifest.DisplayName;
    public string PackageId => entry.Package.Manifest.Id;
    public string Version => entry.Package.Manifest.Version;
    public string Publisher => entry.Package.Manifest.Publisher;
    public string Description => entry.Package.Manifest.Description;

    public void Refresh(ExtensionRuntimeEntry snapshot)
    {
        entry = snapshot;
        State = snapshot.State switch
        {
            PackageState.Running => "运行中",
            PackageState.Failed => "启动失败",
            PackageState.Disabled => "已禁用",
            PackageState.Validated => "已验证",
            PackageState.Stopped => "已停止",
            PackageState.Stopping => "正在停止",
            _ => snapshot.State.ToString()
        };
        ActionText = snapshot.State switch { PackageState.Disabled => "启用", PackageState.Failed => "重试", _ => "禁用" };
        ErrorMessage = snapshot.FailureMessage;
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        CanToggle = false;
        ErrorMessage = null;
        var enable = entry.State is PackageState.Disabled or PackageState.Failed or PackageState.Stopped;
        try
        {
            await runtime.SetEnabledAsync(PackageId, enable);
            var disabled = (settings.Current.DisabledPackages ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (enable) disabled.Remove(PackageId); else disabled.Add(PackageId);
            await settings.SaveAsync(settings.Current with { DisabledPackages = disabled.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() });
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally { IsBusy = false; CanToggle = true; }
    }
}

public sealed partial class PluginWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly WpfNavigationRegistry registry;
    private readonly Dictionary<string, FrameworkElement> pageCache = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private PluginPageOption? selectedPage;
    [ObservableProperty] private FrameworkElement? currentPage;
    [ObservableProperty] private bool hasPages;

    public PluginWorkspaceViewModel(WpfNavigationRegistry registry)
    {
        this.registry = registry;
        Pages = [];
        Rebuild();
        registry.Changed += Registry_OnChanged;
    }

    public ObservableCollection<PluginPageOption> Pages { get; }

    partial void OnSelectedPageChanged(PluginPageOption? value)
    {
        if (value is null) { CurrentPage = null; return; }
        if (!pageCache.TryGetValue(value.Route, out var view))
        {
            view = value.CreateView();
            pageCache[value.Route] = view;
        }
        CurrentPage = view;
    }

    public void Dispose()
    {
        registry.Changed -= Registry_OnChanged;
        GC.SuppressFinalize(this);
    }

    private void Registry_OnChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Rebuild();
        else _ = dispatcher.InvokeAsync(Rebuild);
    }

    private void Rebuild()
    {
        var selectedRoute = SelectedPage?.Route;
        var activeRoutes = registry.Pages.Select(x => x.Page.Route).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleRoute in pageCache.Keys.Where(x => !activeRoutes.Contains(x)).ToArray()) pageCache.Remove(staleRoute);
        Pages.Clear();
        foreach (var item in registry.Pages)
            Pages.Add(new(item.PackageId, item.Page.Route, item.Page.Title, item.Page.IconGlyph, item.Page.CreateView));
        HasPages = Pages.Count > 0;
        SelectedPage = Pages.FirstOrDefault(x => string.Equals(x.Route, selectedRoute, StringComparison.OrdinalIgnoreCase)) ?? Pages.FirstOrDefault();
    }
}

public sealed record PluginPageOption(string PackageId, string Route, string Title, string IconGlyph, Func<FrameworkElement> CreateView);

public sealed partial class ThemeViewModel : ObservableObject
{
    private readonly IThemeService theme;
    private readonly IWindowBackdropService backdrop;
    private readonly ISettingsService settings;
    private readonly ILogger<ThemeViewModel> logger;

    [ObservableProperty] private ThemeDefinition selectedOption;
    [ObservableProperty] private BackdropOption selectedBackdrop;

    public ThemeViewModel(IThemeService theme, IWindowBackdropService backdrop, ISettingsService settings, ILogger<ThemeViewModel> logger)
    {
        this.theme = theme;
        this.backdrop = backdrop;
        this.settings = settings;
        this.logger = logger;
        Options = theme.AvailableThemes;
        BackdropOptions = backdrop.Options;
        selectedOption = theme.SelectedTheme;
        selectedBackdrop = BackdropOptions.First(x => x.Value == backdrop.Selection);
    }

    public IReadOnlyList<ThemeDefinition> Options { get; }
    public IReadOnlyList<BackdropOption> BackdropOptions { get; }

    partial void OnSelectedOptionChanged(ThemeDefinition value)
    {
        theme.Apply(value.Id);
        App.ApplyTheme(theme.Current);
        _ = SaveAppearanceAsync();
    }

    partial void OnSelectedBackdropChanged(BackdropOption value)
    {
        backdrop.Apply(value.Value);
        _ = SaveAppearanceAsync();
    }

    private async Task SaveAppearanceAsync()
    {
        try
        {
            await settings.SaveAsync(settings.Current with
            {
                Theme = theme.SelectedThemeId,
                Backdrop = backdrop.Selection.ToString().ToLowerInvariant()
            });
        }
        catch (Exception exception) { logger.LogWarning(exception, "The appearance selection could not be saved."); }
    }
}

public sealed class SoftwareSettingsViewModel
{
    public string Version => ApplicationInfo.Version;
    public string BuildNumber => ApplicationInfo.BuildNumber;
    public string DataPolicy => "设置与日志仅保存在当前 Windows 用户的本地目录。";
}

public sealed record DiscoveryFailureViewModel(string Path, string Code, string Message)
{
    public DiscoveryFailureViewModel(PackageDiscoveryFailure failure) : this(failure.Path, failure.Code, failure.Message) { }
}
