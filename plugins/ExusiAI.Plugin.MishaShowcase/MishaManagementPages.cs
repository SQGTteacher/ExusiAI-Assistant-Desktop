using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ExusiAI.Plugin.MishaShowcase;

internal static class MishaUi
{
    public static TextBlock Text(string value, double size = 13, FontWeight? weight = null, string? resource = null)
    {
        var text = new TextBlock { Text = value, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        if (resource is not null) text.SetResourceReference(TextBlock.ForegroundProperty, resource);
        return text;
    }

    public static StackPanel Header(string title, string subtitle)
    {
        var panel = new StackPanel { Margin = new(0, 0, 0, 16) };
        panel.Children.Add(Text(title, 26, FontWeights.SemiBold));
        var detail = Text(subtitle, 12, null, "TextSecondaryBrush");
        detail.Margin = new(0, 5, 0, 0);
        panel.Children.Add(detail);
        return panel;
    }

    public static Border Card(UIElement child, Thickness? margin = null)
    {
        var card = new Border { Child = child, Padding = new(17), CornerRadius = new(7), BorderThickness = new(1), Margin = margin ?? new(0, 0, 0, 10) };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return card;
    }

    public static Button Button(string text, bool secondary = false)
    {
        var button = new Button { Content = text, Padding = new(13, 7, 13, 7), Margin = new(0, 0, 7, 0) };
        if (secondary)
        {
            button.SetResourceReference(Button.BackgroundProperty, "SurfaceAltBrush");
            button.SetResourceReference(Button.ForegroundProperty, "TextPrimaryBrush");
        }
        return button;
    }

    public static TextBlock Status() => Text("所有更改均保存在本机插件档案中。", 11, null, "TextSecondaryBrush");
}

internal sealed class MishaSchedulePage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly DataGrid grid;
    private readonly TextBlock status = MishaUi.Status();

