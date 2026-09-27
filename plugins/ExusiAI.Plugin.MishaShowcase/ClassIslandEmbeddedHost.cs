using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Plugin.MishaShowcase;

internal enum ClassIslandEmbeddedModuleGroup
{
    Host,
    Service,
    Component,
    NotificationProvider,
    Trigger,
    Rule,
    Action,
    Authorization,
    Speech,
    Theme,
    ProfileTransfer,
    Tutorial
}

internal enum ClassIslandPortState
{
    Native,
    CompatibilityAdapter,
    HostMapped,
    SourceTracked
}

internal sealed record ClassIslandEmbeddedModuleDescriptor(
    string Id,
    string UpstreamRegistration,
    ClassIslandEmbeddedModuleGroup Group,
    ClassIslandPortState State,
    string ExusiAIAdapter)
{
    public bool RunsInProcess => true;
}

/// <summary>
/// Source baseline and module map for the ClassIsland 2.2 Misha port.
///
/// The catalog intentionally mirrors the registrations in ClassIsland/App.Services.xaml.cs instead
/// of treating ClassIsland.exe as a runtime dependency. Every entry is owned by the ExusiAI plugin
/// lifecycle. Entries marked SourceTracked still need deeper behavioural parity, but they may not
/// be satisfied by launching or embedding an independent ClassIsland application process.
/// </summary>
internal static class ClassIslandEmbeddedModuleCatalog
{
    public const string UpstreamRepository = "ClassIsland/ClassIsland";
    public const string UpstreamBranch = "develop/v2/misha-alpha";
    public const string UpstreamCommit = "08808615899d1a4abb8e0ef576bf1e247adde10f";
    public const string UpstreamServiceRegistrationFile = "ClassIsland/App.Services.xaml.cs";

