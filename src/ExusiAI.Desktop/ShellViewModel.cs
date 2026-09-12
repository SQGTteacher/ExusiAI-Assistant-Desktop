using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using ExusiAI.Core;

namespace ExusiAI.Desktop;

public sealed record NavigationItem(string Route, string Title, string IconData);

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly PageFactory pageFactory;
    private readonly Dictionary<string, FrameworkElement> pageCache = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private NavigationItem? selectedItem;
    [ObservableProperty] private FrameworkElement? currentPage;

    public ShellViewModel(PageFactory pageFactory)
    {
        this.pageFactory = pageFactory;
        NavigationItems = new([
            new("home", "版本首页", "M3,3 H9 V9 H3 Z M15,3 H21 V9 H15 Z M3,15 H9 V21 H3 Z M15,15 H21 V21 H15 Z"),
            new("workspace", "插件工作台", "M4,5 H20 V19 H4 Z M4,9 H20 M9,9 V19"),
            new("marketplace", "扩展市场", "M4,7 H20 V20 H4 Z M8,7 V5 A4,4 0 0 1 16,5 V7 M8,12 H16"),
            new("extensions", "扩展管理", "M12,3 A3,3 0 1 1 12,9 A3,3 0 1 1 12,3 M5,13 H19 V21 H5 Z"),
            new("theme", "外观与主题", "M12,3 A9,9 0 1 0 12,21 C14,21 15,20 15,18 C15,16 13,16 13,14 C13,12 15,11 17,11 H21"),
            new("settings", "软件设置", "M12,8 A4,4 0 1 1 12,16 A4,4 0 1 1 12,8 M12,3 V5 M12,19 V21 M3,12 H5 M19,12 H21 M5.6,5.6 L7,7 M17,17 L18.4,18.4 M18.4,5.6 L17,7 M7,17 L5.6,18.4")
        ]);
        SelectedItem = NavigationItems[0];
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }
    public string PreviewVersion => ApplicationInfo.PreviewLabel;

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
}
