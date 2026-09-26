using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
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

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly ExtensionRuntime runtime;

    [ObservableProperty] private int installedCount;
    [ObservableProperty] private int runningCount;
    [ObservableProperty] private int failedCount;

    public HomeViewModel(ExtensionRuntime runtime)
    {
        this.runtime = runtime;
        RefreshRuntimeSummary();
        runtime.EntriesChanged += Runtime_OnEntriesChanged;
    }

    public string ProductName => ApplicationInfo.ProductName;
    public string Version => ApplicationInfo.Version;
    public string BuildNumber => ApplicationInfo.BuildNumber;

    private void Runtime_OnEntriesChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            RefreshRuntimeSummary();
        else
            _ = dispatcher.InvokeAsync(RefreshRuntimeSummary);
    }

    private void RefreshRuntimeSummary()
    {
        InstalledCount = runtime.Entries.Count;
        RunningCount = runtime.Entries.Count(x => x.State == PackageState.Running);
        FailedCount = runtime.Entries.Count(x => x.State == PackageState.Failed) + runtime.DiscoveryFailures.Length;
    }
}

public sealed partial class MarketplaceViewModel : ObservableObject
{
    private readonly IPackageCatalog catalog;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private int packageCount;
    [ObservableProperty] private int publisherCount;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;

    public MarketplaceViewModel(IPackageCatalog catalog)
    {
        this.catalog = catalog;
        Packages = [];
        _ = RefreshAsync();
    }

    public ObservableCollection<CatalogPackageViewModel> Packages { get; }

    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await catalog.SearchAsync(SearchText);
            Packages.Clear();
            foreach (var package in result) Packages.Add(new(package));
            PackageCount = Packages.Count;
            PublisherCount = Packages.Select(x => x.Publisher).Distinct(StringComparer.CurrentCultureIgnoreCase).Count();
        }
        catch (Exception exception) { ErrorMessage = exception.Message; }
        finally { IsBusy = false; }
    }
}

public sealed class CatalogPackageViewModel(PackageManifest manifest)
{
    public string Name => manifest.DisplayName;
    public string PackageId => manifest.Id;
    public string Version => manifest.Version;
    public string Publisher => manifest.Publisher;
    public string Description => manifest.Description;
    public string Type => manifest.Type.ToString();
    public string ApiVersion => manifest.ApiVersion;
    public int PermissionCount => manifest.Permissions.Length;
    public int DependencyCount => manifest.Dependencies.Length;
}

public sealed partial class PluginManagerViewModel : ObservableObject, IDisposable
{
    private readonly ExtensionRuntime runtime;
    private readonly ISettingsService settings;
    private readonly PluginPackageManager packageManager;
    [ObservableProperty] private string? operationMessage;
    [ObservableProperty] private bool isManagingPackage;