    public static IReadOnlyList<ClassIslandEmbeddedModuleDescriptor> All { get; } =
    [
        Host("workspace", "SettingsService", ClassIslandPortState.Native, nameof(MishaPlatformStore)),
        Host("main-window", "MainWindow", ClassIslandPortState.Native, nameof(MishaMainWindowRuntime)),

        Service("settings", "SettingsService", ClassIslandPortState.Native, "MishaPlatformStore + MishaSettingsCatalog"),
        Service("update", "UpdateService", ClassIslandPortState.HostMapped, "ExusiAI host update lifecycle"),
        Service("taskbar-icon", "ITaskBarIconService / TaskBarIconService", ClassIslandPortState.HostMapped, "ExusiAI tray lifecycle"),
        Service("notification-host", "INotificationHostService / NotificationHostService", ClassIslandPortState.CompatibilityAdapter, "MishaScheduleNotificationPresenter"),
        Service("notification-worker", "INotificationWorkerService / NotificationWorkerService", ClassIslandPortState.CompatibilityAdapter, "ClassIslandScheduleNotificationTracker"),
        Service("theme", "IThemeService / ThemeService", ClassIslandPortState.CompatibilityAdapter, "ClassIslandThemeCompatibilityLayer"),
        Service("mini-info-provider", "MiniInfoProviderHostService", ClassIslandPortState.SourceTracked, "ClassIsland embedded module host"),
        Service("weather", "IWeatherService / WeatherService", ClassIslandPortState.CompatibilityAdapter, nameof(ClassIslandWeatherCache)),
        Service("file-folder", "FileFolderService", ClassIslandPortState.HostMapped, "ExusiAI file services"),
        Service("attached-settings", "IAttachedSettingsHostService / AttachedSettingsHostService", ClassIslandPortState.SourceTracked, "Misha settings adapters"),
        Service("profile", "IProfileService / ProfileService", ClassIslandPortState.Native, nameof(ClassIslandProfileDocument)),
        Service("splash", "ISplashService / SplashService", ClassIslandPortState.HostMapped, "ExusiAI host startup"),
        Service("hang", "IHangService / HangService", ClassIslandPortState.HostMapped, "ExusiAI diagnostics"),
        Service("console", "ConsoleService", ClassIslandPortState.HostMapped, "ExusiAI logging"),
        Service("diagnostic", "DiagnosticService", ClassIslandPortState.HostMapped, "ExusiAI crash/log pipeline"),
        Service("management", "IManagementService / ManagementService", ClassIslandPortState.SourceTracked, "Misha management pages"),
        Service("app-log", "AppLogService", ClassIslandPortState.HostMapped, "IExtensionLogger"),
        Service("components", "IComponentsService / ComponentsService", ClassIslandPortState.Native, "ClassIslandComponentLayoutDocument + MishaNativeMainWindowRenderer"),
        Service("lessons", "ILessonsService / LessonsService", ClassIslandPortState.Native, nameof(ClassIslandRuntimeStateResolver)),
        Service("uri-navigation", "IUriNavigationService / UriNavigationService", ClassIslandPortState.SourceTracked, "ExusiAI navigation bridge"),
        Service("memory-watchdog", "MemoryWatchDogService", ClassIslandPortState.HostMapped, "ExusiAI host process lifecycle"),
        Service("plugin", "IPluginService / PluginService", ClassIslandPortState.HostMapped, "ExusiAI extension runtime"),
        Service("plugin-market", "IPluginMarketService / PluginMarketService", ClassIslandPortState.HostMapped, "ExusiAI plugin market"),
        Service("ruleset", "IRulesetService / RulesetService", ClassIslandPortState.CompatibilityAdapter, "ClassIslandAutomationDocument"),
        Service("action", "IActionService / ActionService", ClassIslandPortState.CompatibilityAdapter, nameof(MishaAutomationRuntime)),
        Service("window-rule", "IWindowRuleService / WindowRuleService", ClassIslandPortState.SourceTracked, "ClassIsland automation compatibility"),
        Service("automation", "IAutomationService / AutomationService", ClassIslandPortState.CompatibilityAdapter, "ClassIslandAutomationDocument + MishaAutomationRuntime"),
        Service("speech", "ISpeechService", ClassIslandPortState.SourceTracked, "ClassIsland speech compatibility"),
        Service("exact-time", "IExactTimeService / ExactTimeService", ClassIslandPortState.CompatibilityAdapter, "Misha exact-time settings/runtime clock"),
        Service("profile-analyze", "IProfileAnalyzeService / ProfileAnalyzeService", ClassIslandPortState.SourceTracked, "ClassIsland profile compatibility"),
        Service("ipc", "IIpcService / IpcService", ClassIslandPortState.SourceTracked, "ExusiAI in-process bridge"),
        Service("authorize", "IAuthorizeService / AuthorizeService", ClassIslandPortState.SourceTracked, "ClassIsland authorization compatibility"),
        Service("uri-trigger", "UriTriggerHandlerService", ClassIslandPortState.SourceTracked, "ClassIsland automation compatibility"),
        Service("signal-trigger", "SignalTriggerHandlerService", ClassIslandPortState.SourceTracked, "ClassIsland automation compatibility"),
        Service("tray-trigger", "TrayMenuTriggerHandlerService", ClassIslandPortState.SourceTracked, "ClassIsland automation compatibility"),
        Service("announcement", "IAnnouncementService / AnnouncementService", ClassIslandPortState.SourceTracked, "ExusiAI notification surface"),
        Service("location", "ILocationService", ClassIslandPortState.SourceTracked, "ExusiAI location adapter"),
        Service("xaml-theme", "IXamlThemeService / XamlThemeService", ClassIslandPortState.CompatibilityAdapter, "ClassIslandThemeCompatibilityLayer + ClassIslandThemeWpfAdapter"),
        Service("audio", "IAudioService / AudioService", ClassIslandPortState.SourceTracked, "ClassIsland audio compatibility"),
        Service("tutorial", "ITutorialService / TutorialService", ClassIslandPortState.SourceTracked, "ExusiAI plugin navigation"),
        Service("refreshing", "IRefreshingService / RefreshingService", ClassIslandPortState.SourceTracked, "Misha refreshing settings"),

        Component("text", "TextComponent", ClassIslandPortState.Native, "MishaNativeMainWindowRenderer"),
        Component("separator", "SeparatorComponent", ClassIslandPortState.Native, "MishaNativeMainWindowRenderer"),
        Component("schedule", "ScheduleComponent", ClassIslandPortState.Native, "MishaNativeMainWindowRenderer"),
        Component("date", "DateComponent", ClassIslandPortState.Native, "MishaNativeMainWindowRenderer"),
        Component("clock", "ClockComponent", ClassIslandPortState.Native, "MishaNativeMainWindowRenderer"),
        Component("weather", "WeatherComponent", ClassIslandPortState.CompatibilityAdapter, "MishaNativeMainWindowRenderer + ClassIslandWeatherCache"),
        Component("countdown", "CountDownComponent", ClassIslandPortState.Native, "MishaNativeMainWindowRenderer"),
        Component("slide", "SlideComponent", ClassIslandPortState.Native, "MishaNativeMainWindowRenderer"),
        Component("rolling", "RollingComponent", ClassIslandPortState.Native, "MishaNativeMainWindowRenderer"),
        Component("group", "GroupComponent", ClassIslandPortState.Native, "MishaNativeMainWindowRenderer"),
        Component("stack", "StackComponent", ClassIslandPortState.Native, "MishaNativeMainWindowRenderer"),

        Notification("class", "ClassNotificationProvider", ClassIslandPortState.CompatibilityAdapter, "ClassIslandScheduleNotificationTracker"),
        Notification("after-school", "AfterSchoolNotificationProvider", ClassIslandPortState.CompatibilityAdapter, "ClassIslandScheduleNotificationTracker"),
        Notification("weather", "WeatherNotificationProvider", ClassIslandPortState.SourceTracked, "ClassIsland weather notification compatibility"),
        Notification("management", "ManagementNotificationProvider", ClassIslandPortState.SourceTracked, "ClassIsland management compatibility"),
        Notification("action", "ActionNotificationProvider", ClassIslandPortState.CompatibilityAdapter, "MishaScheduleNotificationPresenter"),

        Trigger("ruleset-changed", "RulesetChangedTrigger", ClassIslandPortState.SourceTracked),
        Trigger("signal", "SignalTrigger", ClassIslandPortState.SourceTracked),
        Trigger("uri", "UriTrigger", ClassIslandPortState.SourceTracked),
        Trigger("tray-menu", "TrayMenuTrigger", ClassIslandPortState.SourceTracked),
        Trigger("cron", "CronTrigger", ClassIslandPortState.CompatibilityAdapter),
        Trigger("app-startup", "AppStartupTrigger", ClassIslandPortState.HostMapped),
        Trigger("app-stopping", "AppStoppingTrigger", ClassIslandPortState.HostMapped),
        Trigger("on-class", "OnClassTrigger", ClassIslandPortState.Native),
        Trigger("on-breaking-time", "OnBreakingTimeTrigger", ClassIslandPortState.Native),
        Trigger("on-after-school", "OnAfterSchoolTrigger", ClassIslandPortState.Native),
        Trigger("time-state-changed", "CurrentTimeStateChangedTrigger", ClassIslandPortState.Native),
        Trigger("pre-time-point", "PreTimePointTrigger", ClassIslandPortState.CompatibilityAdapter),

        Rule("always-true", "classisland.test.true", ClassIslandPortState.CompatibilityAdapter),
        Rule("always-false", "classisland.test.false", ClassIslandPortState.CompatibilityAdapter),
        Rule("window-class", "classisland.windows.className", ClassIslandPortState.SourceTracked),
        Rule("window-text", "classisland.windows.text", ClassIslandPortState.SourceTracked),
        Rule("window-status", "classisland.windows.status", ClassIslandPortState.SourceTracked),
        Rule("window-process", "classisland.windows.processName", ClassIslandPortState.SourceTracked),
        Rule("current-subject", "classisland.lessons.currentSubject", ClassIslandPortState.CompatibilityAdapter),
        Rule("next-subject", "classisland.lessons.nextSubject", ClassIslandPortState.CompatibilityAdapter),
        Rule("previous-subject", "classisland.lessons.previousSubject", ClassIslandPortState.CompatibilityAdapter),
        Rule("time-state", "classisland.lessons.timeState", ClassIslandPortState.CompatibilityAdapter),
        Rule("current-weather", "classisland.weather.currentWeather", ClassIslandPortState.SourceTracked),
        Rule("tomorrow-weather", "classisland.weather.tomorrowWeather", ClassIslandPortState.SourceTracked),
        Rule("weather-alert", "classisland.weather.hasWeatherAlert", ClassIslandPortState.SourceTracked),
        Rule("rain-time", "classisland.weather.rainTime", ClassIslandPortState.SourceTracked),
        Rule("sunrise-set", "classisland.weather.sunRiseSet", ClassIslandPortState.SourceTracked),

        Action("broadcast-signal", "classisland.broadcastSignal", ClassIslandPortState.SourceTracked),
        Action("run", "RunAction", ClassIslandPortState.SourceTracked),
        Action("notification", "NotificationAction", ClassIslandPortState.CompatibilityAdapter),
        Action("sleep", "SleepAction", ClassIslandPortState.SourceTracked),
        Action("modify-settings", "ModifyAppSettingsAction", ClassIslandPortState.SourceTracked),
        Action("weather-notification", "WeatherNotificationAction", ClassIslandPortState.SourceTracked),
        Action("quit", "AppQuitAction", ClassIslandPortState.HostMapped),
        Action("restart", "AppRestartAction", ClassIslandPortState.HostMapped),

        Descriptor("password", "PasswordAuthorizeProvider", ClassIslandEmbeddedModuleGroup.Authorization, ClassIslandPortState.SourceTracked, "ClassIsland authorization compatibility"),
        Descriptor("system-speech", "SystemSpeechService", ClassIslandEmbeddedModuleGroup.Speech, ClassIslandPortState.SourceTracked, "ClassIsland speech compatibility"),
        Descriptor("edge-tts", "EdgeTtsService", ClassIslandEmbeddedModuleGroup.Speech, ClassIslandPortState.SourceTracked, "ClassIsland speech compatibility"),
        Descriptor("gpt-sovits", "GptSoVitsService", ClassIslandEmbeddedModuleGroup.Speech, ClassIslandPortState.SourceTracked, "ClassIsland speech compatibility"),
        Descriptor("classic", "classisland.classic", ClassIslandEmbeddedModuleGroup.Theme, ClassIslandPortState.CompatibilityAdapter, "ClassIslandThemeCompatibilityLayer"),
        Descriptor("fluent", "classisland.fluent", ClassIslandEmbeddedModuleGroup.Theme, ClassIslandPortState.CompatibilityAdapter, "ClassIslandThemeCompatibilityLayer"),
        Descriptor("cses-import", "classisland.profileTransfer.import.cses", ClassIslandEmbeddedModuleGroup.ProfileTransfer, ClassIslandPortState.SourceTracked, "ClassIsland profile transfer compatibility"),
        Descriptor("legacy-v1-import", "classisland.profileTransfer.import.legacyV1", ClassIslandEmbeddedModuleGroup.ProfileTransfer, ClassIslandPortState.SourceTracked, "ClassIsland profile transfer compatibility"),
        Descriptor("classwidgets1-import", "classisland.profileTransfer.import.classWidgets", ClassIslandEmbeddedModuleGroup.ProfileTransfer, ClassIslandPortState.SourceTracked, "ClassIsland profile transfer compatibility"),
        Descriptor("classwidgets2-import", "classisland.profileTransfer.import.classWidgets2", ClassIslandEmbeddedModuleGroup.ProfileTransfer, ClassIslandPortState.SourceTracked, "ClassIsland profile transfer compatibility"),
        Descriptor("cses-export", "classisland.profileTransfer.export.cses", ClassIslandEmbeddedModuleGroup.ProfileTransfer, ClassIslandPortState.SourceTracked, "ClassIsland profile transfer compatibility"),
        Descriptor("getting-started", "classisland.getStarted", ClassIslandEmbeddedModuleGroup.Tutorial, ClassIslandPortState.SourceTracked, "ExusiAI plugin navigation")
    ];

