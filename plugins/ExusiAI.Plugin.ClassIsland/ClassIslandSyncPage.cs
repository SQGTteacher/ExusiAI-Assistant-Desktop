using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ExusiAI.Plugin.ClassIsland;

internal sealed class ClassIslandSyncPage : UserControl
{
    private static Brush Accent => Resource("AccentBrush", "#D84B57");
    private static Brush AccentSoft => Resource("AccentSoftBrush", "#402126");
    private static Brush Ink => Resource("TextPrimaryBrush", "#F5F2EF");
    private static Brush Muted => Resource("TextSecondaryBrush", "#B2A9AD");
    private static Brush Surface => Resource("SurfaceBrush", "#17191F");
    private static Brush SurfaceAlt => Resource("SurfaceAltBrush", "#22252C");
    private static Brush BorderBrushValue => Resource("BorderBrush", "#3B3E46");

    private readonly ClassIslandCoreService core;
    private readonly Grid contentHost = new();
    private readonly Dictionary<string, Button> navigation = [];
    private TextBlock? operationStatus;

    public ClassIslandSyncPage(ClassIslandCoreService core)
    {
        this.core = core;
        MinWidth = 760;
        Background = Brushes.Transparent;
        Content = BuildShell();
        Loaded += (_, _) => ShowOverview();
    }

