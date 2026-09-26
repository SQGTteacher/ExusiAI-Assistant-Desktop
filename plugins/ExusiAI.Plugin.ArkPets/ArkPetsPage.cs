using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ExusiAI.Plugin.ArkPets;

internal sealed class ArkPetsPage : UserControl
{
    private static readonly Brush Theme = Brush("#2A528C");
    private static readonly Brush ThemeLight = Brush("#86ABDE");
    private static readonly Brush Ink = Brush("#242424");
    private static readonly Brush Paper = Brushes.White;

    private readonly ArkPetsController controller;
    private readonly Grid contentHost = new();
    private readonly Button modelsButton;
    private readonly Button behaviorButton;
    private readonly Button optionsButton;
    private readonly Button launchButton;
    private ListView? modelList;
    private TextBox? searchBox;
    private ComboBox? typeFilter;
    private TextBlock? modelStatus;
    private TextBlock? modelName;
    private TextBlock? modelDetails;
    private TextBlock? integrationStatus;
    private bool loaded;
    private bool isModelsView;
    private bool suppressModelSelection;

    public ArkPetsPage(ArkPetsController controller)
    {
        this.controller = controller;
        MinWidth = 720;
        modelsButton = MenuButton("模型");
        behaviorButton = MenuButton("行为");
        optionsButton = MenuButton("选项");
        launchButton = PrimaryButton("▶  启动");
        launchButton.Height = 42;
        launchButton.FontSize = 17;

        Content = BuildShell();
        modelsButton.Click += (_, _) => ShowModels();
        behaviorButton.Click += (_, _) => ShowBehavior();
        optionsButton.Click += (_, _) => ShowOptions();
        launchButton.Click += async (_, _) => await LaunchAsync();
        controller.Changed += Controller_OnChanged;
        Loaded += async (_, _) =>
        {
            if (loaded) return;
            loaded = true;
            if (!string.IsNullOrWhiteSpace(controller.Settings.ModelRoot) && controller.Catalog.Models.Count == 0)
            {
                try { await controller.ReloadModelsAsync(); }
                catch { }
            }
            ShowModels();
        };
        Unloaded += (_, _) => controller.Changed -= Controller_OnChanged;
    }

    private FrameworkElement BuildShell()
    {
        var frame = new Border
        {
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("#D7DEEA"),
            Background = Paper,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        frame.Child = root;

        var sidebar = new Border
        {
            Background = ThemeLight,
            CornerRadius = new CornerRadius(8, 0, 0, 8),
            Padding = new Thickness(10, 18, 10, 14)
        };
        var sidebarGrid = new Grid();
        sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "ArkPets",
            Foreground = Theme,
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12)
        };
        Grid.SetRow(title, 0);
        sidebarGrid.Children.Add(title);

        var rule = new Border { Height = 1, Background = Brush("#60718A"), Opacity = 0.45, Margin = new Thickness(2, 0, 2, 14) };
        Grid.SetRow(rule, 1);
        sidebarGrid.Children.Add(rule);

        var menu = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        menu.Children.Add(modelsButton);
        menu.Children.Add(behaviorButton);
        menu.Children.Add(optionsButton);
        Grid.SetRow(menu, 2);
        sidebarGrid.Children.Add(menu);