    private static ClassIslandEmbeddedModuleDescriptor Host(string id, string upstream, ClassIslandPortState state, string adapter) =>
        Descriptor(id, upstream, ClassIslandEmbeddedModuleGroup.Host, state, adapter);

    private static ClassIslandEmbeddedModuleDescriptor Service(string id, string upstream, ClassIslandPortState state, string adapter) =>
        Descriptor(id, upstream, ClassIslandEmbeddedModuleGroup.Service, state, adapter);

    private static ClassIslandEmbeddedModuleDescriptor Component(string id, string upstream, ClassIslandPortState state, string adapter) =>
        Descriptor(id, upstream, ClassIslandEmbeddedModuleGroup.Component, state, adapter);

    private static ClassIslandEmbeddedModuleDescriptor Notification(string id, string upstream, ClassIslandPortState state, string adapter) =>
        Descriptor(id, upstream, ClassIslandEmbeddedModuleGroup.NotificationProvider, state, adapter);

    private static ClassIslandEmbeddedModuleDescriptor Trigger(string id, string upstream, ClassIslandPortState state) =>
        Descriptor(id, upstream, ClassIslandEmbeddedModuleGroup.Trigger, state, nameof(MishaAutomationRuntime));

    private static ClassIslandEmbeddedModuleDescriptor Rule(string id, string upstream, ClassIslandPortState state) =>
        Descriptor(id, upstream, ClassIslandEmbeddedModuleGroup.Rule, state, "ClassIslandAutomationDocument");

