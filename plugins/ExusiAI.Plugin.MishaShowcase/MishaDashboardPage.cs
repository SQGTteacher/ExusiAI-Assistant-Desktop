using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class MishaDashboardPage : UserControl
{
    private readonly MishaDashboardViewModel viewModel;
    private readonly DispatcherTimer timer;

    public MishaDashboardPage(MishaPlatformStore store)
    {
        viewModel = new(store);
        DataContext = viewModel;

        timer = new() { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => viewModel.Tick(DateTime.Now);
        Loaded += (_, _) => { viewModel.RefreshLessons(); viewModel.Tick(DateTime.Now); timer.Start(); };
        Unloaded += (_, _) => timer.Stop();

        var root = new StackPanel { Margin = new Thickness(0, 0, 12, 24), MaxWidth = 980 };

        var title = Bound(nameof(MishaDashboardViewModel.SchoolName), 26, FontWeights.SemiBold);
        root.Children.Add(title);
        root.Children.Add(Bound(nameof(MishaDashboardViewModel.WorkspaceText), 11.5, FontWeights.Normal, "TextSecondaryBrush", new Thickness(0, 4, 0, 18)));

        root.Children.Add(Section("当前状态"));
        root.Children.Add(Bound(nameof(MishaDashboardViewModel.CurrentSubject), 28, FontWeights.SemiBold, null, new Thickness(0, 4, 0, 0)));
        root.Children.Add(Bound(nameof(MishaDashboardViewModel.CurrentDetail), 12, FontWeights.Normal, "TextSecondaryBrush", new Thickness(0, 5, 0, 12)));

        var nextGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        nextGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nextGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nextText = new StackPanel();
        nextText.Children.Add(Text("接下来", 12, FontWeights.SemiBold, "TextSecondaryBrush"));
        nextText.Children.Add(Bound(nameof(MishaDashboardViewModel.NextSubject), 20, FontWeights.SemiBold, null, new Thickness(0, 4, 0, 0)));
        nextText.Children.Add(Bound(nameof(MishaDashboardViewModel.NextDetail), 11.5, FontWeights.Normal, "TextSecondaryBrush", new Thickness(0, 3, 0, 0)));
        nextGrid.Children.Add(nextText);

        var time = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        time.Children.Add(Bound(nameof(MishaDashboardViewModel.TimeText), 27, FontWeights.Medium));
        time.Children.Add(Bound(nameof(MishaDashboardViewModel.DateText), 11, FontWeights.Normal, "TextSecondaryBrush", new Thickness(0, 3, 0, 0)));
        Grid.SetColumn(time, 1);
        nextGrid.Children.Add(time);
        root.Children.Add(nextGrid);
        root.Children.Add(new Separator());

        root.Children.Add(Section("今日课表"));
        var lessons = new DataGrid
        {
            ItemsSource = viewModel.Lessons,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 220
        };
        lessons.Columns.Add(new DataGridTextColumn { Header = "节次", Binding = new Binding(nameof(LessonItem.Index)), Width = 60 });
        lessons.Columns.Add(new DataGridTextColumn { Header = "科目", Binding = new Binding(nameof(LessonItem.Subject)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        lessons.Columns.Add(new DataGridTextColumn { Header = "教师", Binding = new Binding(nameof(LessonItem.Teacher)), Width = 110 });
        lessons.Columns.Add(new DataGridTextColumn { Header = "课表", Binding = new Binding(nameof(LessonItem.PlanName)), Width = 150 });
        lessons.Columns.Add(new DataGridTextColumn { Header = "开始", Binding = new Binding(nameof(LessonItem.Start)) { StringFormat = @"hh\:mm" }, Width = 80 });
        lessons.Columns.Add(new DataGridTextColumn { Header = "结束", Binding = new Binding(nameof(LessonItem.End)) { StringFormat = @"hh\:mm" }, Width = 80 });
        root.Children.Add(lessons);

        Content = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private static TextBlock Section(string title) =>
        Text(title, 16, FontWeights.SemiBold, null, new Thickness(0, 18, 0, 8));

    private static TextBlock Text(string value, double size, FontWeight weight, string? foreground = null, Thickness? margin = null)
    {
        var text = new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin ?? new Thickness(0)
        };
        if (foreground is not null) text.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        return text;
    }

    private static TextBlock Bound(string path, double size, FontWeight weight, string? foreground = null, Thickness? margin = null)
    {
        var text = Text("", size, weight, foreground, margin);
        text.SetBinding(TextBlock.TextProperty, new Binding(path));
        return text;
    }
}

internal sealed class MishaAboutPage : UserControl
{
    public MishaAboutPage()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 24), MaxWidth = 900 };
        panel.Children.Add(new TextBlock { Text = "关于 ClassIsland 2.2 Misha 移植", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Note("原项目：ClassIsland/ClassIsland；主要作者与维护者 HelloWRC 及 ClassIsland 开发团队、社区贡献者。"));
        panel.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 14) });
        panel.Children.Add(Row("移植者", "SQGTteacher"));
        panel.Children.Add(Row("上游基线", "develop/v2/misha-alpha"));
        panel.Children.Add(Row("许可", "按仓库 THIRD_PARTY_NOTICES 与 GPL/LGPL 边界保留原项目署名。"));
        panel.Children.Add(Row("配置策略", "直接读写真实 ClassIsland Settings.json、Profiles、ComponentLayouts 与 Automations；不生成示例配置。"));
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static TextBlock Note(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return block;
    }

    private static FrameworkElement Row(string key, string value)
    {
        var grid = new Grid { MinHeight = 54 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = key, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var text = Note(value);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }
}
