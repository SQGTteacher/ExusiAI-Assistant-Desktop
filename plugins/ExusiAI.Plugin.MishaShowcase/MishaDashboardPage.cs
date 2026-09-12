using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class MishaDashboardPage : UserControl
{
    private readonly MishaDashboardViewModel viewModel = new();
    private readonly DispatcherTimer timer;
    private readonly StackPanel detailArea;
    private readonly Button compactButton;
    private bool compact;

    public MishaDashboardPage()
    {
        DataContext = viewModel;
        timer = new() { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => viewModel.Tick(DateTime.Now);
        Loaded += (_, _) => { viewModel.Tick(DateTime.Now); timer.Start(); };
        Unloaded += (_, _) => timer.Stop();

        compactButton = SecondaryButton("紧凑模式");
        compactButton.Click += (_, _) => ToggleCompact();
        var nextButton = PrimaryButton("模拟下一节");
        nextButton.Click += (_, _) => viewModel.SimulateNextLesson();
        var resetButton = SecondaryButton("恢复实时");
        resetButton.Click += (_, _) => viewModel.ResetSimulation();

        detailArea = BuildDetailArea();
        var root = new StackPanel();
        root.Children.Add(BuildHeader(compactButton, nextButton, resetButton));
        root.Children.Add(BuildLessonSummary());
        root.Children.Add(detailArea);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private FrameworkElement BuildHeader(params Button[] actions)
    {
        var grid = new Grid { Margin = new(0, 0, 0, 14) };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(Text("米沙平台兼容范本", 26, FontWeights.SemiBold));
        title.Children.Add(Text("独立实现 · 用于验证 ExusiAI 插件工作台与生命周期", 12, FontWeights.Normal, "TextSecondaryBrush", new(0, 5, 0, 0)));
        grid.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in actions) { action.Margin = new(6, 0, 0, 0); buttons.Children.Add(action); }
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    private FrameworkElement BuildLessonSummary()
    {
        var panel = new Grid { Margin = new(0, 0, 0, 14) };
        panel.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });

        var current = new StackPanel();
        current.Children.Add(Text("当前状态", 11, FontWeights.SemiBold, "TextSecondaryBrush"));
        current.Children.Add(BoundText(nameof(MishaDashboardViewModel.CurrentSubject), 25, FontWeights.SemiBold, new(0, 9, 0, 0)));
        current.Children.Add(BoundText(nameof(MishaDashboardViewModel.CurrentDetail), 12, FontWeights.Normal, new(0, 5, 0, 0), "TextSecondaryBrush"));
        var currentCard = Card(current, "AccentSoftBrush");
        currentCard.Margin = new(0, 0, 7, 0);
        panel.Children.Add(currentCard);

        var next = new StackPanel();
        next.Children.Add(Text("接下来", 11, FontWeights.SemiBold, "TextSecondaryBrush"));
        next.Children.Add(BoundText(nameof(MishaDashboardViewModel.NextSubject), 25, FontWeights.SemiBold, new(0, 9, 0, 0)));
        next.Children.Add(BoundText(nameof(MishaDashboardViewModel.NextDetail), 12, FontWeights.Normal, new(0, 5, 0, 0), "TextSecondaryBrush"));
        var nextCard = Card(next, "SurfaceAltBrush");
        nextCard.Margin = new(7, 0, 0, 0);
        Grid.SetColumn(nextCard, 1);
        panel.Children.Add(nextCard);
        return panel;
    }

    private StackPanel BuildDetailArea()
    {
        var area = new StackPanel();
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) });

        var schedule = new StackPanel();
        schedule.Children.Add(Text("今日课表", 17, FontWeights.SemiBold));
        foreach (var lesson in viewModel.Lessons)
        {
            var row = new Grid { Margin = new(0, 10, 0, 0) };
            row.ColumnDefinitions.Add(new() { Width = new(38) });
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(Text(lesson.Index.ToString("D2"), 14, FontWeights.SemiBold, "AccentBrush"));
            var subject = Text(lesson.Subject, 14, FontWeights.SemiBold);
            Grid.SetColumn(subject, 1); row.Children.Add(subject);
            var metadata = Text($"{lesson.Start:hh\\:mm}–{lesson.End:hh\\:mm}  {lesson.Teacher}", 11, FontWeights.Normal, "TextSecondaryBrush");
            Grid.SetColumn(metadata, 2); row.Children.Add(metadata);
            schedule.Children.Add(row);
        }
        var scheduleCard = Card(schedule, "SurfaceBrush");
        scheduleCard.Margin = new(0, 0, 7, 0);
        columns.Children.Add(scheduleCard);

        var side = new StackPanel { Margin = new(7, 0, 0, 0) };
        var time = new StackPanel();
        time.Children.Add(BoundText(nameof(MishaDashboardViewModel.TimeText), 31, FontWeights.SemiBold));
        time.Children.Add(BoundText(nameof(MishaDashboardViewModel.DateText), 11, FontWeights.Normal, new(0, 4, 0, 0), "TextSecondaryBrush"));
        var progress = new ProgressBar { Height = 4, Minimum = 0, Maximum = 100, Margin = new(0, 13, 0, 0) };
        progress.SetBinding(ProgressBar.ValueProperty, new Binding(nameof(MishaDashboardViewModel.DayProgress)));
        time.Children.Add(progress);
        side.Children.Add(Card(time, "SurfaceBrush"));

        var countdown = new StackPanel();
        countdown.Children.Add(Text("距离 2027 高考", 11, FontWeights.SemiBold, "TextSecondaryBrush"));
        countdown.Children.Add(BoundText(nameof(MishaDashboardViewModel.CountdownText), 20, FontWeights.SemiBold, new(0, 7, 0, 0), "AccentBrush"));
        var countdownCard = Card(countdown, "SurfaceBrush");
        countdownCard.Margin = new(0, 10, 0, 0);
        side.Children.Add(countdownCard);

        var notice = new StackPanel();
        notice.Children.Add(Text("课堂提醒", 11, FontWeights.SemiBold, "TextSecondaryBrush"));
        notice.Children.Add(Text("下课前请整理讲台设备，并确认课件已保存。", 13, FontWeights.Normal, null, new(0, 7, 0, 0)));
        var noticeCard = Card(notice, "AccentSoftBrush");
        noticeCard.Margin = new(0, 10, 0, 0);
        side.Children.Add(noticeCard);
        Grid.SetColumn(side, 1);
        columns.Children.Add(side);
        area.Children.Add(columns);
        return area;
    }

    private void ToggleCompact()
    {
        compact = !compact;
        detailArea.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        compactButton.Content = compact ? "完整模式" : "紧凑模式";
    }

    private static Border Card(UIElement child, string backgroundResource)
    {
        var border = new Border { Child = child, Padding = new(16), CornerRadius = new(7), BorderThickness = new(1) };
        border.SetResourceReference(Border.BackgroundProperty, backgroundResource);
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return border;
    }

    private static TextBlock Text(string value, double size, FontWeight weight, string? foregroundResource = null, Thickness? margin = null)
    {
        var text = new TextBlock { Text = value, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new(0) };
        if (foregroundResource is not null) text.SetResourceReference(TextBlock.ForegroundProperty, foregroundResource);
        return text;
    }

    private static TextBlock BoundText(string property, double size, FontWeight weight, Thickness? margin = null, string? foregroundResource = null)
    {
        var text = Text(string.Empty, size, weight, foregroundResource, margin);
        text.SetBinding(TextBlock.TextProperty, new Binding(property));
        return text;
    }

    private static Button PrimaryButton(string content) => new() { Content = content, Padding = new(13, 7, 13, 7) };

    private static Button SecondaryButton(string content)
    {
        var button = PrimaryButton(content);
        button.SetResourceReference(Button.BackgroundProperty, "SurfaceAltBrush");
        button.SetResourceReference(Button.ForegroundProperty, "TextPrimaryBrush");
        return button;
    }
}

