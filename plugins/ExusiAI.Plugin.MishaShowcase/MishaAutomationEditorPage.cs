using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class MishaAutomationEditorPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly ComboBox configs = new() { MinWidth = 220 };
    private readonly ListBox workflows = new();
    private readonly DataGrid triggers = MishaUi.DataGrid();
    private readonly DataGrid actions = MishaUi.DataGrid();
    private readonly ComboBox triggerCatalog = new() { MinWidth = 210, DisplayMemberPath = nameof(ClassIslandAutomationCatalogItem.Name) };
    private readonly ComboBox actionCatalog = new() { MinWidth = 210, DisplayMemberPath = nameof(ClassIslandAutomationCatalogItem.Name) };
    private readonly TextBox itemSettings = JsonEditor(150);
    private readonly TextBox ruleset = JsonEditor(160);
    private readonly TextBlock status = MishaUi.Note("");
    private ClassIslandAutomationDocument? document;
    private bool selectedItemIsTrigger;

    public MishaAutomationEditorPage(MishaPlatformStore store)
    {
        this.store = store;
        var root = MishaUi.Page("自动化", "直接编辑 ClassIsland Workflow、Triggers、Ruleset、ActionSet 与 Actions。此页面只编辑配置，绝不执行工作流或外部行动。");

        configs.SelectionChanged += async (_, _) => await LoadSelectedConfigAsync();
        root.Children.Add(MishaUi.SettingRow("配置方案", "来自 Config/Automations/*.json。", configs));

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        split.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(MishaUi.Section("工作流"));
        workflows.DisplayMemberPath = nameof(ClassIslandWorkflowRow.Name);
        workflows.MinHeight = 350;
        workflows.SelectionChanged += (_, _) => ReloadWorkflow();
        left.Children.Add(workflows);

        var workflowButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var addWorkflow = MishaUi.Button("新增工作流");
        addWorkflow.Click += (_, _) =>
        {
            if (document is null) return;
            document.AddWorkflow();
            ReloadWorkflows(selectLast: true);
        };
        var removeWorkflow = MishaUi.Button("删除", true);
        removeWorkflow.Click += (_, _) =>
        {
            if (document is null || workflows.SelectedItem is not ClassIslandWorkflowRow row) return;
            document.RemoveWorkflow(row);
            ReloadWorkflows();
        };
        var upWorkflow = MishaUi.Button("上移", true);
        upWorkflow.Click += (_, _) => MoveWorkflow(-1);
        var downWorkflow = MishaUi.Button("下移", true);
        downWorkflow.Click += (_, _) => MoveWorkflow(1);
        foreach (var button in new[] { addWorkflow, removeWorkflow, upWorkflow, downWorkflow })
            workflowButtons.Children.Add(button);
        left.Children.Add(workflowButtons);
        Grid.SetColumn(left, 0);
        split.Children.Add(left);

        var right = new StackPanel();
        var workflowName = new TextBox { MinWidth = 260 };
        workflowName.TextChanged += (_, _) =>
        {
            if (workflows.SelectedItem is ClassIslandWorkflowRow row) row.Name = workflowName.Text;
        };
        var enabled = new CheckBox();
        enabled.Checked += (_, _) => { if (workflows.SelectedItem is ClassIslandWorkflowRow row) row.IsEnabled = true; };
        enabled.Unchecked += (_, _) => { if (workflows.SelectedItem is ClassIslandWorkflowRow row) row.IsEnabled = false; };
        var revert = new CheckBox();
        revert.Checked += (_, _) => { if (workflows.SelectedItem is ClassIslandWorkflowRow row) row.IsRevertEnabled = true; };
        revert.Unchecked += (_, _) => { if (workflows.SelectedItem is ClassIslandWorkflowRow row) row.IsRevertEnabled = false; };
        var condition = new CheckBox();
        condition.Checked += (_, _) => { if (workflows.SelectedItem is ClassIslandWorkflowRow row) row.IsConditionEnabled = true; };
        condition.Unchecked += (_, _) => { if (workflows.SelectedItem is ClassIslandWorkflowRow row) row.IsConditionEnabled = false; };

        workflows.SelectionChanged += (_, _) =>
        {
            if (workflows.SelectedItem is ClassIslandWorkflowRow row)
            {
                workflowName.Text = row.Name;
                enabled.IsChecked = row.IsEnabled;
                revert.IsChecked = row.IsRevertEnabled;
                condition.IsChecked = row.IsConditionEnabled;
                ruleset.Text = row.RulesetJson;
            }
            else
            {
                workflowName.Text = "";
                enabled.IsChecked = false;
                revert.IsChecked = false;
                condition.IsChecked = false;
                ruleset.Text = "";
            }
        };

        right.Children.Add(MishaUi.Section("工作流属性"));
        right.Children.Add(MishaUi.SettingRow("名称", "", workflowName));
        right.Children.Add(MishaUi.SettingRow("启用行动组", "", enabled));
        right.Children.Add(MishaUi.SettingRow("启用恢复", "", revert));
        right.Children.Add(MishaUi.SettingRow("启用条件", "", condition));

        right.Children.Add(MishaUi.Section("触发器"));
        ConfigureItemsGrid(triggers);
        triggers.SelectionChanged += (_, _) =>
        {
            if (triggers.SelectedItem is null) return;
            actions.UnselectAll();
            selectedItemIsTrigger = true;
            ReloadItemSettings();
        };
        right.Children.Add(triggers);

        var triggerActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        triggerCatalog.ItemsSource = ClassIslandAutomationCatalog.Triggers;
        triggerCatalog.SelectedIndex = 0;
        triggerActions.Children.Add(triggerCatalog);
        var addTrigger = MishaUi.Button("添加触发器");
        addTrigger.Margin = new Thickness(8, 0, 8, 0);
        addTrigger.Click += (_, _) => AddTrigger();
        var removeTrigger = MishaUi.Button("删除触发器", true);
        removeTrigger.Click += (_, _) => RemoveTrigger();
        var triggerUp = MishaUi.Button("上移", true);
        triggerUp.Click += (_, _) => MoveTrigger(-1);
        var triggerDown = MishaUi.Button("下移", true);
        triggerDown.Click += (_, _) => MoveTrigger(1);
        foreach (var button in new[] { addTrigger, removeTrigger, triggerUp, triggerDown })
            triggerActions.Children.Add(button);
        right.Children.Add(triggerActions);

        right.Children.Add(MishaUi.Section("行动"));
        ConfigureItemsGrid(actions);
        actions.SelectionChanged += (_, _) =>
        {
            if (actions.SelectedItem is null) return;
            triggers.UnselectAll();
            selectedItemIsTrigger = false;
            ReloadItemSettings();
        };
        right.Children.Add(actions);

        var actionActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actionCatalog.ItemsSource = ClassIslandAutomationCatalog.Actions;
        actionCatalog.SelectedIndex = 0;
        actionActions.Children.Add(actionCatalog);
        var addAction = MishaUi.Button("添加行动");
        addAction.Margin = new Thickness(8, 0, 8, 0);
        addAction.Click += (_, _) => AddAction();
        var removeAction = MishaUi.Button("删除行动", true);
        removeAction.Click += (_, _) => RemoveAction();
        var actionUp = MishaUi.Button("上移", true);
        actionUp.Click += (_, _) => MoveAction(-1);
        var actionDown = MishaUi.Button("下移", true);
        actionDown.Click += (_, _) => MoveAction(1);
        foreach (var button in new[] { addAction, removeAction, actionUp, actionDown })
            actionActions.Children.Add(button);
        right.Children.Add(actionActions);

        right.Children.Add(MishaUi.Section("选中触发器 / 行动 Settings"));
        right.Children.Add(MishaUi.Note("Settings=null 会由 ClassIsland 本体根据真实注册类型创建默认设置；这里可直接编辑已有原生 Settings。"));
        right.Children.Add(itemSettings);
        var applySettings = MishaUi.Button("应用 Settings JSON");
        applySettings.Margin = new Thickness(0, 8, 0, 0);
        applySettings.Click += (_, _) => ApplyItemSettings();
        right.Children.Add(applySettings);

        right.Children.Add(MishaUi.Section("Ruleset"));
        right.Children.Add(ruleset);
        var applyRules = MishaUi.Button("应用 Ruleset JSON");
        applyRules.Margin = new Thickness(0, 8, 0, 0);
        applyRules.Click += (_, _) => ApplyRuleset();
        right.Children.Add(applyRules);

        Grid.SetColumn(right, 1);
        split.Children.Add(right);
        root.Children.Add(split);

        var save = MishaUi.Button("保存自动化配置");
        save.Margin = new Thickness(0, 16, 0, 0);
        save.Click += async (_, _) => await SaveAsync();
        root.Children.Add(save);
        root.Children.Add(status);

        Loaded += (_, _) => RefreshConfigList();
        store.Changed += (_, _) => Dispatcher.Invoke(RefreshConfigList);
        Content = MishaUi.Scroll(root);
    }

    private static TextBox JsonEditor(double minHeight) =>
        new()
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = minHeight
        };

    private static void ConfigureItemsGrid(DataGrid grid)
    {
        grid.AutoGenerateColumns = false;
        grid.CanUserAddRows = false;
        grid.MinHeight = 180;
        grid.Columns.Add(new DataGridTextColumn { Header = "顺序", Binding = new Binding(nameof(ClassIslandAutomationItemRow.Index)), IsReadOnly = true, Width = 60 });
        grid.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = new Binding(nameof(ClassIslandAutomationItemRow.DisplayName)), IsReadOnly = true, Width = 170 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Id", Binding = new Binding(nameof(ClassIslandAutomationItemRow.Id)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
    }

    private void RefreshConfigList()
    {
        if (store.Workspace is null)
        {
            configs.ItemsSource = Array.Empty<string>();
            document = null;
            ReloadWorkflows();
            status.Text = "请先连接 ClassIsland Settings.json。";
            return;
        }

        var names = store.Workspace.EnumerateAutomations();
        configs.ItemsSource = names;
        configs.SelectedItem = names.FirstOrDefault(x =>
            string.Equals(x, store.Workspace.CurrentAutomationConfig, StringComparison.OrdinalIgnoreCase));
        if (configs.SelectedItem is null && names.Count > 0) configs.SelectedIndex = 0;
    }

    private async Task LoadSelectedConfigAsync()
    {
        if (store.Workspace is null || configs.SelectedItem is not string name) return;
        var path = Path.Combine(store.Workspace.AutomationsDirectory, name + ".json");
        if (!File.Exists(path))
        {
            document = null;
            ReloadWorkflows();
            status.Text = "配置文件不存在；不会自动生成空工作流或示例配置。";
            return;
        }

        try
        {
            document = await ClassIslandAutomationDocument.LoadAsync(path);
            ReloadWorkflows();
            status.Text = path;
        }
        catch (Exception exception)
        {
            document = null;
            ReloadWorkflows();
            status.Text = $"加载失败：{exception.Message}";
        }
    }

    private void ReloadWorkflows(bool selectLast = false)
    {
        var values = document?.Workflows ?? [];
        workflows.ItemsSource = values;
        if (values.Count > 0)
            workflows.SelectedIndex = selectLast ? values.Count - 1 : Math.Clamp(workflows.SelectedIndex, 0, values.Count - 1);
        else
            ReloadWorkflow();
    }

    private void ReloadWorkflow()
    {
        if (workflows.SelectedItem is not ClassIslandWorkflowRow row)
        {
            triggers.ItemsSource = null;
            actions.ItemsSource = null;
            itemSettings.Text = "";
            ruleset.Text = "";
            return;
        }

        triggers.ItemsSource = row.Triggers;
        actions.ItemsSource = row.Actions;
        ruleset.Text = row.RulesetJson;
        itemSettings.Text = "";
    }

    private void AddTrigger()
    {
        if (document is null ||
            workflows.SelectedItem is not ClassIslandWorkflowRow workflow ||
            triggerCatalog.SelectedItem is not ClassIslandAutomationCatalogItem item) return;
        document.AddTrigger(workflow, item);
        ReloadWorkflow();
        if (workflow.Triggers.Count > 0) triggers.SelectedIndex = workflow.Triggers.Count - 1;
    }

    private void RemoveTrigger()
    {
        if (document is null ||
            workflows.SelectedItem is not ClassIslandWorkflowRow workflow ||
            triggers.SelectedItem is not ClassIslandTriggerRow trigger) return;
        document.RemoveTrigger(workflow, trigger);
        ReloadWorkflow();
    }

    private void MoveTrigger(int delta)
    {
        if (document is null ||
            workflows.SelectedItem is not ClassIslandWorkflowRow workflow ||
            triggers.SelectedItem is not ClassIslandTriggerRow trigger) return;
        var index = trigger.Index - 1;
        document.MoveTrigger(workflow, trigger, delta);
        ReloadWorkflow();
        if (workflow.Triggers.Count > 0)
            triggers.SelectedIndex = Math.Clamp(index + delta, 0, workflow.Triggers.Count - 1);
    }

    private void AddAction()
    {
        if (document is null ||
            workflows.SelectedItem is not ClassIslandWorkflowRow workflow ||
            actionCatalog.SelectedItem is not ClassIslandAutomationCatalogItem item) return;
        document.AddAction(workflow, item);
        ReloadWorkflow();
        if (workflow.Actions.Count > 0) actions.SelectedIndex = workflow.Actions.Count - 1;
    }

    private void RemoveAction()
    {
        if (document is null ||
            workflows.SelectedItem is not ClassIslandWorkflowRow workflow ||
            actions.SelectedItem is not ClassIslandActionRow action) return;
        document.RemoveAction(workflow, action);
        ReloadWorkflow();
    }

    private void MoveAction(int delta)
    {
        if (document is null ||
            workflows.SelectedItem is not ClassIslandWorkflowRow workflow ||
            actions.SelectedItem is not ClassIslandActionRow action) return;
        var index = action.Index - 1;
        document.MoveAction(workflow, action, delta);
        ReloadWorkflow();
        if (workflow.Actions.Count > 0)
            actions.SelectedIndex = Math.Clamp(index + delta, 0, workflow.Actions.Count - 1);
    }

    private void MoveWorkflow(int delta)
    {
        if (document is null || workflows.SelectedItem is not ClassIslandWorkflowRow workflow) return;
        var index = workflow.Index - 1;
        document.MoveWorkflow(workflow, delta);
        ReloadWorkflows();
        if (document.Workflows.Count > 0)
            workflows.SelectedIndex = Math.Clamp(index + delta, 0, document.Workflows.Count - 1);
    }

    private void ReloadItemSettings()
    {
        if (selectedItemIsTrigger && triggers.SelectedItem is ClassIslandTriggerRow trigger)
            itemSettings.Text = trigger.SettingsJson;
        else if (!selectedItemIsTrigger && actions.SelectedItem is ClassIslandActionRow action)
            itemSettings.Text = action.SettingsJson;
        else
            itemSettings.Text = "";
    }

    private void ApplyItemSettings()
    {
        try
        {
            if (selectedItemIsTrigger && triggers.SelectedItem is ClassIslandTriggerRow trigger)
                trigger.SettingsJson = itemSettings.Text;
            else if (!selectedItemIsTrigger && actions.SelectedItem is ClassIslandActionRow action)
                action.SettingsJson = itemSettings.Text;
            else
                return;

            status.Text = "Settings 已应用到内存；保存后写回真实 ClassIsland 自动化配置。";
        }
        catch (JsonException exception)
        {
            status.Text = $"Settings JSON 无效：{exception.Message}";
        }
    }

    private void ApplyRuleset()
    {
        if (workflows.SelectedItem is not ClassIslandWorkflowRow workflow) return;
        try
        {
            workflow.RulesetJson = ruleset.Text;
            status.Text = "Ruleset 已应用到内存。";
        }
        catch (JsonException exception)
        {
            status.Text = $"Ruleset JSON 无效：{exception.Message}";
        }
    }

    private async Task SaveAsync()
    {
        if (document is null)
        {
            status.Text = "没有已加载的自动化配置。";
            return;
        }

        triggers.CommitEdit(DataGridEditingUnit.Row, true);
        actions.CommitEdit(DataGridEditingUnit.Row, true);
        await document.SaveAsync();
        status.Text = $"已保存：{document.FilePath}";
    }
}