    private static ClassIslandEmbeddedModuleDescriptor Action(string id, string upstream, ClassIslandPortState state) =>
        Descriptor(id, upstream, ClassIslandEmbeddedModuleGroup.Action, state, nameof(MishaAutomationRuntime));

    private static ClassIslandEmbeddedModuleDescriptor Descriptor(
        string id,
        string upstream,
        ClassIslandEmbeddedModuleGroup group,
        ClassIslandPortState state,
        string adapter) =>
        new(group.ToString().ToLowerInvariant() + "." + id, upstream, group, state, adapter);
}

internal sealed record ClassIslandEmbeddedRuntimeContext(
    MishaPlatformStore Store,
    IExtensionContext ExtensionContext);

internal interface IClassIslandEmbeddedRuntimeModule
{
    string Id { get; }
    Task InitializeAsync(ClassIslandEmbeddedRuntimeContext context, CancellationToken cancellationToken);
    Task StartAsync(ClassIslandEmbeddedRuntimeContext context, CancellationToken cancellationToken);
    Task StopAsync(ClassIslandEmbeddedRuntimeContext context, CancellationToken cancellationToken);
}

internal sealed class ClassIslandWorkspaceRuntimeModule : IClassIslandEmbeddedRuntimeModule
{
    public string Id => "host.workspace";