internal sealed class MishaReferencePage : UserControl
{
    public MishaReferencePage()
    {
        var content = new StackPanel();
        content.Children.Add(Text("范本说明", 26, FontWeights.SemiBold));
        content.Children.Add(Text("用于检验一个真实插件需要经过的完整宿主路径。", 12, FontWeights.Normal, "TextSecondaryBrush", new(0, 5, 0, 18)));
        content.Children.Add(Section("来源基线", "参考 ClassIsland develop/v2/misha-alpha 分支在 b61a0353 提交中呈现的组件化课表理念。当前插件是独立 WPF 实现，不加载或复制 ClassIsland 程序代码。"));
        content.Children.Add(Section("运行时覆盖", "清单发现 → 兼容性校验 → 无锁程序集加载 → 初始化与启动 → 两个页面注册 → 工作台切换 → 禁用与卸载。"));
        content.Children.Add(Section("界面覆盖", "动态主题画刷、实时数据绑定、秒级 DispatcherTimer、课程状态计算、列表排版、进度显示、交互按钮和紧凑模式。"));
        content.Children.Add(Section("维护边界", "范本没有联网、文件写入、遥测或 ClassIsland API 依赖。后续课堂扩展可以复制此项目结构，但应将业务模型和持久化服务拆分为独立文件。"));
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static Border Section(string title, string detail)
    {
        var panel = new StackPanel();
        panel.Children.Add(Text(title, 16, FontWeights.SemiBold));
        panel.Children.Add(Text(detail, 13, FontWeights.Normal, "TextSecondaryBrush", new(0, 6, 0, 0)));
        var card = new Border { Child = panel, Padding = new(17), CornerRadius = new(7), BorderThickness = new(1), Margin = new(0, 0, 0, 10) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return card;
    }

    private static TextBlock Text(string value, double size, FontWeight weight, string? resource = null, Thickness? margin = null)
    {
        var text = new TextBlock { Text = value, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new(0) };
        if (resource is not null) text.SetResourceReference(TextBlock.ForegroundProperty, resource);
        return text;
    }
}
