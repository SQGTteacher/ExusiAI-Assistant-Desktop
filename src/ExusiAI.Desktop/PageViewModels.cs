using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ExusiAI.Core;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.Runtime;
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
    public int InstalledCount { get; }
    public int RunningCount { get; }
    public int FailedCount { get; }
}

public sealed class MarketplaceViewModel(IPackageCatalog catalog)
{
    public string Status => catalog is PlaceholderPackageCatalog ? "市场服务尚未接入" : "扩展市场";
    public string Detail => "第一阶段只定义目录边界，不进行联网、下载或安装。";
}

public sealed class PluginManagerViewModel
{
    public PluginManagerViewModel(ExtensionRuntime runtime)
    {
        Entries = new(runtime.Entries.Select(x => new PluginEntryViewModel(x)));
        Failures = new(runtime.DiscoveryFailures.Select(x => new DiscoveryFailureViewModel(x)));
    }
    public ObservableCollection<PluginEntryViewModel> Entries { get; }
    public ObservableCollection<DiscoveryFailureViewModel> Failures { get; }
    public bool HasFailures => Failures.Count > 0;
}

public sealed partial class ThemeViewModel : ObservableObject
{
    private readonly IThemeService theme;
    private readonly ISettingsService settings;
    private readonly ILogger<ThemeViewModel> logger;

    [ObservableProperty] private ThemeOption selectedOption;

    public ThemeViewModel(IThemeService theme, ISettingsService settings, ILogger<ThemeViewModel> logger)
    {
        this.theme = theme;
        this.settings = settings;
        this.logger = logger;
        Options = [new(ThemeSelection.System, "跟随系统", "随 Windows 深浅色自动切换"), new(ThemeSelection.Light, "浅色", "明亮、清晰的课堂界面"), new(ThemeSelection.Dark, "深色", "降低暗光环境下的视觉刺激")];
        selectedOption = Options.First(x => x.Value == theme.Selection);
    }

    public IReadOnlyList<ThemeOption> Options { get; }

    partial void OnSelectedOptionChanged(ThemeOption value)
    {
        theme.Apply(value.Value);
        App.ApplyTheme(theme.Current);
        _ = SaveSelectionAsync(value.Value);
    }

    private async Task SaveSelectionAsync(ThemeSelection selection)
    {
        try { await settings.SaveAsync(new(Theme: selection.ToString().ToLowerInvariant())); }
        catch (Exception exception) { logger.LogWarning(exception, "The theme selection could not be saved."); }
    }
}

public sealed class SoftwareSettingsViewModel
{
    public string Version => ApplicationInfo.Version;
    public string DataPolicy => "设置与日志仅保存在当前 Windows 用户的本地目录。";
}

public sealed record ThemeOption(ThemeSelection Value, string Name, string Description);
public sealed record DiscoveryFailureViewModel(string Path, string Code, string Message)
{
    public DiscoveryFailureViewModel(PackageDiscoveryFailure failure) : this(failure.Path, failure.Code, failure.Message) { }
}

public sealed class PluginEntryViewModel(ExtensionRuntimeEntry entry)
{
    public string Name => entry.Package.Manifest.DisplayName;
    public string PackageId => entry.Package.Manifest.Id;
    public string Version => entry.Package.Manifest.Version;
    public string Publisher => entry.Package.Manifest.Publisher;
    public string Description => entry.Package.Manifest.Description;
    public string Type => entry.Package.Manifest.Type.ToString();
    public string State => entry.State switch { PackageState.Running => "运行中", PackageState.Failed => "启动失败", PackageState.Validated => "已验证", PackageState.Stopped => "已停止", _ => entry.State.ToString() };
}
