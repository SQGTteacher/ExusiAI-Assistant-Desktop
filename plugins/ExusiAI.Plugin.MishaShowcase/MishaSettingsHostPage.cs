using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ExusiAI.Plugin.MishaShowcase;

/// <summary>
/// ClassIsland-style settings host: a single plugin entry with a persistent left navigation pane
/// and a content surface on the right. This mirrors the Misha settings-window information
/// architecture instead of exposing every settings page as a separate host plugin card.
/// </summary>
internal sealed class MishaSettingsHostPage : UserControl
{
    private readonly Dictionary<string, FrameworkElement> pageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ContentControl content = new();
    private readonly TextBlock sectionTitle = new()
    {
        FontSize = 20,
        FontWeight = FontWeights.SemiBold
    };

    public MishaSettingsHostPage(MishaPlatformStore store)
    {
        var pages = new[]
        {
            new MishaSection("overview", "概览", "◫", () => new MishaDashboardPage(store)),
            new MishaSection("schedule", "课表与时间表", "▦", () => new MishaSchedulePage(store)),
            new MishaSection("components", "组件", "◩", () => new MishaComponentsPage(store)),
            new MishaSection("automation", "提醒与自动化", "⚡", () => new MishaAutomationPage(store)),
            new MishaSection("extensions", "扩展", "⊞", () => new MishaExtensionsPage(store)),
            new MishaSection("data", "档案与数据", "⇄", () => new MishaDataPage(store)),
            new MishaSection("about", "关于", "ⓘ", static () => new MishaAboutPage())
        };

        var navigation = new ListBox
        {
            ItemsSource = pages,
            DisplayMemberPath = nameof(MishaSection.Title),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(7, 10, 7, 10)
        };
        navigation.SetResourceReference(ListBox.StyleProperty, "NavigationList");
        navigation.ItemTemplate = BuildNavigationTemplate();

        var pane = new Border
        {
            Width = 205,
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

        var contentHost = new Border { Padding = new Thickness(22) };
        contentHost.SetResourceReference(Border.BackgroundProperty, "AppBackgroundBrush");
        contentHost.Child = content;
        Grid.SetRow(contentHost, 1);
        right.Children.Add(contentHost);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
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
        icon.SetValue(TextBlock.WidthProperty, 28d);
        icon.SetValue(TextBlock.FontSizeProperty, 15d);
        stack.AppendChild(icon);

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(MishaSection.Title)));
        title.SetValue(TextBlock.FontSizeProperty, 13d);
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        title.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        stack.AppendChild(title);

        template.VisualTree = stack;
        return template;
    }

    private sealed record MishaSection(string Id, string Title, string Icon, Func<FrameworkElement> CreateView);
}
