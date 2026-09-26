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

internal sealed record MishaSettingDescriptor(
    string Title,
    string Description,
    string? DefaultJson = null,
    IReadOnlyDictionary<int, string>? Choices = null,
    bool IsReadOnly = false);

internal static class MishaSettingsCatalog
{
    private static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RadiusX"] = "横向圆角半径",
        ["RadiusY"] = "纵向圆角半径",
        ["Scale"] = "整体缩放",
        ["MainWindowLineVerticalMargin"] = "组件行间距",
        ["BackgroundColor"] = "背景颜色",
        ["CustomForegroundColor"] = "文字颜色",
        ["IsCustomBackgroundColorEnabled"] = "使用自定义背景颜色",
        ["IsCustomForegroundColorEnabled"] = "使用自定义文字颜色",
        ["MainWindowFont"] = "界面字体",
        ["MainWindowBodyFontSize"] = "正文字号",
        ["MainWindowEmphasizedFontSize"] = "强调文字字号",
        ["MainWindowLargeFontSize"] = "大号文字字号",
        ["MainWindowSecondaryFontSize"] = "辅助文字字号",
        ["Opacity"] = "背景不透明度",
        ["IsIslandSeperated"] = "分离显示信息岛",
        ["Theme"] = "明暗主题",
        ["AnimationLevel"] = "动画级别",
        ["HideOnClass"] = "上课时自动隐藏",
        ["HideOnFullscreen"] = "全屏时自动隐藏",
        ["HideOnMaxWindow"] = "窗口最大化时自动隐藏",
        ["IsSplashEnabled"] = "显示启动画面",
        ["IsExactTimeEnabled"] = "启用精确时间",
        ["IsAutoBackupEnabled"] = "启用自动备份",
        ["AutoBackupIntervalDays"] = "自动备份间隔（天）",
        ["AutoBackupLimit"] = "保留备份数量",
        ["IsReportingEnabled"] = "发送诊断信息",
        ["IsAutomationEnabled"] = "启用自动化",
        ["IsNotificationEnabled"] = "启用提醒",
        ["IsSpeechEnabled"] = "启用语音播报",
        ["NotificationSoundVolume"] = "提醒音量",
        ["SpeechVolume"] = "语音音量",
        ["WindowDockingLocation"] = "信息岛停靠位置",
        ["WindowDockingMonitorIndex"] = "显示器",
        ["WindowDockingOffsetX"] = "水平偏移",
        ["WindowDockingOffsetY"] = "垂直偏移"
    };

    private static readonly IReadOnlyDictionary<string, MishaSettingDescriptor> Descriptors =
        new Dictionary<string, MishaSettingDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["AnimationLevel"] = new("动画级别", "控制界面动画数量；与 ClassIsland Misha 的 0–2 级一致。", "1",
                new Dictionary<int, string> { [0] = "关闭动画", [1] = "减少动画", [2] = "完整动画" }),
            ["CriticalSafeModeMethod"] = new("严重错误恢复方式", "ClassIsland 检测到连续启动错误时采用的恢复方式。", "0",
                new Dictionary<int, string> { [0] = "询问后处理", [1] = "自动进入安全模式", [2] = "继续启动" }),
            ["HideMode"] = new("隐藏方式", "决定满足隐藏条件时信息岛的处理方式。", "0",
                new Dictionary<int, string> { [0] = "隐藏窗口", [1] = "降低不透明度" }),
            ["HideOnClass"] = new("上课时隐藏", "进入上课状态后自动隐藏信息岛。", "false"),
            ["HideOnFullscreen"] = new("全屏时隐藏", "检测到其他应用全屏时自动隐藏信息岛。", "false"),
            ["HideOnMaxWindow"] = new("窗口最大化时隐藏", "检测到其他应用最大化时自动隐藏信息岛。", "false"),
            ["IsCriticalSafeMode"] = new("严重错误安全模式", "下次启动时进入 ClassIsland 严重错误安全模式。", "false"),
            ["IsSplashEnabled"] = new("显示启动画面", "启动 ClassIsland 模块时显示 Misha 启动画面。", "false"),
            ["IsWaitForTransientDisabled"] = new("等待临时禁用结束", "启动时等待集控临时禁用状态结束。", "false"),
            ["MultiWeekRotationMaxCycle"] = new("最大轮换周数", "多周课表轮换的最大周期，ClassIsland 默认 4 周。", "4"),
            ["ReduceProgressAccuracy"] = new("降低进度精度", "降低进度动画刷新频率以减少资源占用。", "false"),
            ["ShowDetailedStatusOnSplash"] = new("启动画面显示详细状态", "在启动画面展示当前加载步骤。", "false"),
            ["ShowSellingAnnouncement"] = new("显示开源软件提示", "显示 ClassIsland 原版关于倒卖与开源渠道的提示。", "true"),
            ["SingleWeekStartTime"] = new("学期开始日期", "多周轮换课表的计算起点，使用 ClassIsland 日期字符串格式。"),
            ["SplashCustomLogoSource"] = new("启动画面自定义图标", "自定义启动图标路径；留空使用默认图标。", "\"\""),
            ["SplashCustomText"] = new("启动画面自定义文字", "自定义启动画面标题；留空使用默认标题。", "\"\""),
            ["TaskBarIconClickBehavior"] = new("托盘图标单击行为", "单击托盘图标时执行的 ClassIsland 操作。", "0",
                new Dictionary<int, string> { [0] = "打开设置", [1] = "显示/隐藏信息岛", [2] = "打开档案编辑", [4] = "不执行操作" }),
            ["ExactTimeServer"] = new("时间服务器", "精确时间使用的 NTP 服务器。", "\"ntp.aliyun.com\""),
            ["IsExactTimeEnabled"] = new("使用精确时间", "从指定服务器同步时间，而不是只使用系统时间。", "true"),
            ["IsTimeAutoAdjustEnabled"] = new("自动时间偏移", "每天自动增加设定的时间偏移量。", "false"),
            ["TimeAutoAdjustSeconds"] = new("每日偏移增量", "每天自动调整的秒数。", "0.0"),
            ["TimeOffsetSeconds"] = new("课程时间偏移", "增大可抵消铃声提前，减小可抵消铃声滞后，单位为秒。", "0.0"),
            ["IsAutoBackupEnabled"] = new("自动备份", "按周期备份 ClassIsland 数据。", "true"),
            ["AutoBackupIntervalDays"] = new("备份间隔", "两次自动备份之间的天数。", "7"),
            ["AutoBackupLimit"] = new("备份保留数量", "超过此数量时清理较旧的自动备份。", "16"),
            ["BackupFilesSize"] = new("备份占用空间", "由 ClassIsland 运行时计算的展示值。", "\"计算中...\"", IsReadOnly: true),
            ["LastAutoBackupTime"] = new("上次自动备份", "由 ClassIsland 运行时维护。", IsReadOnly: true),
            ["IsReportingEnabled"] = new("发送诊断信息", "允许发送匿名崩溃与诊断信息。", "true"),
            ["TrustedProfileIds"] = new("受信任档案", "ClassIsland 信任的档案 GUID 列表；保留原生 JSON 结构。", "[]"),
            ["IsRefreshingToastEnabled"] = new("启用翻新提醒", "长时间未启动后显示 ClassIsland 翻新提醒。", "true"),
            ["RefreshingToastThresholdDays"] = new("提醒间隔", "连续多少天未启动后显示翻新提醒。", "20"),
            ["ShowRefreshingToastOnNextStart"] = new("下次启动显示提醒", "在下一次启动时强制显示一次翻新提醒。", "false"),
            ["MaxRefreshingToastCounts"] = new("最多显示次数", "一次翻新周期内允许显示的最大次数。", "5"),
            ["LeftRefreshingToastCounts"] = new("剩余显示次数", "由 ClassIsland 运行时维护的剩余提醒次数。", "0", IsReadOnly: true),
            ["RefreshingToastIsOnboardingGuide"] = new("迎新引导模式", "把下一次提醒作为迎新引导显示。", "false"),
            ["OnboardingToastTitle"] = new("迎新标题", "迎新提醒标题；空值由 ClassIsland 使用本地化默认文案。", "\"\""),
            ["OnboardingToastBody"] = new("迎新内容", "迎新提醒正文；空值由 ClassIsland 使用本地化默认文案。", "\"\""),
            ["RefreshingScopes"] = new("翻新范围", "ClassIsland 翻新服务的原生范围对象。", "{}"),
            ["AutoDisableCorruptPlugins"] = new("自动禁用损坏插件", "插件导致启动失败时自动隔离对应 ClassIsland 插件。", "true"),
            ["IsDebugEnabled"] = new("调试模式", "启用 ClassIsland 调试行为和额外日志。", "false"),
            ["IsDebugOptionsEnabled"] = new("显示调试选项", "在界面中显示实验性和调试入口。", "false"),
            ["CurrentComponentConfig"] = new("当前组件配置", "正在使用的 ClassIsland 组件配置名称。", "\"Default\""),
            ["ShowComponentsMigrateTip"] = new("显示组件迁移提示", "显示旧组件配置迁移提示。", "false"),
            ["IsNotificationEnabled"] = new("启用提醒", "ClassIsland 提醒系统总开关。", "true"),
            ["IsNotificationEffectEnabled"] = new("启用提醒特效", "允许提醒提供方显示视觉特效。", "true"),
            ["IsNotificationSoundEnabled"] = new("启用提醒声音", "允许提醒提供方播放声音。", "true"),
            ["IsNotificationTopmostEnabled"] = new("提醒窗口置顶", "提醒显示期间将提醒窗口保持在最上层。", "true"),
            ["IsSpeechEnabled"] = new("启用语音", "启用提醒语音播报。", "true"),
            ["AllowNotificationSpeech"] = new("允许提供方请求语音", "允许提醒提供方启用语音播报。", "false"),
            ["AllowNotificationEffect"] = new("允许提供方请求特效", "允许提醒提供方启用视觉特效。", "true"),
            ["AllowNotificationSound"] = new("允许提供方请求声音", "允许提醒提供方播放声音。", "false"),
            ["AllowNotificationTopmost"] = new("允许提供方请求置顶", "允许提醒提供方将提醒窗口置顶。", "true"),
            ["NotificationSoundPath"] = new("自定义提醒声音", "自定义提醒音频文件路径；留空使用默认声音。", "\"\""),
            ["NotificationSoundVolume"] = new("提醒音量", "提醒声音音量，范围 0–1。", "1.0"),
            ["SpeechVolume"] = new("语音音量", "语音播报音量，范围 0–1。", "1.0"),
            ["SpeechSource"] = new("语音来源", "选择系统语音或 ClassIsland 语音提供方。", "0",
                new Dictionary<int, string> { [0] = "系统语音", [1] = "语音提供方" }),
            ["SelectedSpeechProvider"] = new("语音提供方", "当前 ClassIsland 语音提供方 ID。", "\"classisland.speech.edgeTts\""),
            ["EdgeTtsVoiceName"] = new("Edge TTS 音色", "Edge TTS 使用的音色名称。", "\"zh-CN-XiaoxiaoNeural\""),
            ["NotificationEffectRenderingScale"] = new("特效渲染比例", "提醒特效的渲染缩放。", "1.0"),
            ["NotificationProvidersEnableStates"] = new("提醒提供方开关", "各提醒提供方的原生启用状态。", "{}"),
            ["NotificationProvidersPriority"] = new("提醒提供方顺序", "各提醒提供方的原生优先级列表。", "[]"),
            ["NotificationProvidersSettings"] = new("提醒提供方设置", "各提醒提供方的原生设置对象。", "{}"),
            ["NotificationProvidersNotifySettings"] = new("提醒方式设置", "各提醒提供方的语音、声音、特效与置顶设置。", "{}"),
            ["WeatherLongitude"] = new("经度", "天气位置经度。", "0.0"),
            ["WeatherLatitude"] = new("纬度", "天气位置纬度。", "0.0"),
            ["WeatherLocationSource"] = new("位置来源", "天气位置的来源。", "0",
                new Dictionary<int, string> { [0] = "手动选择", [1] = "自动定位" }),
            ["AutoRefreshWeatherLocation"] = new("自动更新位置", "自动刷新天气位置。", "false"),
            ["NoTLSWeatherRequests"] = new("兼容性天气请求", "仅在旧网络环境无法使用 TLS 时启用非加密天气请求。", "false"),
            ["CityId"] = new("城市 ID", "ClassIsland 天气提供方使用的城市标识。", "\"weathercn:101010100\""),
            ["CityName"] = new("城市名称", "设置页显示的城市名称。", "\"北京 (北京, 中国)\""),
            ["WeatherIconId"] = new("天气图标包", "当前天气图标提供方 ID。", "\"classisland.weatherIcons.lucide\""),
            ["ExcludedWeatherAlerts"] = new("忽略的气象预警", "不显示的气象预警类型列表。", "[]"),
            ["LastWeatherInfo"] = new("上次天气数据", "由天气服务维护的缓存。", "{}", IsReadOnly: true),
            ["CurrentAutomationConfig"] = new("当前自动化配置", "正在使用的 ClassIsland 自动化配置名称。", "\"Default\""),
            ["IsAutomationEnabled"] = new("启用自动化", "启用 ClassIsland 自动化触发器与动作。", "false"),
            ["IsAutomationWarningVisible"] = new("显示自动化安全提示", "首次使用自动化时显示安全提示。", "true"),
            ["AutoInstallUpdateNextStartup"] = new("下次启动安装更新", "已下载更新时在下次启动自动安装。", "true"),
            ["IsAutoSelectUpgradeMirror"] = new("自动选择更新镜像", "根据可用性自动选择更新下载镜像。", "true"),
            ["SelectedUpdateChannelV2"] = new("更新通道", "ClassIsland v2 更新通道。", "\"stable\""),
            ["SelectedUpdateChannelV3"] = new("新更新通道 ID", "ClassIsland v3 更新通道 GUID。", "\"00000000-0000-0000-0000-000000000000\""),
            ["SelectedUpdateMirrorV2"] = new("更新镜像", "当前更新镜像 ID。", "\"main\""),
            ["UpdateMode"] = new("更新方式", "ClassIsland 更新检查与安装方式。", "3",
                new Dictionary<int, string> { [0] = "关闭更新", [1] = "仅检查", [2] = "询问下载", [3] = "自动下载" }),
            ["LastCheckUpdateTime"] = new("上次检查时间", "由更新服务维护。", IsReadOnly: true),
            ["LastUpdateStatus"] = new("上次更新状态", "由更新服务维护。", IsReadOnly: true),
            ["IsPluginsAutoUpdateEnabled"] = new("自动更新插件", "自动更新已安装的 ClassIsland 插件。", "true"),
            ["IsPluginsUpdateNotificationEnabled"] = new("插件更新提醒", "插件更新完成后显示提醒。", "true"),
            ["IgnoreSslForPluginMirrors"] = new("忽略插件镜像证书错误", "仅用于旧网络环境；启用会降低连接安全性。", "false"),
            ["IsPluginMarketWarningVisible"] = new("显示插件市场安全提示", "进入插件市场时显示第三方插件风险提示。", "true"),
            ["OfficialSelectedMirror"] = new("官方插件源镜像", "当前官方插件索引镜像 ID。", "\"github\""),
            ["PluginIndexSelectedMirrors"] = new("插件源镜像选择", "每个插件索引当前选中的镜像。", "{}"),
            ["UserPluginIndexes"] = new("用户插件源", "用户添加的 ClassIsland 插件源列表。", "[]"),
            ["AdditionalPluginIndexes"] = new("附加插件源", "由其他组件注册的插件源。", "{}"),
            ["PluginIndexes"] = new("插件索引缓存", "由插件市场服务维护的索引缓存。", "[]", IsReadOnly: true),
            ["OfficialIndexMirrors"] = new("官方镜像列表", "ClassIsland 官方插件索引镜像表。", "{}"),
            ["IsTransientDisabled"] = new("临时禁用", "集控策略要求 ClassIsland 临时停止运行。", "false", IsReadOnly: true),
            ["SettingsOverlay"] = new("集控设置覆盖", "ClassIsland 1.x 兼容字段名；保持原生对象结构。", "{}"),
            ["SettingsOverlays"] = new("集控设置覆盖集合", "ClassIsland 集控下发的设置覆盖集合。", "{}")
        };

    public static string DisplayName(string key) => DisplayNames.TryGetValue(key, out var value) ? value : key;

    public static MishaSettingDescriptor Describe(string key) =>
        Descriptors.TryGetValue(key, out var value)
            ? value
            : new MishaSettingDescriptor(DisplayName(key), "ClassIsland 原生设置字段；保存时保持原 JSON 类型。");

    public static IReadOnlyList<MishaSettingsCategory> Categories { get; } =
    [
        new("general", "基本", "对应 ClassIsland 2.2 Misha 的行为与启动设置。未写入文件的项目显示上游默认值，保存后仍使用原生字段名和 JSON 类型。",
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
            var descriptor = MishaSettingsCatalog.Describe(key);
            var exists = workspace.Settings.TryGetPropertyValue(key, out var node);
            var effectiveNode = node?.DeepClone();
            if (!exists && descriptor.DefaultJson is not null)
                effectiveNode = JsonNode.Parse(descriptor.DefaultJson);

            if (!exists && effectiveNode is null)
            {
                fields.Children.Add(MishaUi.SettingRow(
                    descriptor.Title,
                    descriptor.Description + " 当前文件未记录该值。",
                    MishaUi.Note("等待 ClassIsland 写入运行时值")));
                continue;
            }

            var editor = SettingEditor.Create(effectiveNode, descriptor);
            if (!descriptor.IsReadOnly)
                editors[key] = editor;
            fields.Children.Add(MishaUi.SettingRow(descriptor.Title, descriptor.Description, editor.Element));
        }

        if (editors.Count == 0)
        {
            fields.Children.Add(MishaUi.Note("此分类只有 ClassIsland 运行时维护的只读状态。"));
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

    private enum SettingEditorKind
    {
        Boolean,
        Choice,
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
        private readonly ComboBox? comboBox;

        private SettingEditor(
            SettingEditorKind kind,
            FrameworkElement element,
            CheckBox? checkBox = null,
            TextBox? textBox = null,
            ComboBox? comboBox = null)
        {
            this.kind = kind;
            Element = element;
            this.checkBox = checkBox;
            this.textBox = textBox;
            this.comboBox = comboBox;
        }

        public FrameworkElement Element { get; }

        public static SettingEditor Create(JsonNode? node, MishaSettingDescriptor descriptor)
        {
            if (descriptor.Choices is not null &&
                node is JsonValue choiceValue &&
                choiceValue.TryGetValue<int>(out var selectedValue))
            {
                var choices = descriptor.Choices
                    .Select(x => new SettingChoice(x.Key, x.Value))
                    .ToArray();
                var combo = new ComboBox
                {
                    ItemsSource = choices,
                    DisplayMemberPath = nameof(SettingChoice.Title),
                    SelectedItem = choices.FirstOrDefault(x => x.Value == selectedValue) ?? choices.FirstOrDefault(),
                    MinWidth = 190,
                    IsEnabled = !descriptor.IsReadOnly
                };
                return new(SettingEditorKind.Choice, combo, comboBox: combo);
            }

            if (node is JsonValue value)
            {
                if (value.TryGetValue<bool>(out var boolean))
                {
                    var checkBox = new CheckBox
                    {
                        IsChecked = boolean,
                        Content = boolean ? "已启用" : "已关闭",
                        MinWidth = 90,
                        IsEnabled = !descriptor.IsReadOnly
                    };
                    checkBox.Checked += (_, _) => checkBox.Content = "已启用";
                    checkBox.Unchecked += (_, _) => checkBox.Content = "已关闭";
                    return new(SettingEditorKind.Boolean, checkBox, checkBox: checkBox);
                }

                if (value.TryGetValue<int>(out var integer))
                    return Text(SettingEditorKind.Integer, integer.ToString(CultureInfo.InvariantCulture), descriptor.IsReadOnly);
                if (value.TryGetValue<long>(out var longInteger))
                    return Text(SettingEditorKind.Long, longInteger.ToString(CultureInfo.InvariantCulture), descriptor.IsReadOnly);
                if (value.TryGetValue<double>(out var number))
                    return Text(SettingEditorKind.Number, number.ToString("R", CultureInfo.InvariantCulture), descriptor.IsReadOnly);
                if (value.TryGetValue<string>(out var text))
                    return Text(SettingEditorKind.Text, text ?? "", descriptor.IsReadOnly, 360);
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
            jsonBox.IsReadOnly = descriptor.IsReadOnly;
            return new(SettingEditorKind.Json, jsonBox, textBox: jsonBox);
        }

        public JsonNode? ReadValue()
        {
            var text = textBox?.Text ?? "";
            return kind switch
            {
                SettingEditorKind.Boolean => JsonValue.Create(checkBox?.IsChecked == true),
                SettingEditorKind.Choice => JsonValue.Create((comboBox?.SelectedItem as SettingChoice)?.Value
                    ?? throw new FormatException("请选择一个有效选项。")),
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

        private static SettingEditor Text(SettingEditorKind kind, string value, bool isReadOnly, double minWidth = 180)
        {
            var box = new TextBox { Text = value, MinWidth = minWidth, IsReadOnly = isReadOnly };
            return new(kind, box, textBox: box);
        }

        private sealed record SettingChoice(int Value, string Title);
    }
}
