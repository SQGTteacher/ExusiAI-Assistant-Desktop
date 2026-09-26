using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class MishaClassPlanGroupsPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly DataGrid groups = MishaUi.DataGrid();
    private readonly DataGrid plans = MishaUi.DataGrid();
    private readonly ComboBox assignedGroup = new()
    {
        MinWidth = 240,
        DisplayMemberPath = nameof(ClassIslandClassPlanGroupRow.Name)
    };
    private readonly TextBlock status = MishaUi.Note("");

    public MishaClassPlanGroupsPage(MishaPlatformStore store)
    {
        this.store = store;
        var root = MishaUi.Page("课表群", "直接编辑 ClassPlanGroups 与 ClassPlan.AssociatedGroup。默认课表群和全局课表群受到保护。");

        groups.AutoGenerateColumns = false;
        groups.CanUserAddRows = false;
        groups.MinHeight = 180;
        groups.Columns.Add(new DataGridTextColumn { Header = "GUID", Binding = new Binding(nameof(ClassIslandClassPlanGroupRow.Id)), IsReadOnly = true, Width = 250 });
        groups.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new Binding(nameof(ClassIslandClassPlanGroupRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        groups.Columns.Add(new DataGridCheckBoxColumn { Header = "全局", Binding = new Binding(nameof(ClassIslandClassPlanGroupRow.IsGlobal)), Width = 70 });
        root.Children.Add(groups);

        var groupActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var add = MishaUi.Button("新增课表群");
        add.Click += (_, _) =>
        {
            if (store.Profile is null) return;
            store.Profile.AddClassPlanGroup();
            Reload();
        };
        var remove = MishaUi.Button("删除选中", true);
        remove.Click += (_, _) =>
        {
            if (store.Profile is null || groups.SelectedItem is not ClassIslandClassPlanGroupRow row) return;
            try
            {
                store.Profile.RemoveClassPlanGroup(row.Id);
                Reload();
                status.Text = "";
            }
            catch (Exception exception)
            {
                status.Text = exception.Message;
            }
        };
        groupActions.Children.Add(add);
        groupActions.Children.Add(remove);
        root.Children.Add(groupActions);

        root.Children.Add(MishaUi.Section("课表归组"));
        plans.AutoGenerateColumns = false;
        plans.CanUserAddRows = false;
        plans.MinHeight = 220;
        plans.Columns.Add(new DataGridTextColumn { Header = "课表", Binding = new Binding(nameof(ClassIslandClassPlanRow.Name)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        plans.SelectionChanged += (_, _) => RefreshAssignedGroup();
        root.Children.Add(plans);

        assignedGroup.SelectionChanged += (_, _) =>
        {
            if (plans.SelectedItem is ClassIslandClassPlanRow plan &&
                assignedGroup.SelectedItem is ClassIslandClassPlanGroupRow group)
                plan.AssociatedGroup = group.Id;
        };
        root.Children.Add(MishaUi.SettingRow(
            "选中课表所属课表群",
            "以课表群名称选择，保存时仍写入 ClassIsland 原生 GUID。",
            assignedGroup));

        var save = MishaUi.Button("保存 Profile");
        save.Margin = new Thickness(0, 12, 0, 0);
        save.Click += async (_, _) =>
        {
            if (store.Profile is null) { status.Text = "尚未打开 Profile。"; return; }
            groups.CommitEdit(DataGridEditingUnit.Row, true);
            plans.CommitEdit(DataGridEditingUnit.Row, true);
            await store.SaveProfileAsync();
            status.Text = "已保存课表群与归组关系。";
        };
        root.Children.Add(save);
        root.Children.Add(status);

        Loaded += (_, _) => Reload();
        store.Changed += (_, _) => Dispatcher.Invoke(Reload);
        Content = MishaUi.Scroll(root);
    }

    private void Reload()
    {
        var profile = store.Profile;
        var groupRows = profile?.ClassPlanGroups ?? [];
        groups.ItemsSource = groupRows;
        plans.ItemsSource = profile?.ClassPlans ?? [];
        assignedGroup.ItemsSource = groupRows;
        RefreshAssignedGroup();
    }

    private void RefreshAssignedGroup()
    {
        if (plans.SelectedItem is not ClassIslandClassPlanRow plan ||
            assignedGroup.ItemsSource is not IEnumerable<ClassIslandClassPlanGroupRow> groupRows)
        {
            assignedGroup.SelectedItem = null;
            return;
        }

        assignedGroup.SelectedItem = groupRows.FirstOrDefault(group => SameId(group.Id, plan.AssociatedGroup));
    }

    private static bool SameId(string left, string right) =>
        Guid.TryParse(left, out var leftGuid) && Guid.TryParse(right, out var rightGuid)
            ? leftGuid == rightGuid
            : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

internal sealed class MishaOrderedSchedulesPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly DataGrid grid = MishaUi.DataGrid();
    private readonly DatePicker date = new() { SelectedDate = DateTime.Today, Width = 160 };
    private readonly ComboBox plans = new() { MinWidth = 220, DisplayMemberPath = nameof(ClassIslandClassPlanRow.Name) };
    private readonly TextBlock status = MishaUi.Note("");

    public MishaOrderedSchedulesPage(MishaPlatformStore store)
    {
        this.store = store;
        var root = MishaUi.Page("预定课表", "直接编辑 Profile.OrderedSchedules。日期键与 ClassPlanId 均保存为 ClassIsland 原生结构。");

        var editor = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        editor.Children.Add(date);
        plans.Margin = new Thickness(8, 0, 8, 0);
        editor.Children.Add(plans);
        var add = MishaUi.Button("预定");
        add.Click += (_, _) => Add();
        editor.Children.Add(add);
        root.Children.Add(editor);

        grid.AutoGenerateColumns = false;
        grid.CanUserAddRows = false;
        grid.MinHeight = 320;
        grid.Columns.Add(new DataGridTextColumn { Header = "日期", Binding = new Binding(nameof(ClassIslandOrderedScheduleRow.Date)) { StringFormat = "yyyy-MM-dd" }, IsReadOnly = true, Width = 130 });
        grid.Columns.Add(new DataGridTextColumn { Header = "课表", Binding = new Binding(nameof(ClassIslandOrderedScheduleRow.ClassPlanName)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        root.Children.Add(grid);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var remove = MishaUi.Button("删除选中", true);
        remove.Click += (_, _) =>
        {
            if (store.Profile is null || grid.SelectedItem is not ClassIslandOrderedScheduleRow row) return;
            store.Profile.RemoveOrderedSchedule(row.DateKey);
            Reload();
        };
        var save = MishaUi.Button("保存 Profile");
        save.Click += async (_, _) =>
        {
            if (store.Profile is null) { status.Text = "尚未打开 Profile。"; return; }
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            await store.SaveProfileAsync();
            status.Text = "已保存预定课表。";
        };
        actions.Children.Add(remove);
        actions.Children.Add(save);
        root.Children.Add(actions);
        root.Children.Add(status);

        Loaded += (_, _) => Reload();
        store.Changed += (_, _) => Dispatcher.Invoke(Reload);
        Content = MishaUi.Scroll(root);
    }

    private void Add()
    {
        if (store.Profile is null || date.SelectedDate is not DateTime selected ||
            plans.SelectedItem is not ClassIslandClassPlanRow plan)
            return;

        store.Profile.AddOrderedSchedule(selected.Date, plan.Id);
        Reload();
    }

    private void Reload()
    {
        grid.ItemsSource = store.Profile?.OrderedSchedules ?? [];
        var classPlans = store.Profile?.ClassPlans ?? [];
        plans.ItemsSource = classPlans;
        if (classPlans.Count > 0 && plans.SelectedItem is null) plans.SelectedIndex = 0;
    }
}

internal sealed class MishaScheduleModePage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly DataGrid grid = MishaUi.DataGrid();
    private readonly TextBlock status = MishaUi.Note("");

    public MishaScheduleModePage(MishaPlatformStore store)
    {
        this.store = store;
        var root = MishaUi.Page("日程模式", "直接编辑 Profile.ScheduleItems 和 ScheduleType。ScheduleType=1 时 ClassIsland 使用独立日程项目。");

        var mode = new ComboBox { ItemsSource = new[] { "经典课表", "独立日程" }, Width = 160 };
        mode.SelectionChanged += (_, _) =>
        {
            if (store.Profile is not null) store.Profile.ScheduleType = mode.SelectedIndex;
        };
        root.Children.Add(MishaUi.SettingRow("日程模式", "与 ClassIsland ScheduleType 一致；经典课表使用 ClassPlans，独立日程使用 ScheduleItems。", mode));

        grid.AutoGenerateColumns = false;
        grid.CanUserAddRows = false;
        grid.MinHeight = 340;
        grid.Columns.Add(new DataGridTextColumn { Header = "GUID", Binding = new Binding(nameof(ClassIslandScheduleItemRow.Id)), IsReadOnly = true, Width = 250 });
        grid.Columns.Add(new DataGridTextColumn { Header = "科目", Binding = new Binding(nameof(ClassIslandScheduleItemRow.SubjectName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "开始", Binding = new Binding(nameof(ClassIslandScheduleItemRow.StartTime)), Width = 100 });
        grid.Columns.Add(new DataGridTextColumn { Header = "结束", Binding = new Binding(nameof(ClassIslandScheduleItemRow.EndTime)), Width = 100 });
        grid.Columns.Add(new DataGridTextColumn { Header = "规则", Binding = new Binding(nameof(ClassIslandScheduleItemRow.RuleType)), Width = 70 });
        grid.Columns.Add(new DataGridTextColumn { Header = "星期", Binding = new Binding(nameof(ClassIslandScheduleItemRow.WeekDay)), Width = 70 });
        grid.Columns.Add(new DataGridTextColumn { Header = "轮换周", Binding = new Binding(nameof(ClassIslandScheduleItemRow.WeekCountDiv)), Width = 80 });
        root.Children.Add(grid);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var add = MishaUi.Button("新增日程");
        add.Click += (_, _) =>
        {
            if (store.Profile is null) return;
            store.Profile.AddScheduleItem();
            Reload(mode);
        };
        var remove = MishaUi.Button("删除选中", true);
        remove.Click += (_, _) =>
        {
            if (store.Profile is null || grid.SelectedItem is not ClassIslandScheduleItemRow row) return;
            store.Profile.RemoveScheduleItem(row.Id);
            Reload(mode);
        };
        var save = MishaUi.Button("保存 Profile");
        save.Click += async (_, _) =>
        {
            if (store.Profile is null) { status.Text = "尚未打开 Profile。"; return; }
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            await store.SaveProfileAsync();
            status.Text = "已保存日程模式数据。";
        };
        actions.Children.Add(add);
        actions.Children.Add(remove);
        actions.Children.Add(save);
        root.Children.Add(actions);
        root.Children.Add(status);

        Loaded += (_, _) => Reload(mode);
        store.Changed += (_, _) => Dispatcher.Invoke(() => Reload(mode));
        Content = MishaUi.Scroll(root);
    }

    private void Reload(ComboBox mode)
    {
        mode.SelectedIndex = Math.Clamp(store.Profile?.ScheduleType ?? 0, 0, 1);
        grid.ItemsSource = store.Profile?.ScheduleItems ?? [];
    }
}

internal sealed class MishaTemporarySchedulePage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly StackPanel body = new();
    private readonly TextBlock status = MishaUi.Note("");

    public MishaTemporarySchedulePage(MishaPlatformStore store)
    {
        this.store = store;
        var root = MishaUi.Page("临时课表", "编辑 ClassIsland Profile 的临时课表、叠层课表和临时课表群字段。");
        root.Children.Add(body);
        root.Children.Add(status);
        Loaded += (_, _) => Reload();
        store.Changed += (_, _) => Dispatcher.Invoke(Reload);
        Content = MishaUi.Scroll(root);
    }

    private void Reload()
    {
        body.Children.Clear();
        var profile = store.Profile;
        if (profile is null)
        {
            body.Children.Add(MishaUi.Note("尚未打开 Profile。"));
            return;
        }

        var plans = profile.ClassPlans;
        var groups = profile.ClassPlanGroups;

        var overlayEnabled = new CheckBox { IsChecked = profile.IsOverlayClassPlanEnabled };
        overlayEnabled.Checked += (_, _) => profile.IsOverlayClassPlanEnabled = true;
        overlayEnabled.Unchecked += (_, _) => profile.IsOverlayClassPlanEnabled = false;
        body.Children.Add(MishaUi.SettingRow("启用叠层课表", "", overlayEnabled));

        var overlayPlan = new ComboBox { ItemsSource = plans, DisplayMemberPath = nameof(ClassIslandClassPlanRow.Name), MinWidth = 220 };
        overlayPlan.SelectedItem = FindPlan(plans, profile.OverlayClassPlanId);
        overlayPlan.SelectionChanged += (_, _) => profile.OverlayClassPlanId = (overlayPlan.SelectedItem as ClassIslandClassPlanRow)?.Id ?? "";
        body.Children.Add(MishaUi.SettingRow("叠层课表", "", overlayPlan));

        var tempPlan = new ComboBox { ItemsSource = plans, DisplayMemberPath = nameof(ClassIslandClassPlanRow.Name), MinWidth = 220 };
        tempPlan.SelectedItem = FindPlan(plans, profile.TempClassPlanId);
        tempPlan.SelectionChanged += (_, _) => profile.TempClassPlanId = (tempPlan.SelectedItem as ClassIslandClassPlanRow)?.Id ?? "";
        body.Children.Add(MishaUi.SettingRow("临时课表", "", tempPlan));

        var tempGroupEnabled = new CheckBox { IsChecked = profile.IsTempClassPlanGroupEnabled };
        tempGroupEnabled.Checked += (_, _) => profile.IsTempClassPlanGroupEnabled = true;
        tempGroupEnabled.Unchecked += (_, _) => profile.IsTempClassPlanGroupEnabled = false;
        body.Children.Add(MishaUi.SettingRow("启用临时课表群", "", tempGroupEnabled));

        var tempGroup = new ComboBox { ItemsSource = groups, DisplayMemberPath = nameof(ClassIslandClassPlanGroupRow.Name), MinWidth = 220 };
        tempGroup.SelectedItem = FindGroup(groups, profile.TempClassPlanGroupId);
        tempGroup.SelectionChanged += (_, _) => profile.TempClassPlanGroupId = (tempGroup.SelectedItem as ClassIslandClassPlanGroupRow)?.Id ?? "";
        body.Children.Add(MishaUi.SettingRow("临时课表群", "", tempGroup));

        var type = new ComboBox { ItemsSource = new[] { "Override", "Inherit" }, SelectedIndex = Math.Clamp(profile.TempClassPlanGroupType, 0, 1), Width = 140 };
        type.SelectionChanged += (_, _) => profile.TempClassPlanGroupType = type.SelectedIndex;
        body.Children.Add(MishaUi.SettingRow("临时课表群模式", "", type));

        var expire = new DatePicker { SelectedDate = profile.TempClassPlanGroupExpireTime, Width = 170 };
        expire.SelectedDateChanged += (_, _) =>
        {
            if (expire.SelectedDate is DateTime value) profile.TempClassPlanGroupExpireTime = value;
        };
        body.Children.Add(MishaUi.SettingRow("失效日期", "", expire));

        var save = MishaUi.Button("保存 Profile");
        save.Margin = new Thickness(0, 14, 0, 0);
        save.Click += async (_, _) =>
        {
            await store.SaveProfileAsync();
            status.Text = "已保存临时课表设置。";
        };
        body.Children.Add(save);
    }

    private static ClassIslandClassPlanRow? FindPlan(IReadOnlyList<ClassIslandClassPlanRow> values, string id) =>
        values.FirstOrDefault(x => SameGuid(x.Id, id));

    private static ClassIslandClassPlanGroupRow? FindGroup(IReadOnlyList<ClassIslandClassPlanGroupRow> values, string id) =>
        values.FirstOrDefault(x => SameGuid(x.Id, id));

    private static bool SameGuid(string left, string right) =>
        Guid.TryParse(left, out var a) && Guid.TryParse(right, out var b)
            ? a == b
            : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
