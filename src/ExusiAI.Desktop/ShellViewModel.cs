using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExusiAI.Core;
using Wpf.Ui.Controls;

namespace ExusiAI.Desktop;

public sealed record NavigationItem(string Route, string Title, SymbolRegular Icon);

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
            new("home", "首页", SymbolRegular.Home24),
            new("workspace", "插件工作台", SymbolRegular.Grid24),
            new("marketplace", "资源库", SymbolRegular.Folder24),
            new("extensions", "扩展管理", SymbolRegular.Apps24),
            new("theme", "外观与主题", SymbolRegular.Color24),
            new("settings", "软件设置", SymbolRegular.Settings24)
        ]);
        SelectedItem = NavigationItems[0];
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }
    public string PreviewVersion => ApplicationInfo.PreviewLabel;

    [RelayCommand]
    private void Navigate(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return;

        var target = NavigationItems.FirstOrDefault(
            x => string.Equals(x.Route, route, StringComparison.OrdinalIgnoreCase));
        if (target is not null)
            SelectedItem = target;
    }

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
