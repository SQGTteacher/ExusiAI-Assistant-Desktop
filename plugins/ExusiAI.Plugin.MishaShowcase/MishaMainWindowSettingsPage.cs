using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ExusiAI.Plugin.MishaShowcase;

/// <summary>
/// ClassIsland-native editor for the main information-island appearance and docking fields.
/// This page intentionally avoids a generic "JSON type -> control" template so the controls
/// carry the same concrete semantics as the upstream Misha settings.
/// </summary>
internal sealed class MishaMainWindowSettingsPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly TextBlock status = MishaUi.Note("");

    private readonly CheckBox isVisible = new() { Content = "显示顶部信息岛" };
    private readonly CheckBox isSeparated = new() { Content = "每个组件使用独立信息岛背景" };
    private readonly CheckBox customBackground = new() { Content = "启用自定义背景色" };
    private readonly CheckBox customForeground = new() { Content = "启用自定义前景色" };
    private readonly CheckBox ignoreWorkArea = new() { Content = "使用原始屏幕尺寸（忽略任务栏工作区）" };

    private readonly TextBox backgroundColor = Input(160);
    private readonly TextBox foregroundColor = Input(160);
    private readonly TextBox opacity = Input(110);
    private readonly TextBox radius = Input(110);
    private readonly TextBox scale = Input(110);
    private readonly TextBox lineMargin = Input(110);
    private readonly TextBox fontFamily = Input(280);
    private readonly TextBox bodyFontSize = Input(110);
    private readonly TextBox monitorIndex = Input(110);
    private readonly TextBox offsetX = Input(110);
    private readonly TextBox offsetY = Input(110);

    private readonly RadioButton[] themeOptions =
    [
        Choice("跟随系统", "MishaTheme", 0),
        Choice("明亮", "MishaTheme", 1),
        Choice("黑暗", "MishaTheme", 2)
    ];

    private readonly RadioButton[] dockingOptions =
    [
        Choice("左上角", "MishaDocking", 0),
        Choice("中上侧", "MishaDocking", 1),
        Choice("右上角", "MishaDocking", 2),
        Choice("左下角", "MishaDocking", 3),
        Choice("中下侧", "MishaDocking", 4),
        Choice("右下角", "MishaDocking", 5)
    ];

    private readonly RadioButton[] layerOptions =
    [
        Choice("置底", "MishaLayer", 0),
        Choice("置顶", "MishaLayer", 1)
    ];

    public MishaMainWindowSettingsPage(MishaPlatformStore store)
    {
        this.store = store;

        var root = MishaUi.Page(
            "信息岛外观与位置",
            "直接编辑 ClassIsland 2.2 Misha Settings.json 中的原生字段。保存后会立即要求顶部信息岛重新渲染和重新停靠，不创建 ExusiAI 专用主题格式。");

        root.Children.Add(MishaUi.Section("显示"));
        root.Children.Add(MishaUi.SettingRow("主界面", "对应 IsMainWindowVisible。", isVisible));
        root.Children.Add(MishaUi.SettingRow("信息岛布局", "对应 IsIslandSeperated。", isSeparated));

        root.Children.Add(MishaUi.Section("主题"));
        root.Children.Add(MishaUi.SettingRow(
            "应用主题",
            "与 ClassIsland Theme 一致：0 跟随系统，1 明亮，2 黑暗。",
            ChoiceRow(themeOptions, 3)));
        root.Children.Add(MishaUi.SettingRow("背景不透明度", "Opacity，范围 0–1。", opacity));
        root.Children.Add(MishaUi.SettingRow("圆角半径", "RadiusX / RadiusY。", radius));
        root.Children.Add(MishaUi.SettingRow("整体缩放", "Scale，建议 0.5–2。", scale));
        root.Children.Add(MishaUi.SettingRow("行间距", "MainWindowLineVerticalMargin。", lineMargin));

        var backgroundPanel = new StackPanel();
        backgroundPanel.Children.Add(customBackground);
        backgroundColor.Margin = new Thickness(0, 8, 0, 0);
        backgroundPanel.Children.Add(backgroundColor);
        root.Children.Add(MishaUi.SettingRow(
            "背景颜色",
            "保持 ClassIsland Color 的原有 JSON 形态；可输入 #RRGGBB 或 ClassIsland 使用的 #RRGGBBAA。",
            backgroundPanel));

        var foregroundPanel = new StackPanel();
        foregroundPanel.Children.Add(customForeground);
        foregroundColor.Margin = new Thickness(0, 8, 0, 0);
        foregroundPanel.Children.Add(foregroundColor);
        root.Children.Add(MishaUi.SettingRow(
            "文字颜色",
            "关闭自定义前景色时，明亮/黑暗主题会自动选择可读文字色。",
            foregroundPanel));

        root.Children.Add(MishaUi.Section("字体"));
        root.Children.Add(MishaUi.SettingRow("字体", "MainWindowFont。留空时沿用系统可用字体。", fontFamily));
        root.Children.Add(MishaUi.SettingRow("正文字号", "MainWindowBodyFontSize。", bodyFontSize));

        root.Children.Add(MishaUi.Section("位置"));
        root.Children.Add(MishaUi.SettingRow(
            "停靠位置",
            "与 ClassIsland WindowDockingLocation 的 0–5 六个位置完全一致。",
            ChoiceRow(dockingOptions, 3)));
        root.Children.Add(MishaUi.SettingRow("显示器索引", "WindowDockingMonitorIndex，从 0 开始。", monitorIndex));

        var offsetRow = new StackPanel { Orientation = Orientation.Horizontal };
        offsetRow.Children.Add(LabeledInput("X", offsetX));
        offsetRow.Children.Add(LabeledInput("Y", offsetY));
        root.Children.Add(MishaUi.SettingRow("停靠偏移", "WindowDockingOffsetX / WindowDockingOffsetY，单位为设备无关像素。", offsetRow));
        root.Children.Add(MishaUi.SettingRow("屏幕工作区", "", ignoreWorkArea));
        root.Children.Add(MishaUi.SettingRow(
            "窗口层级",
            "与 ClassIsland WindowLayer 一致：0 置底，1 置顶。",
            ChoiceRow(layerOptions, 2)));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 8) };
        var save = MishaUi.Button("应用并保存");
        save.Click += async (_, _) => await SaveAsync();
        var reload = MishaUi.Button("从 Settings.json 重新载入", true);
        reload.Click += (_, _) => Reload();
        actions.Children.Add(save);
        actions.Children.Add(reload);
        root.Children.Add(actions);
        root.Children.Add(status);

        Loaded += (_, _) => Reload();
        store.Changed += (_, _) => Dispatcher.Invoke(Reload);
        Content = MishaUi.Scroll(root);
    }

    private void Reload()
    {
        var workspace = store.Workspace;
        if (workspace is null)
        {
            status.Text = "尚未连接 ClassIsland Settings.json。请先在“工作区”导入真实数据。";
            return;
        }

        isVisible.IsChecked = workspace.GetBool("IsMainWindowVisible", true);
        isSeparated.IsChecked = workspace.GetBool("IsIslandSeperated");
        customBackground.IsChecked = workspace.GetBool("IsCustomBackgroundColorEnabled");
        customForeground.IsChecked = workspace.GetBool("IsCustomForegroundColorEnabled");
        ignoreWorkArea.IsChecked = workspace.GetBool("IsIgnoreWorkAreaEnabled");

        Select(themeOptions, Math.Clamp(workspace.GetInt("Theme", 2), 0, 2));
        Select(dockingOptions, Math.Clamp(workspace.GetInt("WindowDockingLocation", 1), 0, 5));
        Select(layerOptions, Math.Clamp(workspace.GetInt("WindowLayer", 1), 0, 1));

        backgroundColor.Text = ReadColor(workspace.Settings["BackgroundColor"], "#000000FF");
        foregroundColor.Text = ReadColor(workspace.Settings["CustomForegroundColor"], "#FFFFFFFF");
        opacity.Text = workspace.GetDouble("Opacity", 0.5).ToString("0.###", CultureInfo.InvariantCulture);
        radius.Text = workspace.GetDouble("RadiusX", 8).ToString("0.###", CultureInfo.InvariantCulture);
        scale.Text = workspace.GetDouble("Scale", 1).ToString("0.###", CultureInfo.InvariantCulture);
        lineMargin.Text = workspace.GetDouble("MainWindowLineVerticalMargin", 5).ToString("0.###", CultureInfo.InvariantCulture);
        fontFamily.Text = workspace.GetString("MainWindowFont");
        bodyFontSize.Text = workspace.GetDouble("MainWindowBodyFontSize", 16).ToString("0.###", CultureInfo.InvariantCulture);
        monitorIndex.Text = workspace.GetInt("WindowDockingMonitorIndex").ToString(CultureInfo.InvariantCulture);
        offsetX.Text = workspace.GetInt("WindowDockingOffsetX").ToString(CultureInfo.InvariantCulture);
        offsetY.Text = workspace.GetInt("WindowDockingOffsetY").ToString(CultureInfo.InvariantCulture);
        status.Text = $"当前配置：{workspace.SettingsPath}";
    }

    private async Task SaveAsync()
    {
        var workspace = store.Workspace;
        if (workspace is null)
        {
            status.Text = "尚未连接 ClassIsland Settings.json。";
            return;
        }

        try
        {
            workspace.Set("IsMainWindowVisible", isVisible.IsChecked == true);
            workspace.Set("IsIslandSeperated", isSeparated.IsChecked == true);
            workspace.Set("Theme", Selected(themeOptions, 2));
            workspace.Set("Opacity", ParseDouble(opacity.Text, 0, 1, "背景不透明度"));
            var radiusValue = ParseDouble(radius.Text, 0, 80, "圆角半径");
            workspace.Set("RadiusX", radiusValue);
            workspace.Set("RadiusY", radiusValue);
            workspace.Set("Scale", ParseDouble(scale.Text, 0.25, 4, "整体缩放"));
            workspace.Set("MainWindowLineVerticalMargin", ParseDouble(lineMargin.Text, 0, 80, "行间距"));
            workspace.Set("MainWindowFont", fontFamily.Text.Trim());
            workspace.Set("MainWindowBodyFontSize", ParseDouble(bodyFontSize.Text, 8, 72, "正文字号"));

            workspace.Set("IsCustomBackgroundColorEnabled", customBackground.IsChecked == true);
            workspace.Set("IsCustomForegroundColorEnabled", customForeground.IsChecked == true);
            WriteColor(workspace, "BackgroundColor", backgroundColor.Text);
            WriteColor(workspace, "CustomForegroundColor", foregroundColor.Text);

            workspace.Set("WindowDockingLocation", Selected(dockingOptions, 1));
            workspace.Set("WindowDockingMonitorIndex", ParseInt(monitorIndex.Text, 0, 63, "显示器索引"));
            workspace.Set("WindowDockingOffsetX", ParseInt(offsetX.Text, -10000, 10000, "X 偏移"));
            workspace.Set("WindowDockingOffsetY", ParseInt(offsetY.Text, -10000, 10000, "Y 偏移"));
            workspace.Set("IsIgnoreWorkAreaEnabled", ignoreWorkArea.IsChecked == true);
            workspace.Set("WindowLayer", Selected(layerOptions, 1));

            await store.SaveWorkspaceSettingsAsync();
            status.Text = "已保存 ClassIsland 原生设置；顶部信息岛已请求立即刷新主题与位置。";
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            status.Text = $"未保存：{exception.Message}";
        }
    }

    private static TextBox Input(double width) => new() { Width = width, HorizontalAlignment = HorizontalAlignment.Left };

    private static RadioButton Choice(string text, string group, int value) =>
        new()
        {
            Content = text,
            GroupName = group,
            Tag = value,
            Margin = new Thickness(0, 2, 18, 6),
            MinWidth = 78
        };

    private static FrameworkElement ChoiceRow(IEnumerable<RadioButton> options, int columns)
    {
        var panel = new UniformGrid { Columns = columns };
        foreach (var option in options)
            panel.Children.Add(option);
        return panel;
    }

    private static FrameworkElement LabeledInput(string label, TextBox box)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 20, 0) };
        panel.Children.Add(new TextBlock { Text = label, Width = 22, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(box);
        return panel;
    }

    private static void Select(IEnumerable<RadioButton> options, int value)
    {
        foreach (var option in options)
            option.IsChecked = option.Tag is int optionValue && optionValue == value;
    }

    private static int Selected(IEnumerable<RadioButton> options, int fallback) =>
        options.FirstOrDefault(x => x.IsChecked == true)?.Tag is int value ? value : fallback;

    private static double ParseDouble(string text, double min, double max, string name)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new FormatException($"{name}必须是有效数字。");
        if (value < min || value > max)
            throw new FormatException($"{name}必须位于 {min.ToString(CultureInfo.InvariantCulture)}–{max.ToString(CultureInfo.InvariantCulture)}。");
        return value;
    }

    private static int ParseInt(string text, int min, int max, string name)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"{name}必须是整数。");
        if (value < min || value > max)
            throw new FormatException($"{name}必须位于 {min}–{max}。");
        return value;
    }

    private static string ReadColor(JsonNode? node, string fallback)
    {
        if (node is JsonValue value &&
            value.TryGetValue<string>(out var text) &&
            !string.IsNullOrWhiteSpace(text))
            return text;

        var fallbackColor = ClassIslandColorCodec.TryParse(fallback, out var parsedFallback)
            ? parsedFallback
            : Colors.Black;
        return ClassIslandColorCodec.Format(ClassIslandColorCodec.Parse(node, fallbackColor));
    }

    private static void WriteColor(ClassIslandWorkspace workspace, string key, string text)
    {
        if (!ClassIslandColorCodec.TryParse(text, out var color))
            throw new FormatException($"{key} 不是有效的 ClassIsland 颜色。请使用 #RRGGBB 或 #RRGGBBAA。");

        if (workspace.Settings[key] is JsonObject existing)
        {
            existing["A"] = color.A;
            existing["R"] = color.R;
            existing["G"] = color.G;
            existing["B"] = color.B;
        }
        else
        {
            workspace.Settings[key] = ClassIslandColorCodec.Format(color);
        }
    }
}