    private FrameworkElement BuildShell()
    {
        var frame = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = BorderBrushValue, Background = Surface };
        frame.SizeChanged += (_, _) => ApplyRoundedClip(frame, 12);
        var root = new Grid(); root.ColumnDefinitions.Add(new() { Width = new GridLength(Metric("TouchSidebarWidth", 210)) }); root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var side = new Border { Background = SurfaceAlt, CornerRadius = new CornerRadius(12, 0, 0, 12), BorderBrush = BorderBrushValue, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(8, 16, 8, 12) };
        var sideGrid = new Grid(); sideGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); sideGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); sideGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var title = new StackPanel { Margin = new Thickness(11, 0, 11, 16) };
        title.Children.Add(new TextBlock { Text = "ClassIsland", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Ink });
        title.Children.Add(new TextBlock { Text = "Misha · ExusiAI 原生插件", FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 3, 0, 0) });
        sideGrid.Children.Add(title);
        var menu = new StackPanel();
        menu.Children.Add(new TextBlock { Text = "通用", FontSize = 11, Foreground = Muted, Margin = new Thickness(12, 10, 0, 7) });
        AddNavigation(menu, "basic", "基本", ShowBasic);
        AddNavigation(menu, "clock", "时钟", ShowClock);
        menu.Children.Add(new TextBlock { Text = "主界面", FontSize = 11, Foreground = Muted, Margin = new Thickness(12, 10, 0, 7) });
        AddNavigation(menu, "overview", "信息岛", ShowOverview);
        AddNavigation(menu, "profile", "档案与课表", ShowProfile);
        AddNavigation(menu, "components", "组件", ShowComponents);
        AddNavigation(menu, "appearance", "外观", ShowAppearance);
        AddNavigation(menu, "window", "窗口", ShowWindow);
        menu.Children.Add(new TextBlock { Text = "服务", FontSize = 11, Foreground = Muted, Margin = new Thickness(12, 10, 0, 7) });
        AddNavigation(menu, "notifications", "提醒", ShowNotifications);
        AddNavigation(menu, "weather", "天气", ShowWeather);
        AddNavigation(menu, "sync", "同步", ShowSync);
        var menuScroll = new ScrollViewer { Content = menu, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(menuScroll, 1); sideGrid.Children.Add(menuScroll);
        var footer = new TextBlock { Text = "配置保持 ClassIsland JSON 兼容", Foreground = Muted, FontSize = 10, Margin = new Thickness(11, 10, 11, 0), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(footer, 2); sideGrid.Children.Add(footer); side.Child = sideGrid; root.Children.Add(side);
        contentHost.Margin = new Thickness(22, 18, 18, 18); Grid.SetColumn(contentHost, 1); root.Children.Add(contentHost); frame.Child = root;
        return frame;
    }

    private void ShowOverview()
    {
        Activate("overview");
        var body = Page("信息岛", "实时呈现当前课程、下一节课和精确时间；由 ExusiAI 生命周期直接管理。", out var stack);
        var preview = new Border { Height = 92, Margin = new Thickness(0, 8, 0, 18), Padding = new Thickness(24, 14, 24, 14), Background = SurfaceAlt, BorderBrush = BorderBrushValue, BorderThickness = new Thickness(1) };
        var safeRadius = Math.Min(core.Appearance.Settings.CornerRadius, 46); preview.CornerRadius = new CornerRadius(safeRadius);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = DateTime.Now.ToString(core.Appearance.Settings.ShowSeconds ? "HH:mm:ss" : "HH:mm"), FontSize = 29, FontWeight = FontWeights.SemiBold, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
        var lesson = CurrentLessonText(); var lessonStack = new StackPanel { Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        lessonStack.Children.Add(new TextBlock { Text = lesson.title, Foreground = Accent, FontSize = 17, FontWeight = FontWeights.SemiBold });
        lessonStack.Children.Add(new TextBlock { Text = lesson.detail, Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });
        Grid.SetColumn(lessonStack, 1); grid.Children.Add(lessonStack); preview.Child = grid; stack.Children.Add(preview);
        var actions = new WrapPanel();
        var toggle = Primary(core.Presentation.IsVisible ? "隐藏桌面信息岛" : "显示桌面信息岛");
        toggle.Click += (_, _) => { if (core.Presentation.IsVisible) core.Presentation.Stop(); else core.Presentation.Start(); ShowOverview(); };
        actions.Children.Add(toggle); var refresh = Secondary("刷新预览"); refresh.Click += (_, _) => ShowOverview(); actions.Children.Add(refresh); stack.Children.Add(actions);
        var profile = core.Profiles.Current;
        stack.Children.Add(Section("当前运行状态", profile is null ? "尚未导入档案。请前往“同步”导入 ClassIsland 备份或 Profile JSON。" :
            $"档案：{profile.Name}\n科目：{profile.Subjects.Count}　时间表：{profile.TimeLayouts.Count}　课表：{profile.ClassPlans.Count}　课表群：{profile.ClassPlanGroups.Count}\n组件方案：{core.Components.CurrentConfigName}　提醒队列：{(core.Notifications.Current is null ? "空闲" : core.Notifications.Current.State.ToString())}"));
        Present(body);
    }

    private void ShowBasic()
    {
        Activate("basic"); var body = Page("基本", "与 ClassIsland Settings.json 保持相同的学期日期字段。", out var stack);
        var anchor = new DatePicker
        {
            SelectedDate = core.Settings.SingleWeekStartTime?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today,
            Width = 190, MinHeight = Metric("TouchCompactTargetHeight", 40)
        };
        stack.Children.Add(Field("学期开始时间（轮换课表起点）", anchor));
        var save = Primary("保存并更新课表");
        save.Click += async (_, _) =>
        {
            if (anchor.SelectedDate is not { } selected) return;
            var date = DateOnly.FromDateTime(selected);
            await core.Settings.SaveGeneralAsync(date, core.Settings.TimeOffsetSeconds, core.Settings.ExactTimeServer);
            core.Timetable.RotationAnchor = date;
            ShowBasic();
        };
        stack.Children.Add(save); Present(body);
    }

    private void ShowClock()
    {
        Activate("clock"); var body = Page("时钟", "调整信息岛时间与课表时间的偏移值。", out var stack);
        stack.Children.Add(Section("当前时间", DateTime.Now.AddSeconds(core.Settings.TimeOffsetSeconds).ToString("yyyy年M月d日 HH:mm:ss")));
        var offset = Numeric(core.Settings.TimeOffsetSeconds);
        stack.Children.Add(Field("时间偏移（秒）", offset));
        var save = Primary("保存并应用");
        save.Click += async (_, _) =>
        {
            var date = core.Settings.SingleWeekStartTime ?? DateOnly.FromDateTime(DateTime.Today);
            var seconds = Parse(offset, core.Settings.TimeOffsetSeconds);
            if (!double.IsFinite(seconds)) return;
            await core.Settings.SaveGeneralAsync(date, Math.Clamp(seconds, -86400, 86400), core.Settings.ExactTimeServer);
            ShowClock();
        };
        stack.Children.Add(save); Present(body);
    }

    private void ShowProfile()
    {
        Activate("profile");
        var body = Page("档案与课表", "直接编辑 ClassIsland 原生 Subjects、TimeLayouts、ClassPlans 和 GUID 关联。", out var stack);
        var profile = core.Profiles.Current;
        if (profile is null) { stack.Children.Add(Section("没有档案", "请先从“同步”页导入 ClassIsland Profile JSON 或自动备份。")); Present(body); return; }
        var columns = new Grid(); columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        columns.Children.Add(BuildSubjectEditor(profile)); var layouts = BuildLayoutEditor(profile); Grid.SetColumn(layouts, 1); columns.Children.Add(layouts); var plans = BuildPlanEditor(profile); Grid.SetColumn(plans, 2); columns.Children.Add(plans);
        stack.Children.Add(columns);
        operationStatus = new TextBlock { Foreground = Accent, Margin = new Thickness(2, 12, 0, 0), TextWrapping = TextWrapping.Wrap }; stack.Children.Add(operationStatus);
        Present(body);
    }

    private FrameworkElement BuildSubjectEditor(ClassIslandProfile profile)
    {
        var panel = EditorPanel("科目"); var list = List(profile.Subjects.Select(x => new Keyed<ClassIslandSubject>(x.Key, x.Value)), nameof(Keyed<ClassIslandSubject>.Display)); panel.Children.Add(list);
        var name = Input("科目名称"); var teacher = Input("任课教师"); panel.Children.Add(name); panel.Children.Add(teacher);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is not Keyed<ClassIslandSubject> item) return; name.Text = item.Value.Name; teacher.Text = item.Value.TeacherName; };
        var buttons = new WrapPanel(); var add = Secondary("新增"); var save = Primary("保存"); var remove = Danger("删除");
        add.Click += (_, _) => { var id = Guid.NewGuid(); profile.Subjects[id] = new() { Name = "新科目" }; ShowProfile(); };
        save.Click += async (_, _) => { if (list.SelectedItem is not Keyed<ClassIslandSubject> item) return; item.Value.Name = name.Text.Trim(); item.Value.Initial = item.Value.Name.FirstOrDefault().ToString(); item.Value.TeacherName = teacher.Text.Trim(); await SaveProfileAsync(profile); ShowProfile(); };
        remove.Click += async (_, _) => { if (list.SelectedItem is not Keyed<ClassIslandSubject> item) return; profile.Subjects.Remove(item.Id); await SaveProfileAsync(profile); ShowProfile(); };
        buttons.Children.Add(add); buttons.Children.Add(save); buttons.Children.Add(remove); panel.Children.Add(buttons); return WrapEditor(panel);
    }

    private FrameworkElement BuildLayoutEditor(ClassIslandProfile profile)
    {
        var panel = EditorPanel("时间表"); var list = List(profile.TimeLayouts.Select(x => new Keyed<ClassIslandTimeLayout>(x.Key, x.Value)), nameof(Keyed<ClassIslandTimeLayout>.Display)); panel.Children.Add(list);
        var name = Input("时间表名称"); panel.Children.Add(name); var details = new TextBlock { Foreground = Muted, Margin = new Thickness(2, 6, 2, 8), TextWrapping = TextWrapping.Wrap };
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is not Keyed<ClassIslandTimeLayout> item) return; name.Text = item.Value.Name; details.Text = $"{item.Value.Layouts.Count} 个时间点 · {item.Value.Layouts.Count(x => x.TimeType == 0)} 节课程"; }; panel.Children.Add(details);
        var buttons = new WrapPanel(); var add = Secondary("新增"); var save = Primary("保存"); var addLesson = Secondary("添加课时");
        add.Click += (_, _) => { profile.TimeLayouts[Guid.NewGuid()] = new() { Name = "新时间表" }; ShowProfile(); };
        save.Click += async (_, _) => { if (list.SelectedItem is not Keyed<ClassIslandTimeLayout> item) return; item.Value.Name = name.Text.Trim(); await SaveProfileAsync(profile); ShowProfile(); };
        addLesson.Click += async (_, _) => { if (list.SelectedItem is not Keyed<ClassIslandTimeLayout> item) return; var start = item.Value.Layouts.LastOrDefault()?.EndTime ?? TimeSpan.FromHours(8); item.Value.Layouts.Add(new() { StartTime = start, EndTime = start + TimeSpan.FromMinutes(40), TimeType = 0 }); await SaveProfileAsync(profile); ShowProfile(); };
        buttons.Children.Add(add); buttons.Children.Add(save); buttons.Children.Add(addLesson); panel.Children.Add(buttons); return WrapEditor(panel);
    }

    private FrameworkElement BuildPlanEditor(ClassIslandProfile profile)
    {
        var panel = EditorPanel("课表"); var list = List(profile.ClassPlans.Select(x => new Keyed<ClassIslandClassPlan>(x.Key, x.Value)), nameof(Keyed<ClassIslandClassPlan>.Display)); panel.Children.Add(list);
        var name = Input("课表名称"); panel.Children.Add(name); var layout = new ComboBox { MinHeight = Metric("TouchCompactTargetHeight", 40), Margin = new Thickness(0, 5, 0, 7), ItemsSource = profile.TimeLayouts.Select(x => new Keyed<ClassIslandTimeLayout>(x.Key, x.Value)).ToArray(), DisplayMemberPath = nameof(Keyed<ClassIslandTimeLayout>.Display) }; panel.Children.Add(layout);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is not Keyed<ClassIslandClassPlan> item) return; name.Text = item.Value.Name; layout.SelectedItem = ((IEnumerable<Keyed<ClassIslandTimeLayout>>)layout.ItemsSource).FirstOrDefault(x => x.Id == item.Value.TimeLayoutId); };
        var buttons = new WrapPanel(); var add = Secondary("新增"); var save = Primary("保存"); var temporary = Secondary("设为临时层");
        add.Click += (_, _) => { profile.ClassPlans[Guid.NewGuid()] = new() { Name = "新课表", TimeLayoutId = profile.TimeLayouts.Keys.FirstOrDefault() }; ShowProfile(); };
        save.Click += async (_, _) => { if (list.SelectedItem is not Keyed<ClassIslandClassPlan> item) return; item.Value.Name = name.Text.Trim(); if (layout.SelectedItem is Keyed<ClassIslandTimeLayout> selected) item.Value.TimeLayoutId = selected.Id; await SaveProfileAsync(profile); ShowProfile(); };
        temporary.Click += async (_, _) => { if (list.SelectedItem is not Keyed<ClassIslandClassPlan> item) return; core.Profiles.CreateTemporaryClassPlan(item.Id); await core.Profiles.SaveAsync(profile); ShowProfile(); };
        buttons.Children.Add(add); buttons.Children.Add(save); buttons.Children.Add(temporary); panel.Children.Add(buttons); return WrapEditor(panel);
    }

    private void ShowComponents()
    {
        Activate("components"); var body = Page("组件布局", "编辑上游 ComponentProfile 的行、组件 GUID、可见性和主行属性。", out var stack);
        var configs = new ComboBox { Width = 220, MinHeight = Metric("TouchCompactTargetHeight", 40), ItemsSource = core.Components.ComponentConfigs, SelectedItem = core.Components.CurrentConfigName };
        configs.SelectionChanged += async (_, _) => { if (configs.SelectedItem is string name) { await core.Components.LoadAsync(name); ShowComponents(); } }; stack.Children.Add(configs);
        var lines = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        for (var lineIndex = 0; lineIndex < core.Components.CurrentComponents.Lines.Count; lineIndex++)
        {
            var index = lineIndex; var line = core.Components.CurrentComponents.Lines[index]; var card = new Border { Background = SurfaceAlt, BorderBrush = BorderBrushValue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) };
            var panel = new StackPanel(); var header = new DockPanel(); var label = new TextBlock { Text = $"第 {index + 1} 行 · {line.Children.Count} 个组件", FontWeight = FontWeights.SemiBold, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center }; header.Children.Add(label);
            var main = new CheckBox { Content = "主要行", IsChecked = line.IsMainLine, HorizontalAlignment = HorizontalAlignment.Right }; main.Checked += async (_, _) => { line.IsMainLine = true; await core.Components.SaveAsync(); }; main.Unchecked += async (_, _) => { line.IsMainLine = false; await core.Components.SaveAsync(); }; DockPanel.SetDock(main, Dock.Right); header.Children.Add(main); panel.Children.Add(header);
            foreach (var component in line.Children)
            {
                var row = new DockPanel { Margin = new Thickness(0, 9, 0, 0) }; var visible = new CheckBox { Content = "显示", IsChecked = component.IsVisible, Width = 62 }; visible.Checked += async (_, _) => { component.IsVisible = true; await core.Components.SaveAsync(); }; visible.Unchecked += async (_, _) => { component.IsVisible = false; await core.Components.SaveAsync(); }; row.Children.Add(visible); var definition = ClassIslandComponentCatalog.Find(component.Id); row.Children.Add(new TextBlock { Text = definition?.Name ?? (string.IsNullOrWhiteSpace(component.NameCache) ? component.Id : component.NameCache), Foreground = definition is null ? Muted : Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }); panel.Children.Add(row);
            }
            var addRow = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) }; var catalog = new ComboBox { Width = 180, MinHeight = Metric("TouchCompactTargetHeight", 40), ItemsSource = ClassIslandComponentCatalog.BuiltIn, DisplayMemberPath = nameof(ClassIslandComponentDefinition.Name), SelectedIndex = 0 }; var add = Secondary("添加组件"); add.Click += async (_, _) => { if (catalog.SelectedItem is not ClassIslandComponentDefinition selected) return; line.Children.Add(new() { Id = selected.Id.ToString(), NameCache = selected.Name }); await core.Components.SaveAsync(); ShowComponents(); }; addRow.Children.Add(catalog); addRow.Children.Add(add); panel.Children.Add(addRow); card.Child = panel; lines.Children.Add(card);
        }
        var addLine = Primary("新增组件行"); addLine.Click += async (_, _) => { core.Components.CurrentComponents.Lines.Add(new()); await core.Components.SaveAsync(); ShowComponents(); }; stack.Children.Add(lines); stack.Children.Add(addLine); Present(body);
    }

    private void ShowNotifications()
    {
        Activate("notifications"); var body = Page("提醒", "使用 ClassIsland v2 Mask → Overlay 生命周期测试并观察提醒队列。", out var stack);
        var title = Input("遮罩标题"); title.Text = "上课提醒"; var message = Input("提醒正文"); message.Text = "下一节课即将开始"; stack.Children.Add(title); stack.Children.Add(message);
        var send = Primary("发送测试提醒"); send.Click += (_, _) => { core.Notifications.Publish(ClassIslandNotificationKind.Information, title.Text, message.Text); ShowNotifications(); }; stack.Children.Add(send);
        stack.Children.Add(Section("当前状态", core.Notifications.Current is null ? "提醒队列空闲" : $"{core.Notifications.Current.State} · 剩余 {core.Notifications.Current.LeftProgress:P0}"));
        foreach (var item in core.Notifications.History.Reverse().Take(20)) stack.Children.Add(Section(item.Title, $"{item.CreatedAt:HH:mm:ss}　{item.Message}")); Present(body);
    }

    private void ShowWeather()
    {
        Activate("weather"); var body = Page("天气", "使用 ClassIsland 的城市编号和天气数据服务；网络不可用时保留最近一次成功获取的数据。", out var stack);
        var city = Input("ClassIsland 城市编号"); city.Text = core.Weather.CityId;
        stack.Children.Add(Field("城市编号", city));
        var actions = new WrapPanel(); var save = Primary("保存并更新天气");
        save.Click += async (_, _) =>
        {
            try { await core.Weather.SetCityAsync(city.Text); ShowWeather(); }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException or System.Net.Http.HttpRequestException)
            { operationStatus!.Text = error.Message; }
        };
        var refresh = Secondary("立即刷新"); refresh.Click += async (_, _) => { await core.Weather.RefreshAsync(); ShowWeather(); };
        actions.Children.Add(save); actions.Children.Add(refresh); stack.Children.Add(actions);
        operationStatus = new TextBlock { Foreground = Accent, Margin = new Thickness(2, 8, 0, 0), TextWrapping = TextWrapping.Wrap }; stack.Children.Add(operationStatus);
        stack.Children.Add(Section("天气数据", core.Weather.Summary));
        Present(body);
    }

    private void ShowAppearance()
    {
        Activate("appearance"); var body = Page("外观", "信息岛主题独立于 ExusiAI 主程序；更改会立即应用到桌面信息岛。", out var stack); var s = core.Appearance.Settings;
        var theme = new ComboBox { MinHeight = Metric("TouchCompactTargetHeight", 40), Width = 220, ItemsSource = Enum.GetValues<ClassIslandIslandTheme>(), SelectedItem = s.IslandTheme };
        stack.Children.Add(Field("信息岛专用主题（独立于主程序）", theme));
        var radius = Numeric(s.CornerRadius); var opacity = Numeric(s.Opacity); var hoverOpacity = Numeric(s.HoverOpacity);
        stack.Children.Add(Field("圆角", radius)); stack.Children.Add(Field("透明度（0.25–1）", opacity));
        stack.Children.Add(Field("鼠标移入后的透明度", hoverOpacity));
        var fade = new CheckBox { Content = "鼠标移入时淡化", IsChecked = s.FadeOnPointerEnter, Margin = new Thickness(2, 0, 0, 12) }; stack.Children.Add(fade);
        var save = Primary("保存并立即应用"); save.Click += async (_, _) =>
        {
            s.IslandTheme = theme.SelectedItem is ClassIslandIslandTheme selected ? selected : s.IslandTheme;
            s.CornerRadius = Parse(radius, s.CornerRadius); s.Opacity = Parse(opacity, s.Opacity); s.HoverOpacity = Parse(hoverOpacity, s.HoverOpacity); s.FadeOnPointerEnter = fade.IsChecked == true;
            await core.Appearance.SaveAsync(); core.Presentation.RefreshAppearance(); ShowAppearance();
        }; stack.Children.Add(save); Present(body);
    }

    private void ShowWindow()
    {
        Activate("window"); var body = Page("窗口", "控制信息岛在教学屏幕上的尺寸、停靠位置和层级。", out var stack); var s = core.Appearance.Settings;
        var dock = new ComboBox { MinHeight = Metric("TouchCompactTargetHeight", 40), Width = 220, ItemsSource = Enum.GetValues<ClassIslandDockPosition>(), SelectedItem = s.DockPosition };
        stack.Children.Add(Field("停靠位置", dock));
        var width = Numeric(s.Width); var height = Numeric(s.Height); var scale = Numeric(s.Scale); var offsetX = Numeric(s.OffsetX); var offsetY = Numeric(s.OffsetY);
        stack.Children.Add(Field("宽度", width)); stack.Children.Add(Field("高度", height)); stack.Children.Add(Field("缩放", scale));
        stack.Children.Add(Field("水平偏移", offsetX)); stack.Children.Add(Field("垂直偏移", offsetY));
        var topmost = new CheckBox { Content = "始终置顶", IsChecked = s.Topmost, Margin = new Thickness(2, 6, 0, 8) };
        var seconds = new CheckBox { Content = "时钟显示秒数", IsChecked = s.ShowSeconds, Margin = new Thickness(2, 0, 0, 12) };
        stack.Children.Add(topmost); stack.Children.Add(seconds);
        var save = Primary("保存并立即应用"); save.Click += async (_, _) =>
        {
            s.DockPosition = dock.SelectedItem is ClassIslandDockPosition position ? position : s.DockPosition;
            s.Width = Parse(width, s.Width); s.Height = Parse(height, s.Height); s.Scale = Parse(scale, s.Scale);
            s.OffsetX = Parse(offsetX, s.OffsetX); s.OffsetY = Parse(offsetY, s.OffsetY);
            s.Topmost = topmost.IsChecked == true; s.ShowSeconds = seconds.IsChecked == true;
            await core.Appearance.SaveAsync(); core.Presentation.RefreshAppearance(); ShowWindow();
        }; stack.Children.Add(save); Present(body);
    }

    private void ShowSync()
    {
        Activate("sync"); var body = Page("同步", "导入 ClassIsland 2.x 自动备份或单个 Profile；原文件不被修改，插件只编辑 ExusiAI 工作副本。", out var stack);
        var actions = new WrapPanel(); var profile = Secondary("导入 Profile JSON"); profile.Click += async (_, _) => await ImportProfileAsync(); var backup = Primary("导入自动备份 ZIP"); backup.Click += async (_, _) => await ImportBackupAsync(); var export = Secondary("导出当前 Profile"); export.Click += async (_, _) => await ExportProfileAsync(); var folder = Secondary("打开数据目录"); folder.Click += (_, _) => { Directory.CreateDirectory(core.DataDirectory); Process.Start(new ProcessStartInfo(core.DataDirectory) { UseShellExecute = true }); };
        actions.Children.Add(profile); actions.Children.Add(backup); actions.Children.Add(export); actions.Children.Add(folder); stack.Children.Add(actions);
        operationStatus = new TextBlock { Foreground = Accent, Margin = new Thickness(2, 14, 0, 0), TextWrapping = TextWrapping.Wrap }; stack.Children.Add(operationStatus);
        stack.Children.Add(Section("兼容策略", "保留 PascalCase、GUID 字典、枚举数值、日期格式及未知字段；保存采用临时文件替换并生成 .bak。Settings.json、Profiles 和 Config 从备份同步到插件工作区。")); Present(body);
    }

    private async Task ImportProfileAsync() { var d = new OpenFileDialog { Filter = "ClassIsland Profile (*.json)|*.json" }; if (d.ShowDialog() != true) return; await using var stream = File.OpenRead(d.FileName); var p = await core.Profiles.ImportAsync(stream, Path.GetFileName(d.FileName)); SetStatus($"已导入档案：{p.Name}"); }
    private async Task ImportBackupAsync() { var d = new OpenFileDialog { Filter = "ClassIsland 自动备份 (*.zip)|*.zip" }; if (d.ShowDialog() != true) return; var r = await core.ImportBackupAsync(d.FileName); SetStatus($"同步完成：{r.TotalFileCount} 个文件，Profiles {r.ProfileFileCount}，Config {r.ConfigFileCount}。"); }
    private async Task ExportProfileAsync() { var p = core.Profiles.Current; if (p is null) { SetStatus("当前没有档案。"); return; } var d = new SaveFileDialog { Filter = "ClassIsland Profile (*.json)|*.json", FileName = $"{p.Id:N}.json" }; if (d.ShowDialog() != true) return; await using var stream = File.Create(d.FileName); await core.Profiles.ExportAsync(p, stream); SetStatus($"已导出：{p.Name}"); }
    private async Task SaveProfileAsync(ClassIslandProfile profile) { await core.Profiles.SaveAsync(profile); SetStatus("档案已保存。"); }

    private ScrollViewer Page(string title, string subtitle, out StackPanel stack) { stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = title, FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = Ink }); stack.Children.Add(new TextBlock { Text = subtitle, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 16) }); return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalFirst, PanningDeceleration = 0.001, PanningRatio = 1 }; }
    private void Present(UIElement view) { contentHost.Children.Clear(); contentHost.Children.Add(view); }
    private void AddNavigation(Panel panel, string key, string title, Action action) { var button = new Button { Content = title, MinHeight = Metric("TouchTargetHeight", 44), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 0, 3), Background = Brushes.Transparent, Foreground = Ink, BorderThickness = new Thickness(0) }; button.Click += (_, _) => action(); navigation[key] = button; panel.Children.Add(button); }
    private void Activate(string key) { foreach (var item in navigation) { item.Value.Background = item.Key == key ? AccentSoft : Brushes.Transparent; item.Value.Foreground = item.Key == key ? Accent : Ink; item.Value.FontWeight = item.Key == key ? FontWeights.SemiBold : FontWeights.Normal; } }
    private static StackPanel EditorPanel(string title) { var p = new StackPanel(); p.Children.Add(new TextBlock { Text = title, Foreground = Accent, FontWeight = FontWeights.SemiBold, FontSize = 16, Margin = new Thickness(0, 0, 0, 8) }); return p; }
    private static Border WrapEditor(UIElement child) => new() { Child = child, Background = SurfaceAlt, BorderBrush = BorderBrushValue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 10, 0) };
    private static ListBox List<T>(IEnumerable<T> items, string display) => new() { ItemsSource = items.ToArray(), DisplayMemberPath = display, MinHeight = 180, MaxHeight = 260, Margin = new Thickness(0, 0, 0, 7), Background = Surface, Foreground = Ink, BorderBrush = BorderBrushValue };
    private static TextBox Input(string tip) => new() { MinHeight = Metric("TouchCompactTargetHeight", 40), Margin = new Thickness(0, 4, 0, 4), Background = Surface, Foreground = Ink, BorderBrush = BorderBrushValue, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tip };
    private static TextBox Numeric(double value) { var box = Input("输入数值"); box.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture); box.Width = 180; box.HorizontalAlignment = HorizontalAlignment.Right; return box; }
    private static double Parse(TextBox box, double fallback) => double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static Border Field(string label, FrameworkElement control) { var row = new Grid { Margin = new Thickness(0, 0, 0, 6) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.Children.Add(new TextBlock { Text = label, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(control, 1); row.Children.Add(control); return new Border { Child = row, Background = SurfaceAlt, CornerRadius = new CornerRadius(7), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 0, 5) }; }
    private static Border Section(string title, string text) { var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = title, Foreground = Accent, FontWeight = FontWeights.SemiBold }); stack.Children.Add(new TextBlock { Text = text, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) }); return new Border { Child = stack, Background = SurfaceAlt, BorderBrush = BorderBrushValue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 14, 0, 0) }; }
    private static Button Primary(string text) => Button(text, Accent, Resource("AccentForegroundBrush", "#FFFFFF"));
    private static Button Secondary(string text) => Button(text, SurfaceAlt, Ink);
    private static Button Danger(string text) => Button(text, Resource("DangerBrush", "#D86464"), Brushes.White);
    private static Button Button(string text, Brush background, Brush foreground) => new() { Content = text, MinHeight = Metric("TouchTargetHeight", 44), MinWidth = 88, Padding = new Thickness(13, 0, 13, 0), Margin = new Thickness(0, 4, 8, 4), Background = background, Foreground = foreground, BorderBrush = BorderBrushValue };
    private static double Metric(string key, double fallback) => Application.Current?.TryFindResource(key) is double value ? value : fallback;
    private static Brush Resource(string key, string fallback) => Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
    private static void ApplyRoundedClip(FrameworkElement element, double radius) { if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return; var r = Math.Min(radius, Math.Min(element.ActualWidth, element.ActualHeight) / 2); element.Clip = new RectangleGeometry(new Rect(0, 0, element.ActualWidth, element.ActualHeight), r, r); }
    private (string title, string detail) CurrentLessonText() { var now = DateTime.Now; var lessons = core.Timetable.GetLessons(now); var current = lessons.FirstOrDefault(x => now.TimeOfDay >= x.Time.StartTime && now.TimeOfDay < x.Time.EndTime); if (current is not null) return (current.Subject.Name, $"{current.Time.StartTime:hh\\:mm}–{current.Time.EndTime:hh\\:mm}  {current.Subject.TeacherName}"); var next = lessons.FirstOrDefault(x => x.Time.StartTime > now.TimeOfDay); return next is null ? ("当前没有课程", "ClassIsland · ExusiAI") : ($"接下来 · {next.Subject.Name}", $"{next.Time.StartTime:hh\\:mm} 开始"); }
    private void SetStatus(string text) { if (operationStatus is not null) operationStatus.Text = text; }
    private sealed record Keyed<T>(Guid Id, T Value) where T : class { public string Display => Value switch { ClassIslandSubject s => s.Name, ClassIslandTimeLayout l => l.Name, ClassIslandClassPlan p => p.Name, _ => Id.ToString() }; }
}
