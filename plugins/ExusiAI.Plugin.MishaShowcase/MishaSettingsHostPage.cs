using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class MishaSettingsHostPage : UserControl
{
    private readonly Dictionary<string, FrameworkElement> pageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContentControl content = new();
    private readonly TextBlock sectionTitle = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };

    public MishaSettingsHostPage(MishaPlatformStore store)
    {
        var pages = new List<MishaSection>
        {
            new("workspace", "工作区", "⌂", () => new MishaWorkspacePage(store)),
            new("main-window", "信息岛外观与位置", "▤", () => new MishaMainWindowSettingsPage(store)),
            new("themes-native", "ClassIsland 主题", "色", () => new MishaThemeCompatibilityPage(store)),
            new("subjects", "科目", "字", () => new MishaSubjectsPage(store)),
            new("time-layouts", "时间表", "◷", () => new MishaTimeLayoutsPage(store)),
            new("class-plans", "课表", "▦", () => new MishaClassPlansPage(store)),
            new("class-plan-groups", "课表群", "群", () => new MishaClassPlanGroupsPage(store)),
            new("ordered-schedules", "预定课表", "日", () => new MishaOrderedSchedulesPage(store)),
            new("schedule-mode", "日程模式", "列", () => new MishaScheduleModePage(store)),
            new("temporary", "临时课表", "叠", () => new MishaTemporarySchedulePage(store)),
            new("profile-file", "档案与导入导出", "⇄", () => new MishaDataPage(store))
        };

        var settingIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["general"] = "⚙",
            ["clock"] = "◷",
            ["storage"] = "▣",
            ["privacy"] = "锁",
            ["refreshing"] = "新",
            ["advanced"] = "⋯",
            ["components-settings"] = "◩",
            ["appearance"] = "◐",
            ["notification"] = "铃",
            ["window"] = "□",
            ["weather"] = "☁",
            ["automation-settings"] = "⚡",
            ["update"] = "↻",
            ["plugins"] = "拼",
            ["themes"] = "色",
            ["management"] = "管",
            ["compat"] = "{}"
        };

        foreach (var category in MishaSettingsCatalog.Categories)
        {
            if (category.Id is "appearance" or "window" or "themes")
                continue;

            var captured = category;
            pages.Add(new MishaSection(
                "settings-" + category.Id,
                category.Title,
                settingIcons.GetValueOrDefault(category.Id, "·"),
                () => new MishaSettingsCategoryPage(store, captured)));
        }

        pages.AddRange(
        [
            new("components", "组件配置", "◩", () => new MishaComponentLayoutsPage(store)),
            new("automation", "自动化配置", "⚡", () => new MishaAutomationEditorPage(store)),
            new("sync", "同步", "⇅", () => new MishaSyncPage(store)),
            new("about", "关于", "ⓘ", static () => new MishaAboutPage())
        ]);

        var navigation = new ListBox
        {
            ItemsSource = pages,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(7, 10, 7, 10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        navigation.SetResourceReference(ListBox.StyleProperty, "NavigationList");
        navigation.ItemTemplate = BuildNavigationTemplate();
        ScrollViewer.SetVerticalScrollBarVisibility(navigation, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(navigation, ScrollBarVisibility.Disabled);

        var pane = new Border
        {
            Width = 218,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = navigation
        };
        pane.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        pane.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition());

        var header = new Border
        {
            Padding = new Thickness(22, 16, 22, 12),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = sectionTitle
        };
        header.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        header.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        right.Children.Add(header);

        var contentHost = new Border { Padding = new Thickness(22, 18, 18, 18), Child = content };
        contentHost.SetResourceReference(Border.BackgroundProperty, "AppBackgroundBrush");
        Grid.SetRow(contentHost, 1);
        right.Children.Add(contentHost);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(218) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(pane);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);
        Content = root;

        navigation.SelectionChanged += (_, _) =>
        {
            if (navigation.SelectedItem is not MishaSection section) return;
            sectionTitle.Text = section.Title;
            if (!pageCache.TryGetValue(section.Id, out var page))
            {
                page = section.CreateView();
                pageCache[section.Id] = page;
            }
            content.Content = page;
        };

        navigation.SelectedIndex = 0;
    }

    private static DataTemplate BuildNavigationTemplate()
    {
        var template = new DataTemplate();
        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(MishaSection.Icon)));
        icon.SetValue(TextBlock.WidthProperty, 30d);
        icon.SetValue(TextBlock.FontSizeProperty, 13d);
        icon.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        stack.AppendChild(icon);

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(MishaSection.Title)));
        title.SetValue(TextBlock.FontSizeProperty, 12.5d);
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        title.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        stack.AppendChild(title);

        template.VisualTree = stack;
        return template;
    }

    private sealed record MishaSection(string Id, string Title, string Icon, Func<FrameworkElement> CreateView);
}
