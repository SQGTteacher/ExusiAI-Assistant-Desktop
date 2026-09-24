using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ExusiAI.Plugin.MishaShowcase;

internal static class MishaUi
{
    public static TextBlock Header(string text) =>
        new() { Text = text, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };

    public static TextBlock Note(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return block;
    }

    public static TextBlock Section(string text) =>
        new() { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 7) };

    public static StackPanel Page(string title, string description)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 24), MaxWidth = 980 };
        panel.Children.Add(Header(title));
        panel.Children.Add(Note(description));
        return panel;
    }

    public static Button Button(string text, bool secondary = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
        if (secondary)
        {
            button.SetResourceReference(Control.BackgroundProperty, "SurfaceAltBrush");
            button.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        }
        return button;
    }

    public static FrameworkElement SettingRow(string label, string description, FrameworkElement editor)
    {
        var wrapper = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };

        var info = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
        info.Children.Add(new TextBlock { Text = label, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(description))
        {
            var note = Note(description);
            note.Margin = new Thickness(0, 3, 0, 0);
            info.Children.Add(note);
        }
        wrapper.Children.Add(info);

        editor.VerticalAlignment = VerticalAlignment.Center;
        editor.HorizontalAlignment = double.IsNaN(editor.Width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        editor.Margin = new Thickness(0, 4, 0, 10);
        wrapper.Children.Add(editor);
        wrapper.Children.Add(new Separator { Opacity = 0.45 });
        return wrapper;
    }

    public static ScrollViewer Scroll(StackPanel panel) =>
        new()
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
}

internal sealed class MishaWorkspacePage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly StackPanel details = new();
    private readonly TextBlock status = MishaUi.Note("未连接 ClassIsland 数据目录。");

    public MishaWorkspacePage(MishaPlatformStore store)
    {
        this.store = store;

        var root = MishaUi.Page("ClassIsland 工作区", "导入已有 ClassIsland Settings.json。所有 ClassIsland JSON 会按原目录结构复制到 ExusiAI 自有工作区，之后只修改该副本；字段与格式仍保持 ClassIsland 原生兼容。");
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var openSettings = MishaUi.Button("导入 Settings.json");
        openSettings.Click += OpenSettings_OnClick;
        var openProfile = MishaUi.Button("仅打开 Profile JSON", true);
        openProfile.Click += OpenProfile_OnClick;
        actions.Children.Add(openSettings);
        actions.Children.Add(openProfile);
        root.Children.Add(actions);
        status.Margin = new Thickness(0, 10, 0, 12);
        root.Children.Add(status);
        root.Children.Add(new Separator());
        root.Children.Add(details);

        store.Changed += (_, _) => Dispatcher.Invoke(RefreshDetails);
        RefreshDetails();
        Content = MishaUi.Scroll(root);
    }

    private void RefreshDetails()
    {
        details.Children.Clear();

        if (store.Workspace is not null)
        {
            details.Children.Add(MishaUi.Section("ExusiAI ClassIsland 工作区"));
            if (!string.IsNullOrWhiteSpace(store.SourceRootDirectory))
                details.Children.Add(MishaUi.SettingRow("导入来源", "仅用于迁移/后续同步，不直接写回。", ReadOnlyText(store.SourceRootDirectory!, 420)));
            details.Children.Add(MishaUi.SettingRow("本地工作目录", "ExusiAI 后续只修改这里的 ClassIsland 原生 JSON。", ReadOnlyText(store.Workspace.RootDirectory, 420)));
            details.Children.Add(MishaUi.SettingRow("Settings.json", "", ReadOnlyText(store.Workspace.SettingsPath, 420)));
            details.Children.Add(MishaUi.SettingRow("当前档案", "", ReadOnlyText(store.Workspace.SelectedProfile, 300)));
            details.Children.Add(MishaUi.SettingRow("组件配置", "", ReadOnlyText(store.Workspace.CurrentComponentConfig, 240)));
            details.Children.Add(MishaUi.SettingRow("自动化配置", "", ReadOnlyText(store.Workspace.CurrentAutomationConfig, 240)));
        }

        if (store.Profile is not null)
        {
            details.Children.Add(MishaUi.Section("当前 Profile"));
            details.Children.Add(MishaUi.SettingRow("档案名称", "", ReadOnlyText(store.Profile.Name, 260)));
            details.Children.Add(MishaUi.SettingRow("档案路径", "", ReadOnlyText(store.Profile.FilePath, 420)));
            details.Children.Add(MishaUi.SettingRow("科目", "", ReadOnlyText(store.Profile.Subjects.Count.ToString(), 100)));
            details.Children.Add(MishaUi.SettingRow("时间表", "", ReadOnlyText(store.Profile.TimeLayouts.Count.ToString(), 100)));
            details.Children.Add(MishaUi.SettingRow("课表", "", ReadOnlyText(store.Profile.ClassPlans.Count.ToString(), 100)));
        }
    }

    private async void OpenSettings_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ClassIsland Settings.json|Settings.json|JSON 文件 (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await store.AttachWorkspaceAsync(dialog.FileName);
            status.Text = store.Profile is null
                ? "已导入 ClassIsland JSON 到 ExusiAI 工作区，但未找到 SelectedProfile 对应档案。"
                : "已导入 ClassIsland 工作区；后续修改仅作用于 ExusiAI 副本。";
        }
        catch (Exception exception)
        {
            status.Text = $"连接失败：{exception.Message}";
        }
    }

    private async void OpenProfile_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ClassIsland Profile (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await store.OpenProfileAsync(dialog.FileName);
            status.Text = "已将 ClassIsland Profile 导入 ExusiAI 本地副本。";
        }
        catch (Exception exception)
        {
            status.Text = $"打开失败：{exception.Message}";
        }
    }

    private static TextBox ReadOnlyText(string value, double width) =>
        new() { Text = value, IsReadOnly = true, Width = width };
}