    public MishaSchedulePage(MishaPlatformStore store)
    {
        this.store = store;
        var root = new StackPanel();
        root.Children.Add(MishaUi.Header("课表与时间表", "编辑课程、教师、起止时间和轮换周；支持临时增删与启用状态。"));
        var profile = new Grid();
        profile.ColumnDefinitions.Add(new() { Width = new(110) });
        profile.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        profile.ColumnDefinitions.Add(new() { Width = new(90) });
        profile.ColumnDefinitions.Add(new() { Width = new(100) });
        profile.Children.Add(MishaUi.Text("当前档案", 13, FontWeights.SemiBold));
        var profileName = new TextBox { Text = store.State.ProfileName, Margin = new(8, 0, 14, 0) };
        Grid.SetColumn(profileName, 1); profile.Children.Add(profileName);
        var weekLabel = MishaUi.Text("轮换周", 13, FontWeights.SemiBold); Grid.SetColumn(weekLabel, 2); profile.Children.Add(weekLabel);
        var week = new ComboBox { ItemsSource = Enumerable.Range(1, 8), SelectedItem = store.State.CycleWeek };
        Grid.SetColumn(week, 3); profile.Children.Add(week);
        profileName.TextChanged += (_, _) => store.State.ProfileName = profileName.Text;
        week.SelectionChanged += (_, _) => store.State.CycleWeek = week.SelectedItem is int value ? value : 1;
        root.Children.Add(MishaUi.Card(profile));

        grid = new DataGrid { ItemsSource = store.State.Schedule, AutoGenerateColumns = true, CanUserAddRows = false, MinHeight = 310, HeadersVisibility = DataGridHeadersVisibility.Column };
        root.Children.Add(MishaUi.Card(grid));
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var add = MishaUi.Button("新增课程");
        add.Click += (_, _) => { store.State.Schedule.Add(new(store.State.Schedule.Count + 1, "新课程", "任课教师", "16:00", "16:40", store.State.CycleWeek, true)); grid.SelectedIndex = store.State.Schedule.Count - 1; };
        var remove = MishaUi.Button("删除选中", true);
        remove.Click += (_, _) => { if (grid.SelectedItem is ScheduleEntry entry) store.State.Schedule.Remove(entry); };
        var save = MishaUi.Button("保存课表");
        save.Click += async (_, _) => await SaveAsync();
        actions.Children.Add(add); actions.Children.Add(remove); actions.Children.Add(save); actions.Children.Add(status);
        root.Children.Add(actions);
        root.Children.Add(MishaUi.Card(MishaUi.Text("临时调课与预定：复制需要调整的课程行，修改日期/轮换周后启用；原课程可暂时取消 Enabled。跨天调整保存在同一档案，恢复时重新启用原课程。", 12, null, "TextSecondaryBrush"), new(0, 12, 0, 0)));
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async Task SaveAsync()
    {
        grid.CommitEdit(DataGridEditingUnit.Row, true);
        await store.SaveAsync();
        status.Text = $"已保存 · {DateTime.Now:HH:mm:ss}";
    }
}

internal sealed class MishaComponentsPage : UserControl
{
    public MishaComponentsPage(MishaPlatformStore store)
    {
        var root = new StackPanel();
        root.Children.Add(MishaUi.Header("组件与显示", "管理信息岛组件、多行布局、主题、自动隐藏和鼠标穿透。"));
        foreach (var component in store.State.Components)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new(90) }); row.ColumnDefinitions.Add(new() { Width = new(85) });
            row.Children.Add(MishaUi.Text(component.Name, 14, FontWeights.SemiBold));
            var enabled = new CheckBox { Content = "显示", IsChecked = component.Enabled }; enabled.Checked += (_, _) => component.Enabled = true; enabled.Unchecked += (_, _) => component.Enabled = false;
            Grid.SetColumn(enabled, 1); row.Children.Add(enabled);
            var rowSelect = new ComboBox { ItemsSource = new[] { 1, 2, 3 }, SelectedItem = component.Row }; rowSelect.SelectionChanged += (_, _) => component.Row = (int)(rowSelect.SelectedItem ?? 1);
            Grid.SetColumn(rowSelect, 2); row.Children.Add(rowSelect); root.Children.Add(MishaUi.Card(row));
        }
        var options = new StackPanel();
        var autoHide = new CheckBox { Content = "授课或全屏时自动隐藏", IsChecked = store.State.AutoHide, Margin = new(0, 0, 0, 8) };
        autoHide.Checked += (_, _) => store.State.AutoHide = true; autoHide.Unchecked += (_, _) => store.State.AutoHide = false;
        var mouse = new CheckBox { Content = "允许信息岛鼠标穿透", IsChecked = store.State.MouseThrough, Margin = new(0, 0, 0, 8) };
        mouse.Checked += (_, _) => store.State.MouseThrough = true; mouse.Unchecked += (_, _) => store.State.MouseThrough = false;
        var protect = new CheckBox { Content = "使用认证保护课表与设置", IsChecked = store.State.PasswordProtection };
        protect.Checked += (_, _) => store.State.PasswordProtection = true; protect.Unchecked += (_, _) => store.State.PasswordProtection = false;
        options.Children.Add(autoHide); options.Children.Add(mouse); options.Children.Add(protect);
        var theme = new ComboBox { ItemsSource = new[] { "跟随宿主", "明亮", "暗色", "高对比度" }, SelectedItem = store.State.Theme, Margin = new(0, 12, 0, 0) };
        theme.SelectionChanged += (_, _) => store.State.Theme = theme.SelectedItem?.ToString() ?? "跟随宿主"; options.Children.Add(theme);
        root.Children.Add(MishaUi.Card(options));
        var save = MishaUi.Button("保存组件布局"); save.Click += async (_, _) => await store.SaveAsync(); root.Children.Add(save);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}