        Grid.SetRow(launchButton, 3);
        sidebarGrid.Children.Add(launchButton);
        sidebar.Child = sidebarGrid;
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);

        contentHost.Margin = new Thickness(18);
        Grid.SetColumn(contentHost, 1);
        root.Children.Add(contentHost);
        return frame;
    }

    private void ShowModels()
    {
        isModelsView = true;
        SetActive(modelsButton);
        contentHost.Children.Clear();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new Border
        {
            Background = ThemeLight,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var tools = new DockPanel { LastChildFill = true };
        var leftTools = new StackPanel { Orientation = Orientation.Horizontal };
        var reload = SecondaryButton("↻  重载");
        var random = SecondaryButton("⤨  随机");
        var reset = SecondaryButton("重置");
        reload.Click += async (_, _) => await ReloadModelsAsync();
        random.Click += async (_, _) =>
        {
            var selected = await controller.SelectRandomModelAsync(FilterModels());
            if (selected is not null) RefreshModelList(selected.Key);
        };
        reset.Click += (_, _) =>
        {
            if (searchBox is not null) searchBox.Text = "";
            if (typeFilter is not null) typeFilter.SelectedIndex = 0;
            RefreshModelList();
        };
        leftTools.Children.Add(reload);
        leftTools.Children.Add(random);
        leftTools.Children.Add(reset);
        DockPanel.SetDock(leftTools, Dock.Left);
        tools.Children.Add(leftTools);

        searchBox = new TextBox
        {
            MinWidth = 180,
            Height = 30,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "搜索名称 / 代号 / 资源键 / 时装"
        };
        searchBox.TextChanged += (_, _) => RefreshModelList();
        tools.Children.Add(searchBox);
        toolbar.Child = tools;
        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var filterRow = new DockPanel { Margin = new Thickness(0, 0, 8, 8) };
        filterRow.Children.Add(new TextBlock
        {
            Text = "模型筛选",
            Foreground = Theme,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        typeFilter = new ComboBox { Width = 150, Height = 28, HorizontalAlignment = HorizontalAlignment.Right };
        typeFilter.SelectionChanged += (_, _) => RefreshModelList();
        DockPanel.SetDock(typeFilter, Dock.Right);
        filterRow.Children.Add(typeFilter);
        Grid.SetRow(filterRow, 0);
        left.Children.Add(filterRow);

        modelList = new ListView
        {
            Margin = new Thickness(0, 0, 8, 0),
            BorderBrush = Brush("#C8D2E2"),
            BorderThickness = new Thickness(1),
            DisplayMemberPath = nameof(ArkPetModel.DisplayName)
        };
        modelList.SelectionChanged += async (_, _) =>
        {
            if (suppressModelSelection || modelList.SelectedItem is not ArkPetModel model) return;
            if (!string.Equals(controller.Settings.SelectedModelKey, model.Key, StringComparison.OrdinalIgnoreCase))
                await controller.SelectModelAsync(model.Key);
            RefreshModelDetails(model);
        };
        Grid.SetRow(modelList, 1);
        left.Children.Add(modelList);
        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        var info = new Border
        {
            BorderBrush = Brush("#C8D2E2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = Paper,
            Padding = new Thickness(14)
        };
        var infoStack = new StackPanel();
        modelName = new TextBlock
        {
            Text = "请选择模型",
            Foreground = Ink,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        };
        modelDetails = new TextBlock
        {
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 14),
            TextWrapping = TextWrapping.Wrap
        };
        infoStack.Children.Add(modelName);
        infoStack.Children.Add(modelDetails);
        infoStack.Children.Add(GroupTitle("模型库管理"));

        var modelRoot = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(controller.Settings.ModelRoot) ? "未选择 Ark-Models 模型库" : controller.Settings.ModelRoot,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(4, 8, 4, 8)
        };
        var chooseLibrary = SecondaryButton("选择模型库");
        chooseLibrary.HorizontalAlignment = HorizontalAlignment.Left;
        chooseLibrary.Click += async (_, _) => await ChooseModelRootAsync();
        infoStack.Children.Add(modelRoot);
        infoStack.Children.Add(chooseLibrary);

        infoStack.Children.Add(GroupTitle("兼容信息"));
        var compatibility = new TextBlock
        {
            Text = controller.Catalog.Models.Count == 0
                ? "加载 Ark-Models 后显示数据版本和兼容版本。"
                : $"ArkPets 兼容：{controller.Catalog.Compatibility}\n游戏数据：{controller.Catalog.GameDataVersionDescription}\n区域：{controller.Catalog.GameDataServerRegion}",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 0)
        };
        infoStack.Children.Add(compatibility);
        info.Child = new ScrollViewer { Content = infoStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(info, 1);
        body.Children.Add(info);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        modelStatus = new TextBlock { Foreground = Theme, Margin = new Thickness(2, 9, 0, 0) };
        Grid.SetRow(modelStatus, 2);
        root.Children.Add(modelStatus);

        contentHost.Children.Add(root);
        PopulateTypeFilter();
        RefreshModelList(controller.Settings.SelectedModelKey);
    }

    private void ShowBehavior()
    {
        isModelsView = false;
        modelStatus = null;
        SetActive(behaviorButton);
        contentHost.Children.Clear();

        var stack = new StackPanel();
        stack.Children.Add(GroupTitle("行为设置"));
        stack.Children.Add(Toggle("允许鼠标交互", controller.Settings.BehaviorAllowInteract, value => s => s with { BehaviorAllowInteract = value }));
        stack.Children.Add(Toggle("允许行走", controller.Settings.BehaviorAllowWalk, value => s => s with { BehaviorAllowWalk = value }));
        stack.Children.Add(Toggle("允许坐下", controller.Settings.BehaviorAllowSit, value => s => s with { BehaviorAllowSit = value }));
        stack.Children.Add(Toggle("允许睡觉", controller.Settings.BehaviorAllowSleep, value => s => s with { BehaviorAllowSleep = value }));
        stack.Children.Add(Toggle("允许特殊基建动作", controller.Settings.BehaviorAllowSpecial, value => s => s with { BehaviorAllowSpecial = value }));
        stack.Children.Add(Toggle("桌宠之间相互避让", controller.Settings.BehaviorDoPeerRepulsion, value => s => s with { BehaviorDoPeerRepulsion = value }));
        stack.Children.Add(NumberSlider("行走速度", controller.Settings.BehaviorWalkSpeed, 5, 120, " px/s", value => s => s with { BehaviorWalkSpeed = value }));
        stack.Children.Add(NumberSlider("AI 活跃程度", controller.Settings.BehaviorAiActivation, 1, 10, "", value => s => s with { BehaviorAiActivation = (int)Math.Round(value) }));

        stack.Children.Add(GroupTitle("物理设置"));
        stack.Children.Add(NumberSlider("重力加速度", controller.Settings.PhysicGravityAcc, 0, 1600, "", value => s => s with { PhysicGravityAcc = value }));
        stack.Children.Add(NumberSlider("空气阻力", controller.Settings.PhysicAirFrictionAcc, 0, 500, "", value => s => s with { PhysicAirFrictionAcc = value }));
        stack.Children.Add(NumberSlider("静摩擦", controller.Settings.PhysicStaticFrictionAcc, 0, 1200, "", value => s => s with { PhysicStaticFrictionAcc = value }));
        stack.Children.Add(NumberSlider("水平速度上限", controller.Settings.PhysicSpeedLimitX, 100, 2000, "", value => s => s with { PhysicSpeedLimitX = value }));
        stack.Children.Add(NumberSlider("垂直速度上限", controller.Settings.PhysicSpeedLimitY, 100, 2000, "", value => s => s with { PhysicSpeedLimitY = value }));

        contentHost.Children.Add(new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
    }

    private void ShowOptions()
    {
        isModelsView = false;
        modelStatus = null;
        SetActive(optionsButton);
        contentHost.Children.Clear();

        var stack = new StackPanel();
        stack.Children.Add(GroupTitle("ArkPets 运行时"));
        stack.Children.Add(PathRow(
            "程序",
            string.IsNullOrWhiteSpace(controller.Settings.RuntimePath) ? "未选择 ArkPets.exe / ArkPets.jar" : controller.Settings.RuntimePath,
            "选择",
            async () => await ChooseRuntimeAsync()));
        stack.Children.Add(PathRow(
            "模型库",
            string.IsNullOrWhiteSpace(controller.Settings.ModelRoot) ? "未选择 Ark-Models" : controller.Settings.ModelRoot,
            "选择",
            async () => await ChooseModelRootAsync()));

        stack.Children.Add(GroupTitle("显示设置"));
        stack.Children.Add(NumberSlider("最大帧率", controller.Settings.DisplayFps, 15, 144, " FPS", value => s => s with { DisplayFps = (int)Math.Round(value) }));
        stack.Children.Add(NumberSlider("显示缩放", controller.Settings.DisplayScale, 0.35, 2.5, "×", value => s => s with { DisplayScale = Math.Round(value, 2) }));
        stack.Children.Add(NumberSlider("下边界距离", controller.Settings.DisplayMarginBottom, 0, 300, " px", value => s => s with { DisplayMarginBottom = (int)Math.Round(value) }));
        stack.Children.Add(Toggle("允许跨多显示器移动", controller.Settings.DisplayMultiMonitors, value => s => s with { DisplayMultiMonitors = value }));
        stack.Children.Add(NumberSlider("正常透明度", controller.Settings.OpacityNormal, 0.2, 1, "", value => s => s with { OpacityNormal = Math.Round(value, 2) }));
        stack.Children.Add(NumberSlider("淡化透明度", controller.Settings.OpacityDim, 0.1, 1, "", value => s => s with { OpacityDim = Math.Round(value, 2) }));

        stack.Children.Add(GroupTitle("渲染设置"));
        stack.Children.Add(Toggle("启用 Mipmap", controller.Settings.RenderEnableMipmap, value => s => s with { RenderEnableMipmap = value }));
        stack.Children.Add(Toggle("高质量着色器", controller.Settings.RenderShaderHighQuality, value => s => s with { RenderShaderHighQuality = value }));
        stack.Children.Add(NumberSlider("动画混合", controller.Settings.RenderAnimationMixture, 0, 1, "", value => s => s with { RenderAnimationMixture = Math.Round(value, 2) }));
        stack.Children.Add(NumberSlider("描边宽度", controller.Settings.RenderOutlineWidth, 0, 8, " px", value => s => s with { RenderOutlineWidth = Math.Round(value, 1) }));

        stack.Children.Add(GroupTitle("窗口与程序"));
        stack.Children.Add(Toggle("桌宠窗口置顶", controller.Settings.WindowStyleTopmost, value => s => s with { WindowStyleTopmost = value }));
        stack.Children.Add(Toggle("桌宠作为后台程序启动", controller.Settings.WindowStyleToolwindow, value => s => s with { WindowStyleToolwindow = value }));
        stack.Children.Add(Toggle("长时间未交互时降低帧率", controller.Settings.EcoMode, value => s => s with { EcoMode = value }));

        stack.Children.Add(GroupTitle("ClassIsland 可选联动"));
        stack.Children.Add(Toggle(
            "上下课提醒",
            controller.Settings.ClassIslandRemindersEnabled,
            value => s => s with { ClassIslandRemindersEnabled = value },
            controller.ClassIslandAvailable));
        stack.Children.Add(Toggle(
            "课间自动整理本节课新/修改的课件文件",
            controller.Settings.OrganizeDesktopDuringBreaks,
            value => s => s with { OrganizeDesktopDuringBreaks = value },
            controller.ClassIslandAvailable));
        integrationStatus = new TextBlock
        {
            Text = controller.ClassIslandAvailable
                ? "已检测到独立的 ClassIsland 2.2 Misha 插件。联动通过独立状态桥接文件读取课程阶段；整理功能只移动本节课开始后新增/修改的常见文档、课件和图片，不处理程序、快捷方式或文件夹。"
                : "未检测到 ClassIsland 2.2 Misha。联动选项已禁用，桌宠本体和 ArkPets 功能不受影响。",
            Foreground = controller.ClassIslandAvailable ? Theme : Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 10)
        };
        stack.Children.Add(integrationStatus);

        stack.Children.Add(GroupTitle("关于"));
        var about = new TextBlock
        {
            Text = "Ark-Pets © 2022-2026 Harry Huang · GPL-3.0\nArk-Models 模型资源版权归上海鹰角网络有限公司所有；本插件不将模型素材重新许可为 GPL。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(4, 8, 4, 16)
        };
        stack.Children.Add(about);

        contentHost.Children.Add(new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
    }

    private async Task ReloadModelsAsync()
    {
        try
        {
            await controller.ReloadModelsAsync();
            PopulateTypeFilter();
            RefreshModelList(controller.Settings.SelectedModelKey);
        }
        catch (Exception exception)
        {
            SetStatus($"模型库加载失败：{exception.Message}");
        }
    }

    private async Task ChooseRuntimeAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 ArkPets v3.x 运行时",
            Filter = "ArkPets 程序 (*.exe;*.jar)|*.exe;*.jar|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        await controller.UpdateSettingsAsync(x => x with { RuntimePath = dialog.FileName });
        ShowOptions();
    }

    private async Task ChooseModelRootAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含 models_data.json 的 Ark-Models 模型库目录",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            await controller.UpdateSettingsAsync(x => x with { ModelRoot = dialog.FolderName }, reloadModels: true);
            ShowModels();
        }
        catch (Exception exception)
        {
            SetStatus($"模型库加载失败：{exception.Message}");
        }
    }

    private async Task LaunchAsync()
    {
        try
        {
            var process = await controller.LaunchSelectedAsync();
            SetStatus($"已启动 {controller.SelectedModel?.DisplayName ?? "桌宠"} · PID {process.Id}");
        }
        catch (Exception exception)
        {
            SetStatus($"启动失败：{exception.Message}");
        }
    }

    private IEnumerable<ArkPetModel> FilterModels()
    {
        IEnumerable<ArkPetModel> items = controller.Catalog.Models;
        if (typeFilter?.SelectedItem is string type && type != "全部")
            items = items.Where(x => string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase));

        var query = searchBox?.Text.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items.Where(x =>
                x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                x.Appellation.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                x.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.SkinGroupName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                x.SortTags.Any(tag => tag.Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        }
        return items;
    }

    private void RefreshModelList(string? selectKey = null)
    {
        if (modelList is null) return;
        var items = FilterModels().ToArray();
        var key = selectKey ?? controller.Settings.SelectedModelKey;
        var selected = items.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        suppressModelSelection = true;
        try
        {
            modelList.ItemsSource = items;
            modelList.SelectedItem = selected;
            if (selected is not null)
                modelList.ScrollIntoView(selected);
        }
        finally
        {
            suppressModelSelection = false;
        }
        RefreshModelDetails(selected);
        if (modelStatus is not null)
            modelStatus.Text = controller.Catalog.Models.Count == 0
                ? "请选择 Ark-Models 模型库；插件直接兼容其 models_data.json。"
                : $"已载入 {controller.Catalog.Models.Count} 个模型 · 当前筛选 {items.Length} 个 · 可用 {items.Count(x => x.IsAvailable)} 个";
    }

    private void RefreshModelDetails(ArkPetModel? model)
    {
        if (modelName is null || modelDetails is null) return;
        if (model is null)
        {
            modelName.Text = "请选择模型";
            modelDetails.Text = "";
            return;
        }

        modelName.Text = model.DisplayName;
        var tags = model.SortTags.Count == 0 ? "—" : string.Join(" / ", model.SortTags);
        modelDetails.Text =
            $"{model.Subtitle}\n\n资源键：{model.Key}\n时装系列：{(string.IsNullOrWhiteSpace(model.SkinGroupName) ? "—" : model.SkinGroupName)}\n标签：{tags}\n状态：{(model.IsAvailable ? "资源完整" : "缺少资源文件")}";
    }

    private void PopulateTypeFilter()
    {
        if (typeFilter is null) return;
        var selected = typeFilter.SelectedItem as string ?? "全部";
        var values = new[] { "全部" }
            .Concat(controller.Catalog.Models.Select(x => x.Type).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Order())
            .ToArray();
        typeFilter.ItemsSource = values;
        typeFilter.SelectedItem = values.Contains(selected, StringComparer.OrdinalIgnoreCase) ? selected : "全部";
    }

    private FrameworkElement Toggle(
        string label,
        bool initial,
        Func<bool, Func<ArkPetsSettings, ArkPetsSettings>> update,
        bool enabled = true)
    {
        var check = new CheckBox
        {
            Content = label,
            IsChecked = initial,
            IsEnabled = enabled,
            Margin = new Thickness(8, 7, 8, 7),
            Foreground = Ink
        };
        check.Checked += async (_, _) => await controller.UpdateSettingsAsync(update(true));
        check.Unchecked += async (_, _) => await controller.UpdateSettingsAsync(update(false));
        return check;
    }

    private FrameworkElement NumberSlider(
        string label,
        double initial,
        double minimum,
        double maximum,
        string suffix,
        Func<double, Func<ArkPetsSettings, ArkPetsSettings>> update)
    {
        var row = new Grid { Margin = new Thickness(4, 6, 4, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var text = new TextBlock { Text = label, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(initial, minimum, maximum),
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var valueText = new TextBlock
        {
            Text = $"{initial:0.##}{suffix}",
            Foreground = Brushes.DimGray,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.ValueChanged += (_, args) => valueText.Text = $"{args.NewValue:0.##}{suffix}";
        slider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent, new System.Windows.Controls.Primitives.DragCompletedEventHandler(async (_, _) =>
        {
            await controller.UpdateSettingsAsync(update(slider.Value));
        }));

        Grid.SetColumn(text, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(valueText, 2);
        row.Children.Add(text);
        row.Children.Add(slider);
        row.Children.Add(valueText);
        return row;
    }

    private FrameworkElement PathRow(string label, string value, string buttonText, Func<Task> action)
    {
        var row = new Grid { Margin = new Thickness(4, 8, 4, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock { Text = label, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };
        var path = new TextBlock
        {
            Text = value,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0)
        };
        var button = SecondaryButton(buttonText);
        button.Click += async (_, _) => await action();

        Grid.SetColumn(name, 0);
        Grid.SetColumn(path, 1);
        Grid.SetColumn(button, 2);
        row.Children.Add(name);
        row.Children.Add(path);
        row.Children.Add(button);
        return row;
    }

    private void Controller_OnChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => Controller_OnChanged(sender, e));
            return;
        }
        if (isModelsView && modelList is not null && contentHost.Children.Count > 0)
            RefreshModelList(controller.Settings.SelectedModelKey);
    }

    private void SetActive(Button active)
    {
        foreach (var button in new[] { modelsButton, behaviorButton, optionsButton })
        {
            button.Background = ReferenceEquals(button, active) ? Theme : Paper;
            button.Foreground = ReferenceEquals(button, active) ? Brushes.White : Theme;
        }
    }

    private void SetStatus(string message)
    {
        if (modelStatus is not null)
        {
            modelStatus.Text = message;
            return;
        }
        integrationStatus ??= new TextBlock();
        integrationStatus.Text = message;
    }

    private static TextBlock GroupTitle(string text) =>
        new()
        {
            Text = text,
            Foreground = Theme,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 16, 4, 6),
            Padding = new Thickness(7, 0, 0, 0)
        };

    private static Button MenuButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Height = 42,
            Margin = new Thickness(0, 5, 0, 5),
            FontSize = 17,
            Foreground = Theme,
            Background = Paper,
            BorderBrush = Theme,
            BorderThickness = new Thickness(1.5),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(24, 0, 0, 0)
        };
        return button;
    }

    private static Button PrimaryButton(string text) =>
        new()
        {
            Content = text,
            Background = Theme,
            Foreground = Brushes.White,
            BorderBrush = Theme,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(3)
        };

    private static Button SecondaryButton(string text) =>
        new()
        {
            Content = text,
            Background = Paper,
            Foreground = Theme,
            BorderBrush = Theme,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(3, 0, 3, 0)
        };

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
