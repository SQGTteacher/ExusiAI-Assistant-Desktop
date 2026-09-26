using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private Image? modelPreview;
    private TextBlock? modelPreviewPlaceholder;
    private TextBlock? integrationStatus;
    private Button? favoriteFilterButton;
    private Button? selectedFavoriteButton;
    private ListBox? runningList;
    private ListBox? controlledList;
    private TextBlock? runtimeStatus;
    private bool loaded;
    private bool isModelsView;
    private bool suppressModelSelection;
    private bool favoriteOnly;
    private readonly HashSet<string> selectedTagFilters = new(StringComparer.OrdinalIgnoreCase);

    private sealed record ChoiceOption<T>(string Label, T Value);

    public ArkPetsPage(ArkPetsController controller)
    {
        this.controller = controller;
        MinWidth = 720;

        // ArkPets 在宿主内仍保持上游的浅色蓝白工作台。
        // 在插件作用域内覆盖宿主的深色动态资源，避免 TextBox、ComboBox、
        // ListBox、Slider、CheckBox 与 ScrollBar 在白色页面上继续套用深色配色。
        Resources["TextPrimaryBrush"] = Ink;
        Resources["TextSecondaryBrush"] = Brush("#5E6B7A");
        Resources["SurfaceBrush"] = Paper;
        Resources["SurfaceAltBrush"] = Brush("#F2F6FC");
        Resources["BorderBrush"] = Brush("#B9C8DC");
        Resources["AccentBrush"] = Theme;
        Resources["AccentForegroundBrush"] = Brushes.White;
        Resources["AccentSoftBrush"] = Brush("#DCE8F8");
        Background = Paper;
        Foreground = Ink;

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
        Loaded += async (_, _) =>
        {
            controller.Changed -= Controller_OnChanged;
            controller.Changed += Controller_OnChanged;
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
        favoriteFilterButton = SecondaryButton("☆  收藏");
        var reset = SecondaryButton("重置");
        reload.Click += async (_, _) => await ReloadModelsAsync();
        random.Click += async (_, _) =>
        {
            var selected = await controller.SelectRandomModelAsync(FilterModels());
            if (selected is not null) RefreshModelList(selected.Key);
        };
        favoriteFilterButton.Click += (_, _) =>
        {
            favoriteOnly = !favoriteOnly;
            UpdateFavoriteFilterButton();
            RefreshModelList();
        };
        reset.Click += (_, _) =>
        {
            selectedTagFilters.Clear();
            if (searchBox is not null) searchBox.Text = "";
            if (typeFilter is not null) typeFilter.SelectedIndex = 0;
            ShowModels();
        };
        leftTools.Children.Add(reload);
        leftTools.Children.Add(random);
        leftTools.Children.Add(favoriteFilterButton);
        leftTools.Children.Add(reset);
        UpdateFavoriteFilterButton();
        DockPanel.SetDock(leftTools, Dock.Left);
        tools.Children.Add(leftTools);

        searchBox = new TextBox
        {
            MinWidth = 180,
            Height = 34,
            Foreground = Ink,
            Background = Paper,
            BorderBrush = Brush("#B9C8DC"),
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
        typeFilter = new ComboBox
        {
            Width = 150,
            Height = 34,
            Foreground = Ink,
            Background = Paper,
            BorderBrush = Brush("#B9C8DC"),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        typeFilter.SelectionChanged += (_, _) => RefreshModelList();
        DockPanel.SetDock(typeFilter, Dock.Right);
        filterRow.Children.Add(typeFilter);
        Grid.SetRow(filterRow, 0);
        left.Children.Add(filterRow);

        var tagFilter = BuildTagFilterPanel();
        Grid.SetRow(tagFilter, 1);
        left.Children.Add(tagFilter);

        modelList = new ListView
        {
            Margin = new Thickness(0, 0, 8, 0),
            Background = Paper,
            Foreground = Ink,
            BorderBrush = Brush("#B9C8DC"),
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
        Grid.SetRow(modelList, 2);
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

        var previewFrame = new Border
        {
            Height = 220,
            Margin = new Thickness(0, 0, 0, 10),
            Background = Brush("#F2F6FC"),
            BorderBrush = Brush("#C8D2E2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        var previewGrid = new Grid();
        modelPreview = new Image
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(10),
            SnapsToDevicePixels = true
        };
        modelPreviewPlaceholder = new TextBlock
        {
            Text = "选择模型后显示纹理图集\n完整角色由 ArkPets 运行时渲染",
            Foreground = Brushes.DimGray,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20)
        };
        previewGrid.Children.Add(modelPreview);
        previewGrid.Children.Add(modelPreviewPlaceholder);
        previewFrame.Child = previewGrid;
        infoStack.Children.Add(previewFrame);

        selectedFavoriteButton = SecondaryButton("☆  收藏");
        selectedFavoriteButton.HorizontalAlignment = HorizontalAlignment.Left;
        selectedFavoriteButton.Click += async (_, _) =>
        {
            if (modelList?.SelectedItem is not ArkPetModel model) return;
            await controller.ToggleFavoriteAsync(model.Key);
            RefreshModelDetails(model);
            RefreshModelList(model.Key);
        };
        infoStack.Children.Add(selectedFavoriteButton);

        var modelLinks = new WrapPanel { Margin = new Thickness(0, 5, 0, 2) };
        var wikiButton = SecondaryButton("PRTS Wiki");
        var helpButton = SecondaryButton("ArkPets 帮助");
        wikiButton.Click += (_, _) => OpenSelectedModelWiki();
        helpButton.Click += (_, _) => OpenExternal("https://arkpets.harryh.cn/help?from=client");
        modelLinks.Children.Add(wikiButton);
        modelLinks.Children.Add(helpButton);
        infoStack.Children.Add(modelLinks);

        infoStack.Children.Add(GroupTitle("模型库管理"));

        var modelRoot = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(controller.Settings.ModelRoot) ? "未选择 Ark-Models 模型库" : controller.Settings.ModelRoot,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(4, 8, 4, 8)
        };
        var manageButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var chooseLibrary = SecondaryButton("选择模型库");
        var updateLibrary = SecondaryButton("联网更新");
        var verifyLibrary = SecondaryButton("校验");
        var importLibrary = SecondaryButton("导入模型 / 模型库 ZIP");
        var exportLibrary = SecondaryButton("导出 ZIP");
        chooseLibrary.Click += async (_, _) => await ChooseModelRootAsync();
        updateLibrary.Click += async (_, _) => await InstallLatestModelsAsync();
        verifyLibrary.Click += async (_, _) => await VerifyModelLibraryAsync();
        importLibrary.Click += async (_, _) => await ImportModelLibraryAsync();
        exportLibrary.Click += async (_, _) => await ExportModelLibraryAsync();
        manageButtons.Children.Add(chooseLibrary);
        manageButtons.Children.Add(updateLibrary);
        manageButtons.Children.Add(verifyLibrary);
        manageButtons.Children.Add(importLibrary);
        manageButtons.Children.Add(exportLibrary);
        infoStack.Children.Add(modelRoot);
        infoStack.Children.Add(manageButtons);

        infoStack.Children.Add(GroupTitle("角色管理"));
        runningList = new ListBox
        {
            MinHeight = 58,
            MaxHeight = 100,
            Margin = new Thickness(4, 6, 4, 6),
            DisplayMemberPath = nameof(ArkPetProcessSnapshot.DisplayText)
        };
        var runningButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var stopSelected = SecondaryButton("停止所选");
        var stopAll = SecondaryButton("全部停止");
        stopSelected.Click += async (_, _) => await StopSelectedInstanceAsync();
        stopAll.Click += async (_, _) =>
        {
            await controller.StopAllAsync();
            RefreshRunningInstances();
        };
        runningButtons.Children.Add(stopSelected);
        runningButtons.Children.Add(stopAll);
        infoStack.Children.Add(runningList);
        infoStack.Children.Add(runningButtons);

        controlledList = new ListBox
        {
            MinHeight = 58,
            MaxHeight = 100,
            Margin = new Thickness(4, 8, 4, 6),
            DisplayMemberPath = nameof(ArkPetsIpcClientSnapshot.DisplayText)
        };
        infoStack.Children.Add(new TextBlock
        {
            Text = "实时控制",
            Foreground = Theme,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 8, 4, 2)
        });
        infoStack.Children.Add(controlledList);

        var controlButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var manualOn = SecondaryButton("手动模式");
        var manualOff = SecondaryButton("退出手动");
        var transparentOn = SecondaryButton("透明模式");
        var transparentOff = SecondaryButton("取消透明");
        var changeStage = SecondaryButton("切换形态");
        var remoteExit = SecondaryButton("退出角色");
        manualOn.Click += async (_, _) => await SendSelectedControlAsync(ArkPetsIpcOperation.KeepAction);
        manualOff.Click += async (_, _) => await SendSelectedControlAsync(ArkPetsIpcOperation.NoKeepAction);
        transparentOn.Click += async (_, _) => await SendSelectedControlAsync(ArkPetsIpcOperation.TransparentMode);
        transparentOff.Click += async (_, _) => await SendSelectedControlAsync(ArkPetsIpcOperation.NoTransparentMode);
        changeStage.Click += async (_, _) => await SendSelectedControlAsync(ArkPetsIpcOperation.ChangeStage);
        remoteExit.Click += async (_, _) => await SendSelectedControlAsync(ArkPetsIpcOperation.Logout);
        controlButtons.Children.Add(manualOn);
        controlButtons.Children.Add(manualOff);
        controlButtons.Children.Add(transparentOn);
        controlButtons.Children.Add(transparentOff);
        controlButtons.Children.Add(changeStage);
        controlButtons.Children.Add(remoteExit);
        infoStack.Children.Add(controlButtons);
        RefreshRunningInstances();

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
        stack.Children.Add(Toggle("允许行走", controller.Settings.BehaviorAllowWalk, value => s => s with { BehaviorAllowWalk = value }));
        stack.Children.Add(Toggle("允许坐下", controller.Settings.BehaviorAllowSit, value => s => s with { BehaviorAllowSit = value }));
        stack.Children.Add(Toggle("允许睡觉", controller.Settings.BehaviorAllowSleep, value => s => s with { BehaviorAllowSleep = value }));
        stack.Children.Add(Toggle("允许特殊基建动作", controller.Settings.BehaviorAllowSpecial, value => s => s with { BehaviorAllowSpecial = value }));
        stack.Children.Add(NumberSlider("活跃级别", controller.Settings.BehaviorAiActivation, 0, 16, " 级", value => s => s with { BehaviorAiActivation = (int)Math.Round(value) }));
        stack.Children.Add(NumberSlider("行走速度", controller.Settings.BehaviorWalkSpeed, 0, 200, " px/s", value => s => s with { BehaviorWalkSpeed = value }));
        stack.Children.Add(Toggle("允许鼠标交互", controller.Settings.BehaviorAllowInteract, value => s => s with { BehaviorAllowInteract = value }));
        stack.Children.Add(Toggle("桌宠之间相互避让", controller.Settings.BehaviorDoPeerRepulsion, value => s => s with { BehaviorDoPeerRepulsion = value }));
        stack.Children.Add(ChoiceRow(
            "方向切换",
            controller.Settings.BehaviorDirectionSwitching,
            new ChoiceOption<int>[]
            {
                new("禁用", 0),
                new("松开拖拽时", 1),
                new("拖拽时", 2),
                new("光标掠过时", 3)
            },
            value => s => s with { BehaviorDirectionSwitching = value }));

        stack.Children.Add(GroupTitle("位置设置"));
        stack.Children.Add(Toggle("允许跨多显示器移动", controller.Settings.DisplayMultiMonitors, value => s => s with { DisplayMultiMonitors = value }));
        stack.Children.Add(NumberSlider("下边界距离", controller.Settings.DisplayMarginBottom, 0, 120, " px", value => s => s with { DisplayMarginBottom = (int)Math.Round(value) }));
        stack.Children.Add(NumberSlider("初始位置 X", controller.Settings.InitialPositionX, 0, 1, "", value => s => s with { InitialPositionX = Math.Round(value, 3) }));
        stack.Children.Add(NumberSlider("初始位置 Y", controller.Settings.InitialPositionY, 0, 1, "", value => s => s with { InitialPositionY = Math.Round(value, 3) }));

        stack.Children.Add(GroupTitle("过渡设置"));
        stack.Children.Add(ChoiceRow(
            "动画间切换",
            controller.Settings.RenderAnimationMixture,
            new ChoiceOption<double>[]
            {
                new("禁用", 0),
                new("快速", 0.1),
                new("标准", 0.3),
                new("慢速", 0.6)
            },
            value => s => s with { RenderAnimationMixture = value }));
        stack.Children.Add(ChoiceRow(
            "位置与透明度过渡",
            controller.Settings.TransitionDuration,
            new ChoiceOption<double>[]
            {
                new("禁用", 0),
                new("快速", 0.1),
                new("标准", 0.3),
                new("慢速", 0.6)
            },
            value => s => s with { TransitionDuration = value }));
        stack.Children.Add(ChoiceRow(
            "缓动函数",
            controller.Settings.TransitionType,
            new ChoiceOption<string>[]
            {
                new("线性（Linear）", "LINEAR"),
                new("正弦缓出（EaseOutSine）", "EASE_OUT_SINE"),
                new("三次方缓出（EaseOutCubic）", "EASE_OUT_CUBIC"),
                new("五次方缓出（EaseOutQuint）", "EASE_OUT_QUINT")
            },
            value => s => s with { TransitionType = value }));

        stack.Children.Add(GroupTitle("物理设置"));
        stack.Children.Add(NumberSlider("重力加速度", controller.Settings.PhysicGravityAcc, 0, 2000, " px/s²", value => s => s with { PhysicGravityAcc = value }));
        stack.Children.Add(NumberSlider("空气阻力", controller.Settings.PhysicAirFrictionAcc, 0, 2000, " px/s²", value => s => s with { PhysicAirFrictionAcc = value }));
        stack.Children.Add(NumberSlider("静摩擦", controller.Settings.PhysicStaticFrictionAcc, 0, 2000, " px/s²", value => s => s with { PhysicStaticFrictionAcc = value }));
        stack.Children.Add(NumberSlider("水平速度上限", controller.Settings.PhysicSpeedLimitX, 0, 2000, " px/s", value => s => s with { PhysicSpeedLimitX = value }));
        stack.Children.Add(NumberSlider("垂直速度上限", controller.Settings.PhysicSpeedLimitY, 0, 2000, " px/s", value => s => s with { PhysicSpeedLimitY = value }));

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
            string.IsNullOrWhiteSpace(controller.Settings.RuntimePath) ? "未准备运行核心" : controller.Settings.RuntimePath,
            "选择",
            async () => await ChooseRuntimeAsync()));
        stack.Children.Add(PathRow(
            "模型库",
            string.IsNullOrWhiteSpace(controller.Settings.ModelRoot) ? "未准备 Ark-Models" : controller.Settings.ModelRoot,
            "选择",
            async () => await ChooseModelRootAsync()));

        var upstreamButtons = new WrapPanel { Margin = new Thickness(4, 4, 4, 8) };
        var installRuntime = SecondaryButton("安装 / 更新运行核心");
        var installModels = SecondaryButton("安装 / 更新模型库");
        installRuntime.Click += async (_, _) => await InstallLatestRuntimeAsync();
        installModels.Click += async (_, _) => await InstallLatestModelsAsync();
        upstreamButtons.Children.Add(installRuntime);
        upstreamButtons.Children.Add(installModels);
        stack.Children.Add(upstreamButtons);

        runtimeStatus = new TextBlock
        {
            Text = BuildRuntimeStatus(),
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 2, 8, 8)
        };
        stack.Children.Add(runtimeStatus);

        stack.Children.Add(GroupTitle("显示设置"));
        stack.Children.Add(ChoiceRow(
            "显示缩放",
            controller.Settings.DisplayScale,
            new ChoiceOption<double>[]
            {
                new("x0.5", 0.5), new("x0.75", 0.75), new("x1.0", 1),
                new("x1.25", 1.25), new("x1.5", 1.5), new("x2.0", 2),
                new("x2.5", 2.5), new("x3.0", 3)
            },
            value => s => s with { DisplayScale = value }));
        stack.Children.Add(ChoiceRow(
            "最大帧率",
            controller.Settings.DisplayFps,
            new ChoiceOption<int>[]
            {
                new("25", 25), new("30", 30), new("45", 45), new("60", 60), new("120", 120)
            },
            value => s => s with { DisplayFps = value }));

        stack.Children.Add(GroupTitle("渲染设置"));
        stack.Children.Add(ChoiceRow(
            "画布颜色",
            controller.Settings.CanvasColor,
            new ChoiceOption<string>[]
            {
                new("透明", "#00000000"),
                new("绿色", "#00FF00FF"),
                new("蓝色", "#0000FFFF"),
                new("品红色", "#FF00FFFF")
            },
            value => s => s with { CanvasColor = value }));
        stack.Children.Add(ChoiceRow(
            "画布覆盖率",
            controller.Settings.CanvasCoverage,
            new ChoiceOption<double>[]
            {
                new("最宽", 0.45), new("较宽", 0.65), new("标准", 0.8), new("较窄", 0.9), new("最窄", 0.95)
            },
            value => s => s with { CanvasCoverage = value }));
        stack.Children.Add(ChoiceRow(
            "画布采样精度",
            controller.Settings.CanvasSamplingInterval,
            new ChoiceOption<int>[]
            {
                new("极精确", 1), new("精确", 4), new("粗略", 16), new("极粗略", 64)
            },
            value => s => s with { CanvasSamplingInterval = value }));
        stack.Children.Add(ChoiceRow(
            "描边显示",
            controller.Settings.RenderOutline,
            OutlineChoices(),
            value => s => s with { RenderOutline = value }));
        stack.Children.Add(ChoiceRow(
            "强调描边",
            controller.Settings.RenderOutlineEmphasis,
            OutlineChoices(),
            value => s => s with { RenderOutlineEmphasis = value }));
        stack.Children.Add(ChoiceRow(
            "描边颜色",
            controller.Settings.RenderOutlineColor,
            OutlineColorChoices(),
            value => s => s with { RenderOutlineColor = value }));
        stack.Children.Add(ChoiceRow(
            "强调描边颜色",
            controller.Settings.RenderOutlineEmphasisColor,
            OutlineColorChoices(),
            value => s => s with { RenderOutlineEmphasisColor = value }));
        stack.Children.Add(ChoiceRow(
            "描边宽度",
            controller.Settings.RenderOutlineWidth,
            new ChoiceOption<double>[]
            {
                new("极细", 1), new("较细", 1.5), new("标准", 2), new("较粗", 3), new("极粗", 5)
            },
            value => s => s with { RenderOutlineWidth = value }));
        stack.Children.Add(NumberSlider("正常透明度", controller.Settings.OpacityNormal, 0.1, 1, "", value => s => s with { OpacityNormal = Math.Round(value, 2) }));
        stack.Children.Add(NumberSlider("淡化透明度", controller.Settings.OpacityDim, 0.1, 1, "", value => s => s with { OpacityDim = Math.Min(Math.Round(value, 2), s.OpacityNormal) }));
        stack.Children.Add(ChoiceRow(
            "阴影",
            controller.Settings.RenderShadowColor,
            new ChoiceOption<string>[]
            {
                new("禁用", "#00000000"),
                new("轻微", "#00000077"),
                new("标准", "#000000BB"),
                new("重墨", "#000000FF")
            },
            value => s => s with { RenderShadowColor = value }));
        stack.Children.Add(Toggle("高质量着色器", controller.Settings.RenderShaderHighQuality, value => s => s with { RenderShaderHighQuality = value }));
        stack.Children.Add(Toggle("启用 Mipmap", controller.Settings.RenderEnableMipmap, value => s => s with { RenderEnableMipmap = value }));

        stack.Children.Add(GroupTitle("高级设置"));
        stack.Children.Add(ChoiceRow(
            "日志级别",
            controller.Settings.LoggingLevel,
            new ChoiceOption<string>[]
            {
                new("DEBUG", "DEBUG"), new("INFO", "INFO"), new("WARN", "WARN"), new("ERROR", "ERROR")
            },
            value => s => s with { LoggingLevel = value }));
        stack.Children.Add(Toggle("随 ExusiAI 启动所选桌宠", controller.Settings.AutoStartPetWithExusiAI, value => s => s with { AutoStartPetWithExusiAI = value }));
        stack.Children.Add(WindowsStartupToggle());
        stack.Children.Add(Toggle("桌宠窗口置顶", controller.Settings.WindowStyleTopmost, value => s => s with { WindowStyleTopmost = value }));
        stack.Children.Add(Toggle("桌宠作为后台工具窗口", controller.Settings.WindowStyleToolwindow, value => s => s with { WindowStyleToolwindow = value }));
        stack.Children.Add(Toggle("长时间未交互时降低帧率", controller.Settings.EcoMode, value => s => s with { EcoMode = value }));
        stack.Children.Add(new TextBlock
        {
            Text = "插件模式固定绑定 ExusiAI：Windows 自启动项只启动 ExusiAI；ExusiAI 退出、插件禁用或卸载时，其启动的 ArkPets 子进程会一并结束。",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 6, 8, 6)
        });

        var privacyNote = new TextBlock
        {
            Text = "兼容运行时遥测由 ExusiAI 配置固定关闭；其余 ArkPets 配置字段保持上游语义。",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 6, 8, 6)
        };
        stack.Children.Add(privacyNote);

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
        stack.Children.Add(new TextBlock
        {
            Text = "Ark-Pets © 2022-2026 Harry Huang · GPL-3.0\nArk-Models 模型资源版权归上海鹰角网络有限公司所有；本插件不将模型素材重新许可为 GPL。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(4, 8, 4, 16)
        });

        contentHost.Children.Add(new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
    }

    private void OpenSelectedModelWiki()
    {
        if (modelList?.SelectedItem is not ArkPetModel model)
        {
            SetStatus("请先选择一个模型。");
            return;
        }

        OpenExternal("https://prts.wiki/w/" + Uri.EscapeDataString(model.Name));
    }

    private void OpenExternal(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus($"无法打开链接：{exception.Message}");
        }
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

    private Task VerifyModelLibraryAsync()
    {
        var result = controller.VerifyModelLibrary();
        if (result.TotalModels == 0)
        {
            SetStatus("模型库中没有可校验的模型。");
            return Task.CompletedTask;
        }

        SetStatus(result.IsHealthy
            ? $"模型库校验完成：{result.AvailableModels} / {result.TotalModels} 个模型资源完整。"
            : $"模型库校验完成：缺少 {result.MissingModels} 个模型资源；示例：{string.Join("、", result.MissingModelKeys.Take(5))}");
        return Task.CompletedTask;
    }

    private async Task ImportModelLibraryAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入单模型或 Ark-Models 模型库 ZIP",
            Filter = "ZIP 压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            SetStatus("正在导入模型库…");
            var result = await controller.ImportModelLibraryAsync(dialog.FileName);
            ShowModels();
            SetStatus(result);
        }
        catch (Exception exception)
        {
            SetStatus($"模型库导入失败：{exception.Message}");
        }
    }

    private async Task ExportModelLibraryAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出 Ark-Models ZIP",
            Filter = "ZIP 压缩包 (*.zip)|*.zip",
            FileName = "ArkModels.zip",
            DefaultExt = ".zip",
            AddExtension = true
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            SetStatus("正在导出模型库…");
            await controller.ExportModelLibraryAsync(dialog.FileName);
            SetStatus($"模型库已导出到 {dialog.FileName}");
        }
        catch (Exception exception)
        {
            SetStatus($"模型库导出失败：{exception.Message}");
        }
    }

    private string BuildRuntimeStatus()
    {
        var runtime = string.IsNullOrWhiteSpace(controller.Settings.RuntimePath)
            ? "运行核心：未准备"
            : $"运行核心：{(string.IsNullOrWhiteSpace(controller.Settings.RuntimeVersion) ? "已就绪" : "v" + controller.Settings.RuntimeVersion)}";
        var models = controller.Catalog.Models.Count == 0
            ? "模型库：未加载"
            : $"模型库：{controller.Catalog.Models.Count} 个模型";
        var ipc = controller.ControlPort is int port
            ? $"控制服务：localhost:{port}"
            : "控制服务：未连接";
        return $"{runtime}\n{models}\n{ipc}";
    }

    private async Task InstallLatestRuntimeAsync()
    {
        try
        {
            SetRuntimeStatus("正在检查并下载 ArkPets 官方便携运行核心…");
            var progress = new Progress<ArkPetsDownloadProgress>(item =>
            {
                if (item.Ratio is double ratio)
                    SetRuntimeStatus($"正在下载运行核心… {ratio:P0}");
            });
            var version = await controller.InstallLatestRuntimeAsync(progress);
            SetRuntimeStatus($"ArkPets 运行核心 v{version} 已由 ExusiAI 管理。");
            ShowOptions();
        }
        catch (Exception exception)
        {
            SetRuntimeStatus($"运行核心安装失败：{exception.Message}");
        }
    }

    private async Task InstallLatestModelsAsync()
    {
        try
        {
            SetRuntimeStatus("正在下载 Ark-Models 官方模型库…");
            var progress = new Progress<ArkPetsDownloadProgress>(item =>
            {
                if (item.Ratio is double ratio)
                    SetRuntimeStatus($"正在下载模型库… {ratio:P0}");
            });
            var count = await controller.InstallLatestModelsAsync(progress);
            SetRuntimeStatus($"Ark-Models 已更新，载入 {count} 个模型。");
            ShowModels();
        }
        catch (Exception exception)
        {
            SetRuntimeStatus($"模型库安装失败：{exception.Message}");
        }
    }

    private async Task SendSelectedControlAsync(ArkPetsIpcOperation operation)
    {
        if (controlledList?.SelectedItem is not ArkPetsIpcClientSnapshot selected)
        {
            SetStatus("请先选择一个已连接到 ExusiAI 的桌宠。");
            return;
        }

        if (operation == ArkPetsIpcOperation.ChangeStage && !selected.CanChangeStage)
        {
            SetStatus("这个模型没有可切换的形态。");
            return;
        }

        var sent = await controller.SendControlAsync(selected.RemoteId, operation);
        SetStatus(sent ? "控制命令已发送。" : "桌宠控制连接已经断开。");
        RefreshRunningInstances();
    }

    private async Task StopSelectedInstanceAsync()
    {
        if (runningList?.SelectedItem is not ArkPetProcessSnapshot selected)
        {
            SetStatus("请先选择要停止的桌宠实例。");
            return;
        }

        var stopped = await controller.StopInstanceAsync(selected.Id);
        SetStatus(stopped ? $"已停止 {selected.ModelName}。" : "未找到对应的桌宠实例。");
        RefreshRunningInstances();
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
        if (favoriteOnly)
            items = items.Where(model => controller.IsFavorite(model.Key));
        if (selectedTagFilters.Count > 0)
            items = items.Where(model => selectedTagFilters.All(tag =>
                model.SortTags.Contains(tag, StringComparer.OrdinalIgnoreCase)));
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
                : $"已载入 {controller.Catalog.Models.Count} 个模型 · 当前筛选 {items.Length} 个 · 可用 {items.Count(x => x.IsAvailable)} 个" +
                  (selectedTagFilters.Count > 0 ? $" · 标签 {selectedTagFilters.Count}" : "");
    }

    private void RefreshModelDetails(ArkPetModel? model)
    {
        if (modelName is null || modelDetails is null) return;
        if (model is null)
        {
            modelName.Text = "请选择模型";
            modelDetails.Text = "";
            SetModelPreview(null);
            if (selectedFavoriteButton is not null)
            {
                selectedFavoriteButton.Content = "☆  收藏";
                selectedFavoriteButton.IsEnabled = false;
            }
            return;
        }

        modelName.Text = model.DisplayName;
        if (selectedFavoriteButton is not null)
        {
            var favorite = controller.IsFavorite(model.Key);
            selectedFavoriteButton.Content = favorite ? "★  已收藏" : "☆  收藏";
            selectedFavoriteButton.IsEnabled = true;
        }
        var tags = model.SortTags.Count == 0 ? "—" : string.Join(" / ", model.SortTags);
        modelDetails.Text =
            $"{model.Subtitle}\n\n资源键：{model.Key}\n时装系列：{(string.IsNullOrWhiteSpace(model.SkinGroupName) ? "—" : model.SkinGroupName)}\n标签：{tags}\n状态：{(model.IsAvailable ? "资源完整" : "缺少资源文件")}";
        SetModelPreview(model);
    }

    private void SetModelPreview(ArkPetModel? model)
    {
        if (modelPreview is null || modelPreviewPlaceholder is null) return;
        modelPreview.Source = null;
        var path = model?.PreviewImagePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            modelPreviewPlaceholder.Text = model is null
                ? "选择模型后显示纹理图集\n完整角色由 ArkPets 运行时渲染"
                : "该模型尚未下载完整贴图";
            modelPreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            // 仅为当前选中项生成低分辨率预览，并立即释放源文件句柄。
            // 这样加载数千模型的完整库时不会预解码所有大贴图或长期锁定文件。
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelHeight = 260;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            modelPreview.Source = bitmap;
            modelPreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException)
        {
            modelPreviewPlaceholder.Text = "贴图预览不可用，但不影响启动模型";
            modelPreviewPlaceholder.Visibility = Visibility.Visible;
        }
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

    private FrameworkElement BuildTagFilterPanel()
    {
        var tags = controller.Catalog.Models
            .SelectMany(model => model.SortTags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => controller.Catalog.SortTags.TryGetValue(tag, out var label) ? label : tag, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (tags.Length == 0)
            return new Border { Height = 1, Margin = new Thickness(0, 0, 8, 4) };

        var flow = new WrapPanel { Margin = new Thickness(0, 0, 8, 6) };
        foreach (var tag in tags)
        {
            var label = controller.Catalog.SortTags.TryGetValue(tag, out var translated) && !string.IsNullOrWhiteSpace(translated)
                ? translated
                : tag;
            var button = SecondaryButton(label);
            button.Padding = new Thickness(8, 3, 8, 3);
            button.Margin = new Thickness(2);
            ApplyTagFilterStyle(button, selectedTagFilters.Contains(tag));
            button.Click += (_, _) =>
            {
                if (!selectedTagFilters.Add(tag))
                    selectedTagFilters.Remove(tag);
                ApplyTagFilterStyle(button, selectedTagFilters.Contains(tag));
                RefreshModelList();
            };
            flow.Children.Add(button);
        }

        return new ScrollViewer
        {
            Content = flow,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 78,
            Margin = new Thickness(0, 0, 0, 2)
        };
    }

    private static void ApplyTagFilterStyle(Button button, bool active)
    {
        button.Background = active ? Theme : Paper;
        button.Foreground = active ? Brushes.White : Theme;
    }

    private void UpdateFavoriteFilterButton()
    {
        if (favoriteFilterButton is null) return;
        favoriteFilterButton.Content = favoriteOnly ? "★  收藏" : "☆  收藏";
        favoriteFilterButton.Background = favoriteOnly ? Theme : Paper;
        favoriteFilterButton.Foreground = favoriteOnly ? Brushes.White : Theme;
    }

    private void RefreshRunningInstances()
    {
        if (runningList is not null)
        {
            var selectedId = runningList.SelectedItem is ArkPetProcessSnapshot selected ? selected.Id : (Guid?)null;
            var items = controller.RunningInstances;
            runningList.ItemsSource = items;
            if (selectedId is not null)
                runningList.SelectedItem = items.FirstOrDefault(item => item.Id == selectedId.Value);
        }

        if (controlledList is not null)
        {
            var remoteId = controlledList.SelectedItem is ArkPetsIpcClientSnapshot selectedClient
                ? selectedClient.RemoteId
                : (Guid?)null;
            var clients = controller.ControlledInstances;
            controlledList.ItemsSource = clients;
            if (remoteId is not null)
                controlledList.SelectedItem = clients.FirstOrDefault(item => item.RemoteId == remoteId.Value);
        }

        if (runtimeStatus is not null)
            runtimeStatus.Text = BuildRuntimeStatus();
    }

    private static IReadOnlyList<ChoiceOption<int>> OutlineChoices() =>
    [
        new("始终开启", 5),
        new("处于前台时", 3),
        new("点击时", 2),
        new("拖拽时", 1),
        new("关闭", 0)
    ];

    private static IReadOnlyList<ChoiceOption<string>> OutlineColorChoices() =>
    [
        new("黄色", "#FFFF00FF"),
        new("橙色", "#FFBB00FF"),
        new("白色", "#FFFFFFFF"),
        new("青色", "#00FFFFFF")
    ];

    private FrameworkElement ChoiceRow<T>(
        string label,
        T initial,
        IReadOnlyList<ChoiceOption<T>> options,
        Func<T, Func<ArkPetsSettings, ArkPetsSettings>> update)
        where T : notnull
    {
        var values = options.ToList();
        var selected = values.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, initial));
        if (selected is null)
        {
            selected = new ChoiceOption<T>($"自定义（{initial}）", initial);
            values.Insert(0, selected);
        }

        var row = new Grid { Margin = new Thickness(4, 6, 4, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var text = new TextBlock
        {
            Text = label,
            Foreground = Ink,
            VerticalAlignment = VerticalAlignment.Center
        };
        var combo = new ComboBox
        {
            ItemsSource = values,
            DisplayMemberPath = nameof(ChoiceOption<T>.Label),
            SelectedItem = selected,
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        combo.SelectionChanged += async (_, _) =>
        {
            if (combo.SelectedItem is ChoiceOption<T> choice)
                await controller.UpdateSettingsAsync(update(choice.Value));
        };

        Grid.SetColumn(text, 0);
        Grid.SetColumn(combo, 1);
        row.Children.Add(text);
        row.Children.Add(combo);
        return row;
    }

    private FrameworkElement WindowsStartupToggle()
    {
        var check = new CheckBox
        {
            Content = "Windows 登录时启动 ExusiAI",
            IsChecked = controller.WindowsStartupEnabled,
            Margin = new Thickness(8, 7, 8, 7),
            Foreground = Ink
        };
        var suppress = false;
        check.Checked += async (_, _) =>
        {
            if (suppress) return;
            if (await controller.SetWindowsStartupEnabledAsync(true)) return;
            suppress = true;
            check.IsChecked = false;
            suppress = false;
            SetRuntimeStatus("无法写入当前用户的 ExusiAI 开机启动项。");
        };
        check.Unchecked += async (_, _) =>
        {
            if (suppress) return;
            if (await controller.SetWindowsStartupEnabledAsync(false)) return;
            suppress = true;
            check.IsChecked = true;
            suppress = false;
            SetRuntimeStatus("无法移除当前用户的 ExusiAI 开机启动项。");
        };
        return check;
    }

    private void SetRuntimeStatus(string message)
    {
        if (runtimeStatus is not null)
            runtimeStatus.Text = message;
        else
            SetStatus(message);
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
        {
            RefreshModelList(controller.Settings.SelectedModelKey);
            RefreshRunningInstances();
        }
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
            Margin = new Thickness(3),
            MinHeight = 32,
            FontSize = 13
        };

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
