using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed record MishaSettingsCategory(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> Keys);

internal static class MishaSettingsCatalog
{
    public static IReadOnlyList<MishaSettingsCategory> Categories { get; } =
    [
        new("general", "基本", "对应 ClassIsland 2.2 Misha 的基本设置。只修改真实 Settings.json 中已经存在的字段，不补写臆造默认值。",
        [
            "AnimationLevel", "CriticalSafeModeMethod", "HideMode", "HideOnClass", "HideOnFullscreen", "HideOnMaxWindow",
            "HideRules", "IsCriticalSafeMode", "IsSplashEnabled", "IsWaitForTransientDisabled", "MultiWeekRotationMaxCycle",
            "ReduceProgressAccuracy", "ShowDetailedStatusOnSplash", "ShowSellingAnnouncement", "SingleWeekStartTime",
            "SplashCustomLogoSource", "SplashCustomText", "TaskBarIconClickBehavior"
        ]),
        new("clock", "时钟", "时间源、精确时间与自动校时设置。",
        ["ExactTimeServer", "IsExactTimeEnabled", "IsTimeAutoAdjustEnabled", "TimeAutoAdjustSeconds", "TimeOffsetSeconds"]),
        new("storage", "存储", "ClassIsland 自动备份和存储状态设置。",
        ["AutoBackupIntervalDays", "AutoBackupLimit", "BackupFilesSize", "IsAutoBackupEnabled", "LastAutoBackupTime"]),
        new("privacy", "隐私", "隐私和诊断设置。ClassIsland 的进程级 Sentry 开关不一定存放在 Settings.json 中，因此不会伪造该字段。",
        ["IsReportingEnabled", "TrustedProfileIds"]),
        new("refreshing", "翻新与迎新", "翻新提示、迎新提示及其显示范围。",
        [
            "IsRefreshingToastEnabled", "LeftRefreshingToastCounts", "MaxRefreshingToastCounts", "OnboardingToastBody",
            "OnboardingToastTitle", "RefreshingScopes", "RefreshingToastIsOnboardingGuide", "RefreshingToastThresholdDays",
            "ShowRefreshingToastOnNextStart"
        ]),
        new("advanced", "高级", "高级兼容、安全模式和渲染相关设置。进程级全局存储项保持由 ClassIsland 本体管理。",
        ["AutoDisableCorruptPlugins", "IsDebugEnabled", "IsDebugOptionsEnabled", "IsCriticalSafeMode", "CriticalSafeModeMethod"]),
        new("components-settings", "组件", "主信息岛组件配置选择。组件布局本体请在“组件配置”页编辑。",
        ["CurrentComponentConfig", "ShowComponentsMigrateTip"]),
        new("appearance", "外观", "主信息岛主题、字体、尺寸、背景、圆角和壁纸设置。",
        [
            "BackgroundColor", "ColorSource", "CustomForegroundColor", "IsCustomBackgroundColorEnabled",
            "IsCustomForegroundColorEnabled", "IsFallbackModeEnabled", "IsIslandSeperated", "IsWallpaperAutoUpdateEnabled",
            "MainWindowBodyFontSize", "MainWindowEmphasizedFontSize", "MainWindowFont", "MainWindowFontWeight2",
            "MainWindowLargeFontSize", "MainWindowLineVerticalMargin", "MainWindowSecondaryFontSize", "Opacity", "PrimaryColor",
            "RadiusX", "RadiusY", "Scale", "SelectedPlatteIndex", "TargetLightValue", "Theme",
            "UseExperimentColorPickingMethod", "WallpaperAutoUpdateIntervalSeconds", "WallpaperClassName", "WallpaperColorPlatte"
        ]),
        new("notification", "提醒", "提醒总开关、语音、声音、特效和置顶行为。提供方专属对象会作为结构化 JSON 保留编辑。",
        [
            "AllowNotificationEffect", "AllowNotificationSound", "AllowNotificationSpeech", "AllowNotificationTopmost",
            "IsNotificationEffectEnabled", "IsNotificationEnabled", "IsNotificationSoundEnabled", "IsNotificationTopmostEnabled",
            "IsSpeechEnabled", "NotificationEffectRenderingScale", "NotificationProvidersEnableStates",
            "NotificationProvidersNotifySettings", "NotificationProvidersPriority", "NotificationProvidersSettings",
            "NotificationSoundPath", "NotificationSoundVolume", "SelectedSpeechProvider", "SpeechSource", "SpeechVolume",
            "EdgeTtsVoiceName", "GptSoVitsSpeechSettings"
        ]),
        new("window", "窗口", "主信息岛停靠、层级、鼠标穿透、淡入淡出、录屏与窗口背景材质设置。",
        [
            "IsErrorLoadingRawInput", "IsIgnoreWorkAreaEnabled", "IsMainWindowBackgroundMaterialEnabled", "IsMouseClickingEnabled",
            "IsMouseInFadingEnabled", "IsMouseInFadingReversed", "IsScreenRecordingModeEnabled", "IsWindowCaptureBlockingEnabled",
            "MainWindowBackgroundMaterialType", "TouchInFadingDurationMs", "UseRawInput", "WindowDockingLocation",
            "WindowDockingMonitorIndex", "WindowDockingOffsetX", "WindowDockingOffsetY", "WindowLayer", "WindowTopmostRecheckMode"
        ]),
        new("weather", "天气", "天气位置、城市、图标和气象预警设置。天气联网查询仍由后续 WeatherService 移植承接。",
        [
            "AutoRefreshWeatherLocation", "CityId", "CityName", "ExcludedWeatherAlerts", "LastWeatherInfo", "NoTLSWeatherRequests",
            "WeatherIconId", "WeatherLatitude", "WeatherLocationSource", "WeatherLongitude"
        ]),
        new("automation-settings", "自动化", "自动化总开关和当前配置选择。工作流本体请在“自动化配置”页编辑。",
        ["CurrentAutomationConfig", "IsAutomationEnabled", "IsAutomationWarningVisible"]),
        new("update", "更新", "更新通道、镜像、状态与自动更新行为。ExusiAI 插件不会自行替换 ClassIsland 程序文件。",
        [
            "AutoInstallUpdateNextStartup", "IsAutoSelectUpgradeMirror", "LastCheckUpdateTime", "LastUpdateStatus",
            "SelectedUpdateChannelV2", "SelectedUpdateChannelV3", "SelectedUpdateMirrorV2", "UpdateMode"
        ]),
        new("plugins", "插件", "ClassIsland 插件源、镜像和自动更新设置；不会在 ExusiAI 进程中直接加载 ClassIsland 插件程序集。",
        [
            "AdditionalPluginIndexes", "IgnoreSslForPluginMirrors", "IsPluginMarketWarningVisible", "IsPluginsAutoUpdateEnabled",
            "IsPluginsUpdateNotificationEnabled", "OfficialIndexMirrors", "OfficialSelectedMirror", "PluginIndexes",
            "PluginIndexSelectedMirrors", "UserPluginIndexes"
        ]),
        new("themes", "主题", "ClassIsland 主题相关状态；主题包的发现/预览/应用将在主题运行时移植中继续补齐。",
        ["IsThemeSeparateInfoVisible", "IsThemeWarningVisible"]),
        new("management", "集控", "集控相关 Settings.json 字段与策略缓存。服务端连接、凭据和策略执行不会在没有明确安全隔离前主动运行。",
        ["IsTransientDisabled", "SettingsOverlay", "SettingsOverlays"]),
        new("compat", "兼容字段", "显示未被上述 Misha 设置页归类的 Settings.json 根字段，确保上游新增字段和插件字段仍可查看与编辑。",
        [])
    ];

