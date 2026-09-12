using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Desktop;

public sealed record NavigationItem(string Route, string Title, string Icon, string? PackageId = null);

public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly PageFactory pageFactory;
    private readonly WpfNavigationRegistry registry;
    private readonly Dictionary<string, FrameworkElement> pageCache = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private NavigationItem? selectedItem;
    [ObservableProperty] private FrameworkElement? currentPage;

    public ShellViewModel(PageFactory pageFactory, WpfNavigationRegistry registry)
    {
        this.pageFactory = pageFactory;
        this.registry = registry;
        NavigationItems = new([
            new("home", "首页", "\uE80F"),
            new("marketplace", "扩展市场", "\uE719"),
            new("extensions", "扩展管理", "\uE74C"),
            new("theme", "主题设置", "\uE790"),
            new("settings", "软件设置", "\uE713")
        ]);
        AddExtensionPages();
        registry.Changed += Registry_OnChanged;
        SelectedItem = NavigationItems[0];
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        if (value is null) return;
        if (!pageCache.TryGetValue(value.Route, out var page))
        {
            page = pageFactory.Create(value.Route);
            pageCache[value.Route] = page;
        }
        CurrentPage = page;
    }

    public void Dispose()
    {
        registry.Changed -= Registry_OnChanged;
        GC.SuppressFinalize(this);
    }

    private void Registry_OnChanged(object? sender, EventArgs e)
    {
        foreach (var item in NavigationItems.Where(x => x.PackageId is not null).ToArray()) NavigationItems.Remove(item);
        AddExtensionPages();
    }

    private void AddExtensionPages()
    {
        foreach (var item in registry.Pages)
            NavigationItems.Add(new(item.Page.Route, item.Page.Title, item.Page.IconGlyph, item.PackageId));
    }
}