internal sealed class MishaAutomationPage : UserControl
{
    public MishaAutomationPage(MishaPlatformStore store)
    {
        var root = new StackPanel(); root.Children.Add(MishaUi.Header("提醒与自动化", "在课程事件或指定时间触发提醒、语音、文件、应用、网页与显示行动。"));
        var grid = new DataGrid { ItemsSource = store.State.Automations, AutoGenerateColumns = true, CanUserAddRows = false, MinHeight = 300 };
        root.Children.Add(MishaUi.Card(grid));
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var add = MishaUi.Button("新增规则"); add.Click += (_, _) => store.State.Automations.Add(new("新自动化", "每天 08:00", "显示普通提醒", true));
        var remove = MishaUi.Button("删除选中", true); remove.Click += (_, _) => { if (grid.SelectedItem is AutomationEntry item) store.State.Automations.Remove(item); };
        var save = MishaUi.Button("保存规则"); save.Click += async (_, _) => { grid.CommitEdit(DataGridEditingUnit.Row, true); await store.SaveAsync(); };
        actions.Children.Add(add); actions.Children.Add(remove); actions.Children.Add(save); root.Children.Add(actions);
        root.Children.Add(MishaUi.Card(MishaUi.Text("强调提醒支持：提示音、语音播报、置顶和强调视觉效果。外部行动仅在用户明确配置后执行；插件默认规则不会启动程序或访问网络。", 12, null, "TextSecondaryBrush"), new(0, 12, 0, 0)));
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}

internal sealed class MishaExtensionsPage : UserControl
{
    public MishaExtensionsPage(MishaPlatformStore store)
    {
        var root = new StackPanel(); root.Children.Add(MishaUi.Header("内置扩展", "按需安装或卸载移植插件的内置功能模块，避免所有功能强制常驻。"));
        foreach (var extension in store.State.Extensions)
        {
            var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var info = new StackPanel(); info.Children.Add(MishaUi.Text(extension.Name, 15, FontWeights.SemiBold)); var detail = MishaUi.Text(extension.Description, 12, null, "TextSecondaryBrush"); detail.Margin = new(0, 4, 0, 0); info.Children.Add(detail); grid.Children.Add(info);
            var toggle = MishaUi.Button(extension.Installed ? "卸载" : "安装", extension.Installed); Grid.SetColumn(toggle, 1); grid.Children.Add(toggle);
            toggle.Click += async (_, _) => { extension.Installed = !extension.Installed; toggle.Content = extension.Installed ? "卸载" : "安装"; await store.SaveAsync(); };
            root.Children.Add(MishaUi.Card(grid));
        }
        root.Children.Add(MishaUi.Card(MishaUi.Text("这些模块随插件发行，不从未知地址下载代码；“安装”会启用模块并保存状态。第三方 ExusiAI 插件仍由宿主的扩展管理与本地资源库负责。", 12, null, "TextSecondaryBrush")));
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}

internal sealed class MishaDataPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly TextBlock status = MishaUi.Status();
    public MishaDataPage(MishaPlatformStore store)
    {
        this.store = store;
        var root = new StackPanel(); root.Children.Add(MishaUi.Header("档案与数据", "管理多周档案、天气和时间设置，并以 JSON 导入或导出完整配置。"));
        var settings = new StackPanel();
        settings.Children.Add(MishaUi.Text("天气位置", 12, FontWeights.SemiBold)); var city = new TextBox { Text = store.State.WeatherCity, Margin = new(0, 5, 0, 10) }; city.TextChanged += (_, _) => store.State.WeatherCity = city.Text; settings.Children.Add(city);
        var sync = new CheckBox { Content = "自动同步软件时间（也可手动对齐铃声）", IsChecked = store.State.TimeSync }; sync.Checked += (_, _) => store.State.TimeSync = true; sync.Unchecked += (_, _) => store.State.TimeSync = false; settings.Children.Add(sync);
        root.Children.Add(MishaUi.Card(settings));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var export = MishaUi.Button("导出档案"); export.Click += Export_OnClick;
        var import = MishaUi.Button("导入档案", true); import.Click += Import_OnClick;
        var save = MishaUi.Button("保存设置"); save.Click += async (_, _) => { await store.SaveAsync(); status.Text = "设置已保存。"; };
        buttons.Children.Add(export); buttons.Children.Add(import); buttons.Children.Add(save); buttons.Children.Add(status); root.Children.Add(buttons);
        root.Children.Add(MishaUi.Card(MishaUi.Text("表格/CSES 互操作由“CSES 互操作”内置扩展提供。本页 JSON 格式保存全部轮换周、课程、组件、自动化和扩展状态，写入采用临时文件替换以避免档案损坏。", 12, null, "TextSecondaryBrush"), new(0, 12, 0, 0)));
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async void Export_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "ExusiAI ClassIsland 档案 (*.json)|*.json", FileName = $"{store.State.ProfileName}.json" };
        if (dialog.ShowDialog() != true) return;
        try { await store.ExportAsync(dialog.FileName); status.Text = "档案已导出。"; } catch (Exception exception) { status.Text = $"导出失败：{exception.Message}"; }
    }

    private async void Import_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ExusiAI ClassIsland 档案 (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        try { await store.ImportAsync(dialog.FileName); status.Text = "档案已导入；重新打开页面即可刷新内容。"; } catch (Exception exception) { status.Text = $"导入失败：{exception.Message}"; }
    }
}
