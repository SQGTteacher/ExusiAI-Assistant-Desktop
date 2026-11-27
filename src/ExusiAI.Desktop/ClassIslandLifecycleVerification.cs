using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.Runtime;
using ExusiAI.Extension.Wpf;
using ExusiAI.Theme;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExusiAI.Desktop;

internal static partial class ClassIslandVerification
{
    private const int LifecycleCycles = 8;
    private const int SoakCycles = 24;

    private static async Task VerifyLifecycleAsync(string package, string outputDirectory, bool soak)
    {
        const string id = "exusiai.classisland";
        var data = Path.Combine(outputDirectory, "LifecycleData");
        var report = Path.Combine(outputDirectory, "lifecycle.log");
        void Log(string message) => File.AppendAllText(report, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        var cycles = soak ? SoakCycles : LifecycleCycles;
        var elapsed = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        var metrics = Path.Combine(outputDirectory, "lifecycle-resources.csv");
        File.WriteAllText(metrics, "cycle,phase,elapsed_ms,operation_ms,private_bytes,working_set_bytes,handles,threads,user_objects,gdi_objects"
            + Environment.NewLine);
        Log($"Windows {Environment.OSVersion.Version}, {RuntimeInformation.ProcessArchitecture}, {cycles} cycles.");
        void Sample(int cycle, string phase, double? operationMs = null)
        {
            process.Refresh();
            File.AppendAllText(metrics, FormattableString.Invariant(
                $"{cycle},{phase},{elapsed.ElapsedMilliseconds},{operationMs:F1},{process.PrivateMemorySize64},{process.WorkingSet64},{process.HandleCount},{process.Threads.Count},{GetGuiResources(process.Handle, 1)},{GetGuiResources(process.Handle, 0)}")
                + Environment.NewLine);
        }

        await using var runtime = new ExtensionRuntime(
            new(new(), new(), NullLogger<PackageDiscoveryService>.Instance), NullLogger<ExtensionRuntime>.Instance);
        var navigation = new WpfNavigationRegistry();
        var tray = new WpfTrayRegistry();
        using var theme = new ThemeService();
        IDisposable? activeHost = null;
        object? activePlugin = null;
        Type? hostType = null;
        // Replace the still-dormant host on first initialization, before StartAsync
        // can use the user's startup preference. The manager owns all later
        // suspend/resume/final-stop transitions; native reads/writes use isolated data.
        runtime.EntriesChanged += (_, _) =>
        {
            var initialized = runtime.Entries.SingleOrDefault(entry => entry.Package.Manifest.Id == id &&
                entry.State == PackageState.Initialized);
            if (initialized?.Instance is not { } plugin) return;
            if (ReferenceEquals(activePlugin, plugin)) return;
            activePlugin = plugin;
            var field = plugin.GetType().GetField("host", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ((IDisposable)field.GetValue(plugin)!).Dispose();
            hostType = plugin.GetType().Assembly.GetType("ExusiAI.Plugin.ClassIsland.ClassIslandHost", throwOnError: true)!;
            activeHost = (IDisposable)Activator.CreateInstance(hostType, BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, args: [data, package], culture: null)!;
            field.SetValue(plugin, activeHost);
        };
        using var coordinator = new WpfExtensionCoordinator(runtime, navigation, tray, theme,
            NullLogger<WpfExtensionCoordinator>.Instance);
        coordinator.Start();
        await runtime.DiscoverAsync(Path.GetDirectoryName(package)!);
        await runtime.StartAsync(runtime.Entries.Select(entry => entry.Package.Manifest.Id).Where(packageId => packageId != id));

        void RequireState(PackageState state)
        {
            var entry = runtime.Entries.Single(item => item.Package.Manifest.Id == id);
            if (entry.State != state)
                throw new InvalidOperationException($"ClassIsland lifecycle expected {state}, got {entry.State}: {entry.FailureMessage}");
            var running = state == PackageState.Running;
            if ((entry.Instance is not null) != running || navigation.Pages.Count != (running ? 1 : 0) ||
                tray.Menus.Count != (running ? 1 : 0) || (running && tray.Menus[0].Commands.Count != 6))
                throw new InvalidOperationException("ClassIsland lifecycle left incorrect plugin/page/tray registrations.");
        }
        object? Read(string property) => hostType!.GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(activeHost);
        Task Command(string command) => tray.Menus.Single().Commands.Single(item => item.Id == command).ExecuteAsync(CancellationToken.None);
        void RequireDormant()
        {
            if (Read("IsStopped") is not true || Read("RequiresRestart") is not false)
                throw new InvalidOperationException("ClassIsland activation unexpectedly initialized or stopped its native runtime.");
        }
        void RequireResumedHidden()
        {
            if (Read("IsStopped") is not false || Read("RequiresRestart") is not false || Read("IsVisible") is not false)
                throw new InvalidOperationException("ClassIsland resume did not retain a healthy hidden native runtime.");
        }

        RequireState(PackageState.Running);
        RequireDormant();
        await Task.Run(() => runtime.SetEnabledAsync(id, false));
        RequireState(PackageState.Disabled);
        Log("PASS: dormant plugin disabled; page and tray removed.");

        Sample(0, "dormant");

        async Task ObserveIdleAsync(int cycle, IntPtr window, bool visible)
        {
            for (var sample = 0; sample < 5; sample++)
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                if (!IsWindow(window) || IsWindowVisible(window) != visible || Read("IsVisible") is not bool status || status != visible)
                    throw new InvalidOperationException($"ClassIsland did not retain visibility={visible} during the idle probe.");
                Sample(cycle, visible ? "visible_idle" : "hidden_idle");
            }
        }

        Assembly? sharedEvents = null;
        object? sharedWindowIcon = null;
        IntPtr[]? finalWindows = null;
        for (var cycle = 1; cycle <= cycles; cycle++)
        {
            Log($"Starting cycle {cycle}.");
            var phaseTimer = Stopwatch.StartNew();
            await runtime.SetEnabledAsync(id, true);
            RequireState(PackageState.Running);
            if (cycle == 1) RequireDormant();
            else
            {
                RequireResumedHidden();
                Sample(cycle, "resumed_hidden", phaseTimer.Elapsed.TotalMilliseconds);
            }
            await Command("show");
            var entryPoint = (Type)hostType!.GetField("entryPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(activeHost)!;
            var events = AssemblyLoadContext.GetLoadContext(entryPoint.Assembly)!.LoadFromAssemblyName(new AssemblyName("Microsoft.Win32.SystemEvents"));
            if (events.GetName().Version?.Major != 9 || (sharedEvents is not null && !ReferenceEquals(sharedEvents, events)))
                throw new InvalidOperationException($"Cycle {cycle}: SystemEvents was not shared across service sessions.");
            sharedEvents = events;
            if ((int)InvokeEntryPoint(hostType!, activeHost!, "GetEmbeddedServiceGeneration")! != cycle ||
                InvokeEntryPoint(hostType!, activeHost!, "GetEmbeddedServiceHostActive") is not true)
                throw new InvalidOperationException($"Cycle {cycle}: managed service host was not rebuilt exactly once.");
            var window = new IntPtr((long)InvokeEntryPoint(hostType!, activeHost!, "GetEmbeddedWindowHandle")!);
            if (!IsWindowVisible(window) || Read("IsVisible") is not true)
                throw new InvalidOperationException($"Cycle {cycle}: re-enabled island is not visible.");
            Sample(cycle, "visible", phaseTimer.Elapsed.TotalMilliseconds);
            if (soak && cycle == cycles)
                await ObserveIdleAsync(cycle, window, visible: true);
            var render = await CaptureNativeWindowAsync(window, Path.Combine(outputDirectory, $"classisland-cycle-{cycle}.png"));
            if (!render.HasVisibleContent)
                throw new InvalidOperationException($"Cycle {cycle}: re-enabled island did not render: {render}");
            Sample(cycle, "rendered");
            // Embedded startup suppresses the splash, so this persisted native
            // preference is a harmless marker for checking session reload.
            NativeUi(hostType!, activeHost!, app =>
            {
                var settings = app.GetType().GetProperty("Settings")!.GetValue(app)!;
                var marker = settings.GetType().GetProperty("IsSplashEnabled")!;
                if (cycle > 1 && (bool)marker.GetValue(settings)! != ((cycle - 1) % 2 == 0))
                    throw new InvalidOperationException($"Cycle {cycle}: native settings were not reloaded.");
                marker.SetValue(settings, cycle % 2 == 0);
            });
            await Command("hide");
            if (soak && cycle == cycles)
                await ObserveIdleAsync(cycle, window, visible: false);
            await Command("settings");
            if (IsWindowVisible(window) || Read("IsVisible") is not false)
                throw new InvalidOperationException($"Cycle {cycle}: settings revealed a hidden island.");
            var windows = await GetNativeWindowsAsync(hostType!, activeHost!);
            Sample(cycle, "settings_open");
            object? settingsSourceIcon = null;
            NativeUi(hostType!, activeHost!, app =>
            {
                VerifyNoNativeTrayIcon(entryPoint);
                (sharedWindowIcon, settingsSourceIcon) = ReadWindowIcons(app, sharedWindowIcon);
                if (ReadWin32IconHandle(settingsSourceIcon) == IntPtr.Zero)
                    throw new InvalidOperationException("ClassIsland settings have no native source icon.");
            });
            var services = GetEmbeddedServices(entryPoint);
            var core = SharedAssembly(entryPoint, "ClassIsland.Core");
            var exactTime = services.GetService(core.GetType("ClassIsland.Core.Abstractions.Services.IExactTimeService", throwOnError: true)!)!;
            var windowRule = services.GetService(core.GetType("ClassIsland.Core.Abstractions.Services.IWindowRuleService", throwOnError: true)!)!;
            var platform = GetPlatformService(entryPoint);
            if (CountPlatformHandlers(platform) != 1)
                throw new InvalidOperationException($"Cycle {cycle}: expected exactly one process-wide foreground-window callback.");
            SessionProbe? sessionProbe = null;
            NativeUi(hostType!, activeHost!, app => sessionProbe = PrepareSessionProbe(entryPoint, app));
            await Task.Delay(100);
            NativeUi(hostType!, activeHost!, _ => VerifyActiveSessionProbe(sessionProbe!));
            var oldShow = tray.Menus.Single().Commands.Single(command => command.Id == "show");
            phaseTimer.Restart();
            await Task.Run(() => runtime.SetEnabledAsync(id, false));
            var disableMs = phaseTimer.Elapsed.TotalMilliseconds;
            RequireState(PackageState.Disabled);
            VerifySuspended(hostType!, activeHost!, windows);
            if (ReadWin32IconHandle(settingsSourceIcon!) != IntPtr.Zero)
                throw new InvalidOperationException("ClassIsland settings retained a native source icon after closing.");
            Sample(cycle, "suspended");
            VerifySuspendedServices(entryPoint, exactTime, windowRule, platform);
            await Task.Delay(650);
            NativeUi(hostType!, activeHost!, _ => VerifyStoppedSessionProbe(sessionProbe!));
            if (InvokeEntryPoint(hostType!, activeHost!, "GetEmbeddedServiceHostActive") is not false)
                throw new InvalidOperationException($"Cycle {cycle}: managed service host survived disable.");
            if (tray.IsRegistered(id, oldShow))
                throw new InvalidOperationException($"Cycle {cycle}: old tray command remains registered.");
            using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "Settings.json")));
            var savedVisible = saved.RootElement.GetProperty("IsMainWindowVisible").GetBoolean();
            var savedSplash = saved.RootElement.GetProperty("IsSplashEnabled").GetBoolean();
            if (savedVisible || savedSplash != (cycle % 2 == 0))
                throw new InvalidOperationException($"Cycle {cycle}: suspension did not preserve native settings: visible={savedVisible}; splash={savedSplash}; expected splash={cycle % 2 == 0}.");
            finalWindows = windows;
            Sample(cycle, "disabled", disableMs);
            Log($"PASS: cycle {cycle}; rendered {render}; stopped service host, cron workers, in-flight action and hang timer; completed async exit actions; rejected stale settings writes; merged plugin action menu; destroyed {windows.Length} windows; settings saved; page and tray removed.");
        }

