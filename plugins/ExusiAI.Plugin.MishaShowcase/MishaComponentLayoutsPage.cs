using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class MishaComponentLayoutsPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly ComboBox configs = new() { MinWidth = 220 };
    private readonly ListBox lines = new();
    private readonly DataGrid components = MishaUi.DataGrid();
    private readonly ComboBox addComponentCatalog = new() { MinWidth = 220, DisplayMemberPath = nameof(ClassIslandComponentCatalogItem.Name) };
    private readonly TextBox settingsEditor = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = 150
    };
    private readonly TextBlock status = MishaUi.Note("");
    private ClassIslandComponentLayoutDocument? document;

    public MishaComponentLayoutsPage(MishaPlatformStore store)
    {
        this.store = store;
        var root = MishaUi.Page("组件配置", "直接编辑 ClassIsland ComponentProfile 的 Lines、Children 和 ComponentSettings。不会创建示例组件配置。");

        configs.SelectionChanged += async (_, _) => await LoadSelectedConfigAsync();
        root.Children.Add(MishaUi.SettingRow("配置方案", "来自 Config/ComponentLayouts/*.json。", configs));

        var lineArea = new Grid();
        lineArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        lineArea.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(MishaUi.Section("主界面行"));
        lines.DisplayMemberPath = nameof(ClassIslandComponentLineRow.Index);
        lines.MinHeight = 280;
        lines.SelectionChanged += (_, _) => ReloadComponents();
        left.Children.Add(lines);

        var lineButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var addLine = MishaUi.Button("新增行");
        addLine.Click += (_, _) =>
        {
            if (document is null) return;
            document.AddLine();
            ReloadLines(selectLast: true);
        };
        var removeLine = MishaUi.Button("删除行", true);
        removeLine.Click += (_, _) =>
        {
            if (document is null || lines.SelectedItem is not ClassIslandComponentLineRow line) return;
            document.RemoveLine(line);
            ReloadLines();
        };
        var upLine = MishaUi.Button("上移", true);
        upLine.Click += (_, _) => MoveLine(-1);
        var downLine = MishaUi.Button("下移", true);
        downLine.Click += (_, _) => MoveLine(1);
        foreach (var button in new[] { addLine, removeLine, upLine, downLine }) lineButtons.Children.Add(button);
        left.Children.Add(lineButtons);

        Grid.SetColumn(left, 0);
        lineArea.Children.Add(left);

        var right = new StackPanel();
        right.Children.Add(MishaUi.Section("行属性"));

        var mainLine = new CheckBox();
        mainLine.Checked += (_, _) => { if (lines.SelectedItem is ClassIslandComponentLineRow line) line.IsMainLine = true; };
        mainLine.Unchecked += (_, _) => { if (lines.SelectedItem is ClassIslandComponentLineRow line) line.IsMainLine = false; };
        var notify = new CheckBox();
        notify.Checked += (_, _) => { if (lines.SelectedItem is ClassIslandComponentLineRow line) line.IsNotificationEnabled = true; };
        notify.Unchecked += (_, _) => { if (lines.SelectedItem is ClassIslandComponentLineRow line) line.IsNotificationEnabled = false; };
        lines.SelectionChanged += (_, _) =>
        {
            if (lines.SelectedItem is ClassIslandComponentLineRow line)
            {
                mainLine.IsChecked = line.IsMainLine;
                notify.IsChecked = line.IsNotificationEnabled;
            }
            else
            {
                mainLine.IsChecked = false;
                notify.IsChecked = false;
            }
        };
        right.Children.Add(MishaUi.SettingRow("主要行", "", mainLine));
        right.Children.Add(MishaUi.SettingRow("启用提醒", "", notify));

        right.Children.Add(MishaUi.Section("组件"));
        ConfigureComponentsGrid();
        right.Children.Add(components);

        var addRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        addComponentCatalog.ItemsSource = ClassIslandComponentCatalog.BuiltIns;
        addComponentCatalog.SelectedIndex = 0;
        addRow.Children.Add(addComponentCatalog);
        var addComponent = MishaUi.Button("添加组件");
        addComponent.Margin = new Thickness(8, 0, 8, 0);
        addComponent.Click += (_, _) => AddComponent();
        var removeComponent = MishaUi.Button("删除组件", true);
        removeComponent.Click += (_, _) => RemoveComponent();
        var componentUp = MishaUi.Button("组件上移", true);
        componentUp.Click += (_, _) => MoveComponent(-1);
        var componentDown = MishaUi.Button("组件下移", true);
        componentDown.Click += (_, _) => MoveComponent(1);
        foreach (var button in new[] { addComponent, removeComponent, componentUp, componentDown }) addRow.Children.Add(button);
        right.Children.Add(addRow);

        right.Children.Add(MishaUi.Section("组件 Settings"));
        right.Children.Add(MishaUi.Note("这里编辑选中组件原生 Settings 节点。新增组件保持 Settings=null，由 ClassIsland 本体按组件类型创建真正默认设置。"));
        components.SelectionChanged += (_, _) => ReloadSettingsEditor();
        right.Children.Add(settingsEditor);

        var settingsActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var applySettings = MishaUi.Button("应用 Settings JSON");
        applySettings.Click += (_, _) => ApplySettingsJson();
        var resetSettings = MishaUi.Button("设为 null", true);
        resetSettings.Click += (_, _) =>
        {
            if (components.SelectedItem is not ClassIslandComponentRow row) return;
            row.Node["Settings"] = null;
            ReloadSettingsEditor();
        };
        settingsActions.Children.Add(applySettings);
        settingsActions.Children.Add(resetSettings);
        right.Children.Add(settingsActions);

        Grid.SetColumn(right, 1);
        lineArea.Children.Add(right);
        root.Children.Add(lineArea);

        var save = MishaUi.Button("保存组件配置");
        save.Margin = new Thickness(0, 16, 0, 0);
        save.Click += async (_, _) => await SaveAsync();
        root.Children.Add(save);
        root.Children.Add(status);

        Loaded += (_, _) => RefreshConfigList();
        store.Changed += (_, _) => Dispatcher.Invoke(RefreshConfigList);
        Content = MishaUi.Scroll(root);
    }

    private void ConfigureComponentsGrid()
    {
        components.AutoGenerateColumns = false;
        components.CanUserAddRows = false;
        components.MinHeight = 260;
        components.Columns.Add(new DataGridTextColumn { Header = "顺序", Binding = new Binding(nameof(ClassIslandComponentRow.Index)), IsReadOnly = true, Width = 60 });
        components.Columns.Add(new DataGridTextColumn { Header = "组件", Binding = new Binding(nameof(ClassIslandComponentRow.DisplayName)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        components.Columns.Add(new DataGridTextColumn { Header = "Id", Binding = new Binding(nameof(ClassIslandComponentRow.Id)), Width = 275 });
        components.Columns.Add(new DataGridTextColumn { Header = "相对行", Binding = new Binding(nameof(ClassIslandComponentRow.RelativeLineNumber)), Width = 75 });
        components.Columns.Add(new DataGridCheckBoxColumn { Header = "规则隐藏", Binding = new Binding(nameof(ClassIslandComponentRow.HideOnRule)), Width = 80 });
        components.Columns.Add(new DataGridCheckBoxColumn { Header = "固定宽度", Binding = new Binding(nameof(ClassIslandComponentRow.IsFixedWidthEnabled)), Width = 80 });
        components.Columns.Add(new DataGridTextColumn { Header = "宽度", Binding = new Binding(nameof(ClassIslandComponentRow.FixedWidth)), Width = 80 });
        components.Columns.Add(new DataGridTextColumn { Header = "透明度", Binding = new Binding(nameof(ClassIslandComponentRow.Opacity)), Width = 80 });
        components.Columns.Add(new DataGridCheckBoxColumn { Header = "自定义前景", Binding = new Binding(nameof(ClassIslandComponentRow.IsCustomForegroundColorEnabled)), Width = 95 });
        components.Columns.Add(new DataGridTextColumn { Header = "前景色 JSON", Binding = new Binding(nameof(ClassIslandComponentRow.ForegroundColor)), Width = 150 });
        components.Columns.Add(new DataGridCheckBoxColumn { Header = "自定义背景", Binding = new Binding(nameof(ClassIslandComponentRow.IsCustomBackgroundColorEnabled)), Width = 95 });
        components.Columns.Add(new DataGridTextColumn { Header = "背景色 JSON", Binding = new Binding(nameof(ClassIslandComponentRow.BackgroundColor)), Width = 150 });
        components.Columns.Add(new DataGridCheckBoxColumn { Header = "自定义圆角", Binding = new Binding(nameof(ClassIslandComponentRow.IsCustomCornerRadiusEnabled)), Width = 95 });
        components.Columns.Add(new DataGridTextColumn { Header = "圆角", Binding = new Binding(nameof(ClassIslandComponentRow.CustomCornerRadius)), Width = 70 });
    }

    private void RefreshConfigList()
    {
        if (store.Workspace is null)
        {
            configs.ItemsSource = Array.Empty<string>();
            document = null;
            ReloadLines();
            status.Text = "请先连接 ClassIsland Settings.json。";
            return;
        }

        var names = store.Workspace.EnumerateComponentLayouts();
        configs.ItemsSource = names;
        var current = store.Workspace.CurrentComponentConfig;
        configs.SelectedItem = names.FirstOrDefault(x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase));
        if (configs.SelectedItem is null && names.Count > 0) configs.SelectedIndex = 0;
    }

    private async Task LoadSelectedConfigAsync()
    {
        if (store.Workspace is null || configs.SelectedItem is not string name) return;

        var path = Path.Combine(store.Workspace.ComponentLayoutsDirectory, name + ".json");
        if (!File.Exists(path))
        {
            document = null;
            ReloadLines();
            status.Text = "配置文件不存在；不会创建示例配置。";
            return;
        }

        try
        {
            document = await ClassIslandComponentLayoutDocument.LoadAsync(path);
            ReloadLines();
            status.Text = path;
        }
        catch (Exception exception)
        {
            document = null;
            ReloadLines();
            status.Text = $"加载失败：{exception.Message}";
        }
    }

    private void ReloadLines(bool selectLast = false)
    {
        var values = document?.Lines ?? [];
        lines.ItemsSource = values;
        if (values.Count > 0)
            lines.SelectedIndex = selectLast ? values.Count - 1 : Math.Clamp(lines.SelectedIndex, 0, values.Count - 1);
        else
            components.ItemsSource = null;
    }

    private void ReloadComponents()
    {
        components.ItemsSource = lines.SelectedItem is ClassIslandComponentLineRow line ? line.Components : null;
        ReloadSettingsEditor();
    }

    private void AddComponent()
    {
        if (document is null ||
            lines.SelectedItem is not ClassIslandComponentLineRow line ||
            addComponentCatalog.SelectedItem is not ClassIslandComponentCatalogItem item) return;

        document.AddComponent(line, item);
        ReloadComponents();
        if (line.Components.Count > 0) components.SelectedIndex = line.Components.Count - 1;
    }

    private void RemoveComponent()
    {
        if (document is null ||
            lines.SelectedItem is not ClassIslandComponentLineRow line ||
            components.SelectedItem is not ClassIslandComponentRow component) return;

        document.RemoveComponent(line, component);
        ReloadComponents();
    }

    private void MoveComponent(int delta)
    {
        if (document is null ||
            lines.SelectedItem is not ClassIslandComponentLineRow line ||
            components.SelectedItem is not ClassIslandComponentRow component) return;

        var index = component.Index - 1;
        document.MoveComponent(line, component, delta);
        ReloadComponents();
        if (line.Components.Count > 0)
            components.SelectedIndex = Math.Clamp(index + delta, 0, line.Components.Count - 1);
    }

    private void MoveLine(int delta)
    {
        if (document is null || lines.SelectedItem is not ClassIslandComponentLineRow line) return;
        var index = line.Index - 1;
        document.MoveLine(line, delta);
        ReloadLines();
        if (document.Lines.Count > 0)
            lines.SelectedIndex = Math.Clamp(index + delta, 0, document.Lines.Count - 1);
    }

    private void ReloadSettingsEditor()
    {
        settingsEditor.Text = components.SelectedItem is ClassIslandComponentRow row ? row.SettingsJson : "";
    }

    private void ApplySettingsJson()
    {
        if (components.SelectedItem is not ClassIslandComponentRow row) return;
        try
        {
            row.SettingsJson = settingsEditor.Text;
            status.Text = "Settings JSON 已应用到内存；点击保存后写回 ClassIsland 配置。";
        }
        catch (JsonException exception)
        {
            status.Text = $"Settings JSON 无效：{exception.Message}";
        }
    }

    private async Task SaveAsync()
    {
        if (document is null)
        {
            status.Text = "没有已加载的组件配置。";
            return;
        }

        components.CommitEdit(DataGridEditingUnit.Row, true);
        await document.SaveAsync();
        store.NotifyConfigurationChanged();
        status.Text = $"已保存：{document.FilePath}";
    }
}