    private static readonly HashSet<string> KnownKeys = Categories
        .Where(x => x.Id != "compat")
        .SelectMany(x => x.Keys)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ResolveKeys(MishaSettingsCategory category, JsonObject settings)
    {
        if (category.Id != "compat")
            return category.Keys;

        return settings.Select(x => x.Key)
            .Where(x => !KnownKeys.Contains(x))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

internal sealed class MishaSettingsCategoryPage : UserControl
{
    private readonly MishaPlatformStore store;
    private readonly MishaSettingsCategory category;
    private readonly StackPanel fields = new();
    private readonly TextBlock status = MishaUi.Note("");
    private readonly Dictionary<string, SettingEditor> editors = new(StringComparer.OrdinalIgnoreCase);

    public MishaSettingsCategoryPage(MishaPlatformStore store, MishaSettingsCategory category)
    {
        this.store = store;
        this.category = category;

        var root = MishaUi.Page(category.Title, category.Description);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var save = MishaUi.Button("保存 Settings.json");
        save.Click += async (_, _) => await SaveAsync();
        var reload = MishaUi.Button("重新载入", true);
        reload.Click += (_, _) => Reload();
        actions.Children.Add(save);
        actions.Children.Add(reload);
        root.Children.Add(actions);
        root.Children.Add(status);
        root.Children.Add(fields);

        Loaded += (_, _) => Reload();
        store.Changed += (_, _) => Dispatcher.Invoke(Reload);
        Content = MishaUi.Scroll(root);
    }

    private void Reload()
    {
        fields.Children.Clear();
        editors.Clear();

        var workspace = store.Workspace;
        if (workspace is null)
        {
            status.Text = "尚未连接 ClassIsland Settings.json。请先在“工作区”选择真实 ClassIsland 数据目录。";
            return;
        }

        status.Text = $"直接编辑：{workspace.SettingsPath}";
        var keys = MishaSettingsCatalog.ResolveKeys(category, workspace.Settings);
        if (keys.Count == 0)
        {
            fields.Children.Add(MishaUi.Note("当前 Settings.json 没有可归入此页的字段。"));
            return;
        }

        foreach (var key in keys)
        {
            if (!workspace.Settings.TryGetPropertyValue(key, out var node))
            {
                var missing = MishaUi.Note("当前 Settings.json 中没有写入此字段；保留 ClassIsland 自身的默认行为，不创建占位配置。");
                missing.Margin = new Thickness(0);
                fields.Children.Add(MishaUi.SettingRow(key, "不会自动补写上游未保存的默认值。", missing));
                continue;
            }

            var editor = SettingEditor.Create(node);
            editors[key] = editor;
            fields.Children.Add(MishaUi.SettingRow(key, Describe(node), editor.Element));
        }
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
            foreach (var (key, editor) in editors)
                workspace.Settings[key] = editor.ReadValue();

            await store.SaveWorkspaceSettingsAsync();
            status.Text = $"已原子保存 {System.IO.Path.GetFileName(workspace.SettingsPath)}，旧文件保存在 .bak。";
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            status.Text = $"未保存：{exception.Message}";
        }
    }

    private static string Describe(JsonNode? node) => node switch
    {
        null => "ClassIsland 原生 null；可保持 null 或改为任意合法 JSON 值。",
        JsonArray => "ClassIsland 原生数组；按 JSON 编辑并进行语法验证。",
        JsonObject => "ClassIsland 原生对象；按 JSON 编辑并进行语法验证，未知字段原样保留。",
        JsonValue value when value.TryGetValue<bool>(out _) => "布尔设置。",
        JsonValue value when value.TryGetValue<int>(out _) => "整数设置。",
        JsonValue value when value.TryGetValue<long>(out _) => "整数设置。",
        JsonValue value when value.TryGetValue<double>(out _) => "数值设置。",
        _ => "文本设置。"
    };

    private enum SettingEditorKind
    {
        Boolean,
        Integer,
        Long,
        Number,
        Text,
        Json
    }

    private sealed class SettingEditor
    {
        private readonly SettingEditorKind kind;
        private readonly CheckBox? checkBox;
        private readonly TextBox? textBox;

        private SettingEditor(SettingEditorKind kind, FrameworkElement element, CheckBox? checkBox = null, TextBox? textBox = null)
        {
            this.kind = kind;
            Element = element;
            this.checkBox = checkBox;
            this.textBox = textBox;
        }

        public FrameworkElement Element { get; }

        public static SettingEditor Create(JsonNode? node)
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<bool>(out var boolean))
                {
                    var checkBox = new CheckBox { IsChecked = boolean, Content = "启用", MinWidth = 90 };
                    return new(SettingEditorKind.Boolean, checkBox, checkBox: checkBox);
                }

                if (value.TryGetValue<int>(out var integer))
                    return Text(SettingEditorKind.Integer, integer.ToString(CultureInfo.InvariantCulture));
                if (value.TryGetValue<long>(out var longInteger))
                    return Text(SettingEditorKind.Long, longInteger.ToString(CultureInfo.InvariantCulture));
                if (value.TryGetValue<double>(out var number))
                    return Text(SettingEditorKind.Number, number.ToString("R", CultureInfo.InvariantCulture));
                if (value.TryGetValue<string>(out var text))
                    return Text(SettingEditorKind.Text, text ?? "", 360);
            }

            var jsonBox = new TextBox
            {
                Text = node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinWidth = 420,
                MinHeight = node is JsonArray or JsonObject ? 120 : 72,
                MaxHeight = 320
            };
            return new(SettingEditorKind.Json, jsonBox, textBox: jsonBox);
        }

        public JsonNode? ReadValue()
        {
            var text = textBox?.Text ?? "";
            return kind switch
            {
                SettingEditorKind.Boolean => JsonValue.Create(checkBox?.IsChecked == true),
                SettingEditorKind.Integer => JsonValue.Create(int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)),
                SettingEditorKind.Long => JsonValue.Create(long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)),
                SettingEditorKind.Number => JsonValue.Create(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)),
                SettingEditorKind.Text => JsonValue.Create(text),
                SettingEditorKind.Json => string.IsNullOrWhiteSpace(text)
                    ? throw new JsonException("JSON 值不能为空白。")
                    : JsonNode.Parse(text),
                _ => throw new InvalidOperationException("未知设置编辑器类型。")
            };
        }

        private static SettingEditor Text(SettingEditorKind kind, string value, double minWidth = 180)
        {
            var box = new TextBox { Text = value, MinWidth = minWidth };
            return new(kind, box, textBox: box);
        }
    }
}