internal sealed class MishaSubjectsPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly DataGrid grid = new();
    private readonly TextBlock status = MishaUi.Note("");

    public MishaSubjectsPage(MishaPlatformStore store)
    {
        this.store = store;
        var root = MishaUi.Page("科目", "直接编辑当前 ClassIsland Profile 的 Subjects 字典，保留每个科目的 GUID 与未知字段。");

        ConfigureGrid();
        root.Children.Add(grid);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var add = MishaUi.Button("新增科目");
        add.Click += (_, _) =>
        {
            if (store.Profile is null) return;
            store.Profile.AddSubject();
            Reload();
        };
        var remove = MishaUi.Button("删除选中", true);
        remove.Click += (_, _) =>
        {
            if (store.Profile is null || grid.SelectedItem is not ClassIslandSubjectRow row) return;
            try
            {
                store.Profile.RemoveSubject(row.Id);
                Reload();
                status.Text = "";
            }
            catch (Exception exception)
            {
                status.Text = exception.Message;
            }
        };
        var save = MishaUi.Button("保存 Profile");
        save.Click += async (_, _) => await SaveAsync();

        actions.Children.Add(add);
        actions.Children.Add(remove);
        actions.Children.Add(save);
        root.Children.Add(actions);
        root.Children.Add(status);

        Loaded += (_, _) => Reload();
        store.Changed += (_, _) => Dispatcher.Invoke(Reload);
        Content = MishaUi.Scroll(root);
    }

    private void ConfigureGrid()
    {
        grid.AutoGenerateColumns = false;
        grid.CanUserAddRows = false;
        grid.MinHeight = 340;
        grid.Columns.Add(new DataGridTextColumn { Header = "GUID", Binding = new Binding(nameof(ClassIslandSubjectRow.Id)), IsReadOnly = true, Width = 250 });
        grid.Columns.Add(new DataGridTextColumn { Header = "科目", Binding = new Binding(nameof(ClassIslandSubjectRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "简称", Binding = new Binding(nameof(ClassIslandSubjectRow.Initial)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "教师", Binding = new Binding(nameof(ClassIslandSubjectRow.TeacherName)), Width = 130 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "户外", Binding = new Binding(nameof(ClassIslandSubjectRow.IsOutDoor)), Width = 65 });
    }

    private void Reload() => grid.ItemsSource = store.Profile?.Subjects ?? [];

    private async Task SaveAsync()
    {
        if (store.Profile is null) { status.Text = "尚未打开 Profile。"; return; }
        grid.CommitEdit(DataGridEditingUnit.Row, true);
        await store.SaveProfileAsync();
        status.Text = $"已保存 {Path.GetFileName(store.Profile.FilePath)}";
    }
}

internal sealed class MishaTimeLayoutsPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly ComboBox layouts = new();
    private readonly DataGrid points = new();
    private readonly TextBlock status = MishaUi.Note("");

    public MishaTimeLayoutsPage(MishaPlatformStore store)
    {
        this.store = store;
        var root = MishaUi.Page("时间表", "直接编辑 TimeLayouts 与 Layouts。上课、课间、分割线和行动时间点保持 ClassIsland 原生 TimeType。");

        layouts.DisplayMemberPath = nameof(ClassIslandTimeLayoutRow.Name);
        layouts.MinWidth = 260;
        layouts.SelectionChanged += (_, _) => ReloadPoints();
        root.Children.Add(MishaUi.SettingRow("当前时间表", "", layouts));

        ConfigurePoints();
        root.Children.Add(points);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var addLayout = MishaUi.Button("新增时间表");
        addLayout.Click += (_, _) =>
        {
            if (store.Profile is null) return;
            var added = store.Profile.AddTimeLayout();
            ReloadLayouts(added.Id);
        };
        var removeLayout = MishaUi.Button("删除时间表", true);
        removeLayout.Click += (_, _) =>
        {
            if (store.Profile is null || layouts.SelectedItem is not ClassIslandTimeLayoutRow layout) return;
            try { store.Profile.RemoveTimeLayout(layout.Id); ReloadLayouts(); status.Text = ""; }
            catch (Exception exception) { status.Text = exception.Message; }
        };
        var addClass = MishaUi.Button("新增上课时间点");
        addClass.Click += (_, _) => AddPoint(0);
        var addBreak = MishaUi.Button("新增课间");
        addBreak.Click += (_, _) => AddPoint(1);
        var removePoint = MishaUi.Button("删除时间点", true);
        removePoint.Click += (_, _) =>
        {
            if (store.Profile is null ||
                layouts.SelectedItem is not ClassIslandTimeLayoutRow layout ||
                points.SelectedItem is not ClassIslandTimeLayoutItemRow point) return;
            store.Profile.RemoveTimeLayoutItem(layout.Id, point.Node);
            ReloadPoints();
        };
        var save = MishaUi.Button("保存 Profile");
        save.Click += async (_, _) => await SaveAsync();

        foreach (var button in new[] { addLayout, removeLayout, addClass, addBreak, removePoint, save })
            actions.Children.Add(button);
        root.Children.Add(actions);
        root.Children.Add(status);

        Loaded += (_, _) => ReloadLayouts();
        store.Changed += (_, _) => Dispatcher.Invoke(() => ReloadLayouts());
        Content = MishaUi.Scroll(root);
    }

    private void ConfigurePoints()
    {
        points.AutoGenerateColumns = false;
        points.CanUserAddRows = false;
        points.MinHeight = 330;
        points.Columns.Add(new DataGridTextColumn { Header = "开始", Binding = new Binding(nameof(ClassIslandTimeLayoutItemRow.StartTime)), Width = 105 });
        points.Columns.Add(new DataGridTextColumn { Header = "结束", Binding = new Binding(nameof(ClassIslandTimeLayoutItemRow.EndTime)), Width = 105 });
        points.Columns.Add(new DataGridTextColumn { Header = "TimeType", Binding = new Binding(nameof(ClassIslandTimeLayoutItemRow.TimeType)), Width = 90 });
        points.Columns.Add(new DataGridTextColumn { Header = "课间名称", Binding = new Binding(nameof(ClassIslandTimeLayoutItemRow.BreakName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        points.Columns.Add(new DataGridTextColumn { Header = "默认科目", Binding = new Binding(nameof(ClassIslandTimeLayoutItemRow.DefaultSubject)), Width = 150 });
        points.Columns.Add(new DataGridCheckBoxColumn { Header = "默认隐藏", Binding = new Binding(nameof(ClassIslandTimeLayoutItemRow.IsHideDefault)), Width = 85 });
    }

    private void ReloadLayouts(string? selectId = null)
    {
        var values = store.Profile?.TimeLayouts ?? [];
        layouts.ItemsSource = values;
        if (values.Count == 0) { points.ItemsSource = null; return; }

        layouts.SelectedItem = selectId is null
            ? values[0]
            : values.FirstOrDefault(x => x.Id == selectId) ?? values[0];
    }

    private void ReloadPoints()
    {
        points.ItemsSource = layouts.SelectedItem is ClassIslandTimeLayoutRow row ? row.Items : null;
    }

    private void AddPoint(int type)
    {
        if (store.Profile is null || layouts.SelectedItem is not ClassIslandTimeLayoutRow layout) return;
        store.Profile.AddTimeLayoutItem(layout.Id, type);
        ReloadPoints();
    }

    private async Task SaveAsync()
    {
        if (store.Profile is null) { status.Text = "尚未打开 Profile。"; return; }
        points.CommitEdit(DataGridEditingUnit.Row, true);
        await store.SaveProfileAsync();
        status.Text = "已保存真实 ClassIsland Profile。";
    }
}

internal sealed class MishaClassPlansPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly ComboBox plans = new();
    private readonly DataGrid lessons = new();
    private readonly StackPanel ruleArea = new();
    private readonly TextBlock status = MishaUi.Note("");

    public MishaClassPlansPage(MishaPlatformStore store)
    {
        this.store = store;
        var root = MishaUi.Page("课表", "直接编辑 ClassPlans、Classes 与 TimeRule；科目引用保持原 GUID。");

        plans.DisplayMemberPath = nameof(ClassIslandClassPlanRow.Name);
        plans.MinWidth = 280;
        plans.SelectionChanged += (_, _) => ReloadSelected();
        root.Children.Add(MishaUi.SettingRow("当前课表", "", plans));

        root.Children.Add(ruleArea);
        ConfigureLessons();
        root.Children.Add(lessons);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var add = MishaUi.Button("新增课表");
        add.Click += (_, _) =>
        {
            if (store.Profile is null || store.Profile.TimeLayouts.Count == 0)
            {
                status.Text = "请先创建时间表。";
                return;
            }
            var added = store.Profile.AddClassPlan(store.Profile.TimeLayouts[0].Id);
            ReloadPlans(added.Id);
        };
        var remove = MishaUi.Button("删除课表", true);
        remove.Click += (_, _) =>
        {
            if (store.Profile is null || plans.SelectedItem is not ClassIslandClassPlanRow plan) return;
            store.Profile.RemoveClassPlan(plan.Id);
            ReloadPlans();
        };
        var save = MishaUi.Button("保存 Profile");
        save.Click += async (_, _) => await SaveAsync();
        actions.Children.Add(add);
        actions.Children.Add(remove);
        actions.Children.Add(save);
        root.Children.Add(actions);
        root.Children.Add(status);

        Loaded += (_, _) => ReloadPlans();
        store.Changed += (_, _) => Dispatcher.Invoke(() => ReloadPlans());
        Content = MishaUi.Scroll(root);
    }

    private void ConfigureLessons()
    {
        lessons.AutoGenerateColumns = false;
        lessons.CanUserAddRows = false;
        lessons.MinHeight = 330;
        lessons.Columns.Add(new DataGridTextColumn { Header = "节次", Binding = new Binding(nameof(ClassIslandLessonRow.Index)), IsReadOnly = true, Width = 65 });
        lessons.Columns.Add(new DataGridTextColumn { Header = "科目", Binding = new Binding(nameof(ClassIslandLessonRow.SubjectName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        lessons.Columns.Add(new DataGridTextColumn { Header = "教师", Binding = new Binding(nameof(ClassIslandLessonRow.TeacherName)), IsReadOnly = true, Width = 120 });
        lessons.Columns.Add(new DataGridTextColumn { Header = "开始", Binding = new Binding(nameof(ClassIslandLessonRow.StartTime)), IsReadOnly = true, Width = 95 });
        lessons.Columns.Add(new DataGridTextColumn { Header = "结束", Binding = new Binding(nameof(ClassIslandLessonRow.EndTime)), IsReadOnly = true, Width = 95 });
        lessons.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = new Binding(nameof(ClassIslandLessonRow.Enabled)), Width = 70 });
    }

    private void ReloadPlans(string? selectedId = null)
    {
        var values = store.Profile?.ClassPlans ?? [];
        plans.ItemsSource = values;
        if (values.Count == 0)
        {
            lessons.ItemsSource = null;
            ruleArea.Children.Clear();
            return;
        }
        plans.SelectedItem = selectedId is null
            ? values[0]
            : values.FirstOrDefault(x => x.Id == selectedId) ?? values[0];
    }

    private void ReloadSelected()
    {
        if (plans.SelectedItem is not ClassIslandClassPlanRow plan)
        {
            lessons.ItemsSource = null;
            ruleArea.Children.Clear();
            return;
        }

        lessons.ItemsSource = plan.Lessons;
        ruleArea.Children.Clear();
        ruleArea.Children.Add(MishaUi.Section("启用规则"));

        var enabled = new CheckBox { IsChecked = plan.IsEnabled };
        enabled.Checked += (_, _) => plan.IsEnabled = true;
        enabled.Unchecked += (_, _) => plan.IsEnabled = false;
        ruleArea.Children.Add(MishaUi.SettingRow("默认启用", "", enabled));

        var type = new ComboBox { ItemsSource = new[] { "Weekly", "Date", "Loop" }, SelectedIndex = Math.Clamp(plan.RuleType, 0, 2), Width = 130 };
        type.SelectionChanged += (_, _) => plan.RuleType = type.SelectedIndex;
        ruleArea.Children.Add(MishaUi.SettingRow("TimeRule 类型", "", type));

        var weekDay = new ComboBox { ItemsSource = Enum.GetNames<DayOfWeek>(), SelectedIndex = Math.Clamp(plan.WeekDay, 0, 6), Width = 140 };
        weekDay.SelectionChanged += (_, _) => plan.WeekDay = weekDay.SelectedIndex;
        ruleArea.Children.Add(MishaUi.SettingRow("星期", "Sunday=0，与 ClassIsland TimeRule 一致。", weekDay));

        var week = new TextBox { Text = plan.WeekCountDiv.ToString(), Width = 100 };
        week.TextChanged += (_, _) => { if (int.TryParse(week.Text, out var value)) plan.WeekCountDiv = value; };
        ruleArea.Children.Add(MishaUi.SettingRow("轮换周", "0 表示不限制；n 表示第 n 周。", week));

        var total = new TextBox { Text = plan.WeekCountDivTotal.ToString(), Width = 100 };
        total.TextChanged += (_, _) => { if (int.TryParse(total.Text, out var value)) plan.WeekCountDivTotal = Math.Max(1, value); };
        ruleArea.Children.Add(MishaUi.SettingRow("轮换总周数", "", total));
    }

    private async Task SaveAsync()
    {
        if (store.Profile is null) { status.Text = "尚未打开 Profile。"; return; }
        lessons.CommitEdit(DataGridEditingUnit.Row, true);
        await store.SaveProfileAsync();
        status.Text = "已保存真实 ClassIsland Profile。";
    }
}

internal sealed class MishaNativeJsonConfigPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly Func<ClassIslandWorkspace, string?> pathResolver;
    private readonly TextBox editor = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 430 };
    private readonly TextBlock status = MishaUi.Note("");

    public MishaNativeJsonConfigPage(MishaPlatformStore store, string title, string description, Func<ClassIslandWorkspace, string?> pathResolver)
    {
        this.store = store;
        this.pathResolver = pathResolver;

        var root = MishaUi.Page(title, description);
        editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        root.Children.Add(editor);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var reload = MishaUi.Button("重新加载", true);
        reload.Click += async (_, _) => await ReloadAsync();
        var save = MishaUi.Button("验证并保存");
        save.Click += async (_, _) => await SaveAsync();
        actions.Children.Add(reload);
        actions.Children.Add(save);
        root.Children.Add(actions);
        root.Children.Add(status);

        Loaded += async (_, _) => await ReloadAsync();
        store.Changed += (_, _) => Dispatcher.InvokeAsync(() => _ = ReloadAsync());
        Content = MishaUi.Scroll(root);
    }

    private async Task ReloadAsync()
    {
        if (store.Workspace is null)
        {
            editor.Text = "";
            status.Text = "请先连接 ClassIsland Settings.json。";
            return;
        }

        var path = pathResolver(store.Workspace);
        if (path is null || !File.Exists(path))
        {
            editor.Text = "";
            status.Text = "当前 Settings.json 未指向已存在的配置文件。不会自动创建示例配置。";
            return;
        }

        editor.Text = await File.ReadAllTextAsync(path);
        status.Text = path;
    }

    private async Task SaveAsync()
    {
        if (store.Workspace is null) { status.Text = "请先连接工作区。"; return; }
        var path = pathResolver(store.Workspace);
        if (path is null || !File.Exists(path))
        {
            status.Text = "目标配置不存在；为避免生成模拟配置，本移植不会自动创建。";
            return;
        }

        try
        {
            var parsed = JsonNode.Parse(editor.Text)
                ?? throw new InvalidDataException("JSON 根节点为空。");
            await ClassIslandWorkspace.WriteJsonAtomicAsync(path, parsed);
            status.Text = $"已保存：{path}";
        }
        catch (Exception exception)
        {
            status.Text = $"保存失败：{exception.Message}";
        }
    }
}

internal sealed class MishaDataPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly TextBlock status = MishaUi.Note("");

    public MishaDataPage(MishaPlatformStore store)
    {
        this.store = store;
        var root = MishaUi.Page("档案文件", "导入现有 Profile 后只编辑 ExusiAI 本地副本；“导出副本”用于显式迁移，不会把后续编辑目标切回 ClassIsland 原目录。");

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var open = MishaUi.Button("打开 Profile");
        open.Click += Open_OnClick;
        var save = MishaUi.Button("保存", true);
        save.Click += async (_, _) => await SaveAsync();
        var saveAs = MishaUi.Button("导出副本", true);
        saveAs.Click += SaveAs_OnClick;
        actions.Children.Add(open);
        actions.Children.Add(save);
        actions.Children.Add(saveAs);
        root.Children.Add(actions);
        root.Children.Add(status);

        Content = MishaUi.Scroll(root);
    }

    private async void Open_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ClassIsland Profile (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        try { await store.OpenProfileAsync(dialog.FileName); status.Text = "已打开真实 Profile。"; }
        catch (Exception exception) { status.Text = $"打开失败：{exception.Message}"; }
    }

    private async Task SaveAsync()
    {
        try { await store.SaveProfileAsync(); status.Text = "已保存。"; }
        catch (Exception exception) { status.Text = $"保存失败：{exception.Message}"; }
    }

    private async void SaveAs_OnClick(object sender, RoutedEventArgs e)
    {
        if (store.Profile is null) { status.Text = "尚未打开 Profile。"; return; }
        var dialog = new SaveFileDialog { Filter = "ClassIsland Profile (*.json)|*.json", FileName = Path.GetFileName(store.Profile.FilePath) };
        if (dialog.ShowDialog() != true) return;
        try { await store.SaveProfileAsAsync(dialog.FileName); status.Text = "已导出 ClassIsland 原生 Profile 副本；当前编辑仍留在 ExusiAI 工作区。"; }
        catch (Exception exception) { status.Text = $"另存失败：{exception.Message}"; }
    }
}