    public PluginManagerViewModel(ExtensionRuntime runtime, ISettingsService settings, PluginPackageManager packageManager)
    {
        this.runtime = runtime;
        this.settings = settings;
        this.packageManager = packageManager;
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

    private PluginEntryViewModel CreateEntry(ExtensionRuntimeEntry entry) => new(entry, runtime, settings, packageManager, message => OperationMessage = message);

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (IsManagingPackage) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "导入 ExusiAI 插件", Filter = "插件压缩包 (*.zip)|*.zip" };
        if (dialog.ShowDialog() != true) return;
        IsManagingPackage = true;
        try
        {
            var manifest = await packageManager.ImportAsync(dialog.FileName);
            OperationMessage = $"已安装并启动 {manifest.DisplayName} {manifest.Version}。";
        }
        catch (Exception exception) { OperationMessage = $"导入失败：{exception.Message}"; }
        finally { IsManagingPackage = false; }
    }

    [RelayCommand]
    private void OpenPackagesFolder()
    {
        Directory.CreateDirectory(packageManager.PackagesDirectory);
        Process.Start(new ProcessStartInfo(packageManager.PackagesDirectory) { UseShellExecute = true });
        OperationMessage = "也可以将包含 package.json 的插件文件夹直接放入此目录，重启后自动加载。";
    }

    private void Runtime_OnEntriesChanged(object? sender, EventArgs e)
    {
        void Refresh()
        {
            var currentIds = runtime.Entries.Select(x => x.Package.Manifest.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in Entries.Where(x => !currentIds.Contains(x.PackageId)).ToArray()) Entries.Remove(stale);
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
    private readonly PluginPackageManager packageManager;
    private readonly Action<string> report;

    [ObservableProperty] private string state = string.Empty;
    [ObservableProperty] private string actionText = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool canToggle = true;
    [ObservableProperty] private string? errorMessage;

    public PluginEntryViewModel(ExtensionRuntimeEntry entry, ExtensionRuntime runtime, ISettingsService settings, PluginPackageManager packageManager, Action<string> report)
    {
        this.entry = entry;
        this.runtime = runtime;
        this.settings = settings;
        this.packageManager = packageManager;
        this.report = report;
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

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsBusy) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出插件",
            Filter = "插件压缩包 (*.zip)|*.zip",
            FileName = $"{PackageId}-{Version}.zip"
        };
        if (dialog.ShowDialog() != true) return;
        IsBusy = true;
        try { await packageManager.ExportAsync(PackageId, dialog.FileName); report($"{Name} 已导出为 ZIP 插件包。 "); }
        catch (Exception exception) { ErrorMessage = exception.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UninstallAsync()
    {
        if (IsBusy) return;
        if (MessageBox.Show($"确定卸载“{Name}”吗？插件设置将保留，之后可以重新导入。", "卸载插件", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        IsBusy = true;
        CanToggle = false;
        try { await packageManager.UninstallAsync(PackageId); report($"{Name} 已卸载。 "); }
        catch (Exception exception) { ErrorMessage = $"卸载失败：{exception.Message}"; }
        finally { IsBusy = false; CanToggle = true; }
    }
}

public sealed partial class PluginWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly WpfNavigationRegistry registry;
    private readonly ICrashReporter crashReporter;
    private readonly Dictionary<string, FrameworkElement> pageCache = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private PluginPageOption? selectedPage;
    [ObservableProperty] private FrameworkElement? currentPage;
    [ObservableProperty] private bool hasPages;

    public PluginWorkspaceViewModel(WpfNavigationRegistry registry, ICrashReporter crashReporter)
    {
        this.registry = registry;
        this.crashReporter = crashReporter;
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
            try { view = value.CreateView(); }
            catch (Exception exception) { view = crashReporter.CreateErrorPage(exception, $"创建插件页面 {value.PackageId}/{value.Route}"); }
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
        OptionsView = CollectionViewSource.GetDefaultView(Options);
        OptionsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ThemeDefinition.ModeGroup)));
        BackdropOptions = backdrop.Options;
        selectedOption = theme.SelectedTheme;
        selectedBackdrop = BackdropOptions.First(x => x.Value == backdrop.Selection);
    }

    public IReadOnlyList<ThemeDefinition> Options { get; }
    public ICollectionView OptionsView { get; }
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

public sealed partial class SoftwareSettingsViewModel : ObservableObject
{
    private readonly IAppPaths paths;
    [ObservableProperty] private string? lastActionMessage;

    public SoftwareSettingsViewModel(IAppPaths paths, ExtensionRuntime runtime)
    {
        this.paths = paths;
        InstalledPackageCount = runtime.Entries.Count;
    }

    public string Version => ApplicationInfo.Version;
    public string BuildNumber => ApplicationInfo.BuildNumber;
    public string Runtime => RuntimeInformation.FrameworkDescription;
    public string OperatingSystem => RuntimeInformation.OSDescription;
    public string Architecture => RuntimeInformation.ProcessArchitecture.ToString();
    public string DataDirectory => paths.UserDataDirectory;
    public string PackagesDirectory => paths.PackagesDirectory;
    public string LogsDirectory => paths.LogsDirectory;
    public int InstalledPackageCount { get; }
    public string DataPolicy => "设置与日志仅保存在当前 Windows 用户的本地目录。";

    [RelayCommand] private void OpenDataDirectory() => OpenDirectory(paths.UserDataDirectory);
    [RelayCommand] private void OpenPackagesDirectory() => OpenDirectory(paths.PackagesDirectory);
    [RelayCommand] private void OpenLogsDirectory() => OpenDirectory(paths.LogsDirectory);

    private void OpenDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            LastActionMessage = $"已打开：{path}";
        }
        catch (Exception exception) { LastActionMessage = $"无法打开目录：{exception.Message}"; }
    }
}

public sealed record DiscoveryFailureViewModel(string Path, string Code, string Message)
{
    public DiscoveryFailureViewModel(PackageDiscoveryFailure failure) : this(failure.Path, failure.Code, failure.Message) { }
}