        await runtime.SetEnabledAsync(id, true);
        RequireResumedHidden();
        var importSource = Path.Combine(outputDirectory, "ImportSource");
        Directory.CreateDirectory(importSource);
        File.Copy(Path.Combine(data, "Settings.json"), Path.Combine(importSource, "Settings.json"));
        async Task<string> Import(string source) => await (Task<string>)hostType!.GetMethod("ImportDataAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(activeHost, [source, CancellationToken.None])!;
        var generation = (int)InvokeEntryPoint(hostType!, activeHost!, "GetEmbeddedServiceGeneration")!;
        File.WriteAllText(Path.Combine(importSource, "Settings.json"), "[]");
        try
        {
            await Import(importSource);
            throw new InvalidOperationException("Invalid import was accepted.");
        }
        catch (InvalidDataException) { }
        RequireResumedHidden();
        if ((int)InvokeEntryPoint(hostType!, activeHost!, "GetEmbeddedServiceGeneration")! != generation)
            throw new InvalidOperationException("Invalid import interrupted the active session.");
        File.Copy(Path.Combine(data, "Settings.json"), Path.Combine(importSource, "Settings.json"), overwrite: true);
        var backup = await Import(importSource);
        RequireResumedHidden();
        Sample(cycles, "imported");
        if (!File.Exists(Path.Combine(backup, "Settings.json")) ||
            (int)InvokeEntryPoint(hostType!, activeHost!, "GetEmbeddedServiceGeneration")! != generation + 1)
            throw new InvalidOperationException("Hidden import did not back up data and rebuild the session.");
        await Command("show");
        await Import(backup);
        if (Read("RequiresRestart") is not false || Read("IsVisible") is not true ||
            (int)InvokeEntryPoint(hostType!, activeHost!, "GetEmbeddedServiceGeneration")! != generation + 2)
            throw new InvalidOperationException("Backup restore did not rebuild the session and restore visibility.");
        var restoredWindow = new IntPtr((long)InvokeEntryPoint(hostType!, activeHost!, "GetEmbeddedWindowHandle")!);
        var restoredRender = await CaptureNativeWindowAsync(restoredWindow, Path.Combine(outputDirectory, "classisland-restored.png"));
        if (!restoredRender.HasVisibleContent)
            throw new InvalidOperationException("Restored session did not render.");
        Sample(cycles, "restored");
        await Command("hide");
        await Command("settings");
        finalWindows = await GetNativeWindowsAsync(hostType!, activeHost!);
        await runtime.SetEnabledAsync(id, false);
        RequireState(PackageState.Disabled);
        VerifySuspended(hostType!, activeHost!, finalWindows);
        Log("PASS: hidden import and visible backup restore rebuilt sessions without restarting Avalonia.");

        await runtime.SetEnabledAsync(id, true);
        hostType!.GetMethod("SetFollowHostTheme", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(activeHost, [true]);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseStop = new ManualResetEventSlim();
        EventHandler pauseStop = (_, _) =>
        {
            stopping.TrySetResult();
            if (!releaseStop.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Import stop probe was not released.");
        };
        NativeUi(hostType!, activeHost!, app => app.GetType().GetEvent("AppStopping")!.AddEventHandler(app, pauseStop));
        var concurrentImport = Import(importSource);
        await stopping.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task? disposal = null;
        try
        {
            await Task.Run(() => hostType.GetMethod("ApplyTheme", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(activeHost, [new HostTheme(true, "#4488CC")])).WaitAsync(TimeSpan.FromSeconds(3));
            disposal = Task.Run(() => activeHost!.Dispose());
            await Task.Delay(100);
            if (disposal.IsCompleted)
                throw new InvalidOperationException("Final disposal bypassed the import transition.");
        }
        finally { releaseStop.Set(); }
        await concurrentImport;
        await disposal!.WaitAsync(TimeSpan.FromSeconds(20));
        if (Read("IsStopped") is not true)
            throw new InvalidOperationException("Concurrent import and final disposal left Avalonia running.");
        Log("PASS: queued theme callback during suspension; final disposal waited for import and stopped Avalonia.");

        await runtime.StopAsync();
        RequireState(PackageState.Stopped);
        VerifyStopped(hostType!, activeHost!, finalWindows!);
        Sample(cycles, "stopped");
        // Keep the normal lifecycle samples independent of forced collection.
        await Task.Run(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        });
        Sample(cycles, "after_finalizers");
        Log("PASS: final shutdown stopped the retained Avalonia runtime.");
    }

    private static void NativeUi(Type hostType, IDisposable host, Action<object> action)
    {
        var entry = (Type)hostType.GetField("entryPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var dispatcher = AssemblyLoadContext.GetLoadContext(entry.Assembly)!
            .LoadFromAssemblyName(new AssemblyName("Avalonia.Base")).GetType("Avalonia.Threading.Dispatcher")!;
        Action invoke = () => action(entry.GetField("embeddedApp", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!);
        dispatcher.GetMethod("Invoke", [typeof(Action)])!.Invoke(dispatcher.GetProperty("UIThread")!.GetValue(null), [invoke]);
    }

    private static async Task<IntPtr[]> GetNativeWindowsAsync(Type hostType, IDisposable host)
    {
        var handles = new List<IntPtr>();
        // Settings navigation can return before the settings window appears.
        // The hidden owner window alone is insufficient.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            handles.Clear();
            var hasSettingsWindow = false;
            NativeUi(hostType, host, app =>
            {
                var lifetime = app.GetType().GetProperty("DesktopLifetime")!.GetValue(app)!;
                foreach (var window in (IEnumerable)lifetime.GetType().GetProperty("Windows")!.GetValue(lifetime)!)
                {
                    var handle = window!.GetType().GetMethod("TryGetPlatformHandle")!.Invoke(window, null)!;
                    handles.Add((IntPtr)handle.GetType().GetProperty("Handle")!.GetValue(handle)!);
                    hasSettingsWindow |= window.GetType().Name == "SettingsWindowNew";
                }
            });
            if (hasSettingsWindow && handles.Count >= 2 && handles.All(handle => handle != IntPtr.Zero))
                return handles.ToArray();
            await Task.Delay(100);
        }
        throw new InvalidOperationException("ClassIsland lifecycle expected native island and settings windows.");
    }

    private static void VerifyStopped(Type type, IDisposable host, IntPtr[] windows)
    {
        if (type.GetProperty("IsStopped", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) is not true ||
            windows.Any(IsWindow))
            throw new InvalidOperationException("ClassIsland shutdown left a native thread or window alive.");
        var entry = (Type)type.GetField("entryPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        if (entry.GetProperty("IsEmbeddedRunning")!.GetValue(null) is not false)
            throw new InvalidOperationException("ClassIsland shutdown retained its embedded app.");
    }

    private static void VerifySuspended(Type type, IDisposable host, IntPtr[] windows)
    {
        if (type.GetProperty("IsStopped", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) is true ||
            type.GetProperty("RequiresRestart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) is true ||
            windows.Any(IsWindow))
            throw new InvalidOperationException("ClassIsland suspension stopped Avalonia or retained a native window.");
        var entry = (Type)type.GetField("entryPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        if (entry.GetProperty("IsEmbeddedRunning")!.GetValue(null) is not true)
            throw new InvalidOperationException("ClassIsland suspension released its process-scoped Avalonia application.");
    }

    private static Assembly SharedAssembly(Type entryPoint, string name) =>
        AssemblyLoadContext.GetLoadContext(entryPoint.Assembly)!.LoadFromAssemblyName(new AssemblyName(name));

    private static (object MainIcon, object SettingsSourceIcon) ReadWindowIcons(object app, object? previousIcon)
    {
        var main = app.GetType().GetProperty("MainWindow")!.GetValue(app)!;
        var lifetime = app.GetType().GetProperty("DesktopLifetime")!.GetValue(app)!;
        var windows = (IEnumerable)lifetime.GetType().GetProperty("Windows")!.GetValue(lifetime)!;
        var settings = windows.Cast<object>().Single(window => window.GetType().Name == "SettingsWindowNew");
        // FluentAvalonia's AppWindow.Icon is the title-bar image. Use the
        // underlying Avalonia Window property to check the native source.
        var iconProperty = main.GetType().GetProperty("Icon")!;
        var mainIcon = iconProperty.GetValue(main);
        if (mainIcon is null || (previousIcon is not null && !ReferenceEquals(mainIcon, previousIcon)))
            throw new InvalidOperationException("ClassIsland reallocated the information-island source icon.");
        var settingsIcon = iconProperty.GetValue(settings)!;
        var platformIcon = settingsIcon.GetType().GetProperty("PlatformImpl", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(settingsIcon)!;
        var nativeIcon = platformIcon.GetType().GetField("_bigIcon", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(platformIcon)!;
        return (mainIcon, nativeIcon);
    }

    private static IntPtr ReadWin32IconHandle(object icon) =>
        (IntPtr)icon.GetType().GetProperty("Handle")!.GetValue(icon)!;

    private static void VerifyNoNativeTrayIcon(Type entryPoint)
    {
        var tray = SharedAssembly(entryPoint, "Avalonia.Win32").GetType("Avalonia.Win32.TrayIconImpl", throwOnError: true)!;
        var registrations = (IDictionary)tray.GetField("s_trayIcons", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        if (registrations.Count != 0)
            throw new InvalidOperationException($"The embedded ClassIsland runtime created {registrations.Count} native tray registrations.");
    }

    private static IServiceProvider GetEmbeddedServices(Type entryPoint)
    {
        var appHost = SharedAssembly(entryPoint, "ClassIsland.Shared").GetType("ClassIsland.Shared.IAppHost", throwOnError: true)!;
        var host = appHost.GetField("Host", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        return (IServiceProvider)host.GetType().GetProperty("Services")!.GetValue(host)!;
    }

    private static object GetPlatformService(Type entryPoint)
    {
        var services = SharedAssembly(entryPoint, "ClassIsland.Platforms.Abstractions")
            .GetType("ClassIsland.Platforms.Abstraction.PlatformServices", throwOnError: true)!;
        return services.GetProperty("WindowPlatformService", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
    }

    private static int CountPlatformHandlers(object platform)
    {
        var handlers = (ICollection)platform.GetType()
            .GetField("_changedEventHandlers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(platform)!;
        return handlers.Count;
    }

    private static void VerifySuspendedServices(Type entryPoint, object exactTime, object windowRule, object platform)
    {
        var timer = exactTime.GetType().GetProperty("UpdateTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(exactTime)!;
        if (timer.GetType().GetProperty("IsEnabled")!.GetValue(timer) is true)
            throw new InvalidOperationException("ClassIsland suspension left the exact-time synchronization timer enabled.");

        var platformServices = SharedAssembly(entryPoint, "ClassIsland.Platforms.Abstractions")
            .GetType("ClassIsland.Platforms.Abstraction.PlatformServices", throwOnError: true)!;
        var systemEvents = platformServices.GetProperty("SystemEventsService", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        if (HasHandlerTarget(systemEvents, "TimeChanged", exactTime))
            throw new InvalidOperationException("ClassIsland suspension retained the process-wide system time subscription.");

        if (CountPlatformHandlers(platform) != 0)
            throw new InvalidOperationException("ClassIsland suspension retained a process-wide foreground-window callback.");

        var ruleset = SharedAssembly(entryPoint, "ClassIsland.Core")
            .GetType("ClassIsland.Core.Abstractions.Services.IRulesetService", throwOnError: true)!;
        var rules = (IDictionary)ruleset.GetProperty("Rules", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        foreach (string id in rules.Keys)
        {
            var handle = rules[id]!.GetType().GetField("Handle", BindingFlags.Instance | BindingFlags.Public)!.GetValue(rules[id]) as Delegate;
            if (handle is null) continue;
            if (id.StartsWith("classisland.windows.", StringComparison.Ordinal))
            {
                if (handle.GetInvocationList().Any(item => ReferenceEquals(item.Target, windowRule)))
                    throw new InvalidOperationException($"ClassIsland suspension retained rule handler {id}.");
                continue;
            }

            if ((id.StartsWith("classisland.lessons.", StringComparison.Ordinal) ||
                 id.StartsWith("classisland.weather.", StringComparison.Ordinal)) &&
                handle.GetInvocationList().Any(item => item.Target?.GetType().Name is "LessonsService" or "WeatherService"))
                throw new InvalidOperationException($"ClassIsland suspension retained rule handler {id}.");
        }
    }

    private static bool HasHandlerTarget(object source, string eventName, object target)
    {
        for (var type = source.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field?.GetValue(source) is Delegate handler)
                return handler.GetInvocationList().Any(item => ReferenceEquals(item.Target, target));
        }
        return false;
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
}