    public async Task InitializeAsync(ClassIslandEmbeddedRuntimeContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await context.Store.RestoreLastWorkspaceAsync())
            context.ExtensionContext.Logger.Information("Restored the last imported ClassIsland workspace from the ExusiAI data copy.");
        else
            context.ExtensionContext.Logger.Information("No reusable ClassIsland workspace binding was found; import is required once.");
    }

    public Task StartAsync(ClassIslandEmbeddedRuntimeContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task StopAsync(ClassIslandEmbeddedRuntimeContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

internal sealed class ClassIslandMainWindowRuntimeModule : IClassIslandEmbeddedRuntimeModule
{
    private MishaMainWindowRuntime? runtime;
    private System.Windows.Threading.Dispatcher? dispatcher;

    public string Id => "host.main-window";

    public Task InitializeAsync(ClassIslandEmbeddedRuntimeContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task StartAsync(ClassIslandEmbeddedRuntimeContext context, CancellationToken cancellationToken)
    {
        if (runtime is not null) return;
        cancellationToken.ThrowIfCancellationRequested();

        dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            context.ExtensionContext.Logger.Warning("WPF Application is unavailable; ClassIsland embedded main-window module was not started.");
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            runtime = new MishaMainWindowRuntime(context.Store, context.ExtensionContext.Logger);
            runtime.Start();
        });
    }

    public async Task StopAsync(ClassIslandEmbeddedRuntimeContext context, CancellationToken cancellationToken)
    {
        if (runtime is null)
        {
            dispatcher = null;
            return;
        }

        var shutdownDispatcher = dispatcher ?? System.Windows.Application.Current?.Dispatcher;
        if (shutdownDispatcher is null)
            throw new InvalidOperationException("ClassIsland embedded main-window module lost its WPF dispatcher during shutdown.");

        Task shutdown = Task.CompletedTask;
        await shutdownDispatcher.InvokeAsync(() =>
        {
            shutdown = runtime.DisposeAsync().AsTask();
            runtime = null;
        });
        await shutdown.WaitAsync(cancellationToken);
        dispatcher = null;
    }
}

/// <summary>
/// Owns the ClassIsland-derived runtime inside the ExusiAI plugin lifecycle. This is deliberately
/// an in-process host: it never discovers, starts, supervises, or depends on ClassIsland.exe.
/// </summary>
internal sealed class ClassIslandEmbeddedHost : IAsyncDisposable
{
    private readonly IClassIslandEmbeddedRuntimeModule[] runtimeModules;
    private IExtensionContext? extensionContext;
    private ClassIslandEmbeddedRuntimeContext? runtimeContext;
    private bool initialized;
    private bool started;

    public ClassIslandEmbeddedHost()
    {
        Store = new MishaPlatformStore();
        runtimeModules =
        [
            new ClassIslandWorkspaceRuntimeModule(),
            new ClassIslandMainWindowRuntimeModule()
        ];
    }

    public MishaPlatformStore Store { get; }

    public IReadOnlyList<ClassIslandEmbeddedModuleDescriptor> Modules => ClassIslandEmbeddedModuleCatalog.All;

    internal IReadOnlyList<string> RuntimeModuleIds => runtimeModules.Select(x => x.Id).ToArray();

    public async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        if (initialized) return;
        extensionContext = context;
        runtimeContext = new ClassIslandEmbeddedRuntimeContext(Store, context);

        foreach (var module in runtimeModules)
            await module.InitializeAsync(runtimeContext, cancellationToken);

        var active = Modules.Count(x => x.State is ClassIslandPortState.Native or ClassIslandPortState.CompatibilityAdapter or ClassIslandPortState.HostMapped);
        context.Logger.Information(
            "ClassIsland Misha embedded host initialized from " + ClassIslandEmbeddedModuleCatalog.UpstreamBranch + "@" + ClassIslandEmbeddedModuleCatalog.UpstreamCommit[..12] + "; " +
            "tracked registrations: " + Modules.Count + ", active/host-mapped: " + active + ". No independent ClassIsland process is used.");
        initialized = true;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!initialized || runtimeContext is null || extensionContext is null)
            throw new InvalidOperationException("ClassIsland embedded host must be initialized before start.");
        if (started) return;

        var startedCount = 0;
        try
        {
            foreach (var module in runtimeModules)
            {
                await module.StartAsync(runtimeContext, cancellationToken);
                startedCount++;
            }
            started = true;
            extensionContext.Logger.Information("ClassIsland Misha embedded modules started inside ExusiAI.");
        }
        catch
        {
            for (var index = startedCount - 1; index >= 0; index--)
            {
                try
                {
                    await runtimeModules[index].StopAsync(runtimeContext, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    extensionContext.Logger.Error("Failed to roll back ClassIsland embedded module '" + runtimeModules[index].Id + "'.", exception);
                }
            }
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (runtimeContext is null || extensionContext is null) return;

        Exception? firstFailure = null;
        for (var index = runtimeModules.Length - 1; index >= 0; index--)
        {
            try
            {
                await runtimeModules[index].StopAsync(runtimeContext, cancellationToken);
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
                extensionContext.Logger.Error("Failed to stop ClassIsland embedded module '" + runtimeModules[index].Id + "'.", exception);
            }
        }

        started = false;
        extensionContext.Logger.Information("ClassIsland Misha embedded modules stopped.");
        if (firstFailure is not null)
            throw new InvalidOperationException("One or more ClassIsland embedded modules failed to stop cleanly.", firstFailure);
    }

    public async ValueTask DisposeAsync()
    {
        if (runtimeContext is not null && extensionContext is not null)
            await StopAsync(CancellationToken.None);
        runtimeContext = null;
        extensionContext = null;
        initialized = false;
    }
}
