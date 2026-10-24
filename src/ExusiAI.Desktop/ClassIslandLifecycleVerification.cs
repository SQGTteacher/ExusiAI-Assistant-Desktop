using System.Collections;
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
    private static async Task VerifyLifecycleAsync(string package, string outputDirectory)
    {
        const string id = "exusiai.classisland";
        var data = Path.Combine(outputDirectory, "LifecycleData");
        var report = Path.Combine(outputDirectory, "lifecycle.log");
        void Log(string message) => File.AppendAllText(report, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        await using var runtime = new ExtensionRuntime(
            new(new(), new(), NullLogger<PackageDiscoveryService>.Instance), NullLogger<ExtensionRuntime>.Instance);
        var navigation = new WpfNavigationRegistry();
        var tray = new WpfTrayRegistry();
        using var theme = new ThemeService();
        IDisposable? activeHost = null;
        object? activePlugin = null;
        Type? hostType = null;
        // Replace the still-dormant host on first initialization, before StartAsync
        // can use the user's startup preference. The real manager owns all later
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

        Assembly? sharedEvents = null;
        IntPtr[]? finalWindows = null;
        for (var cycle = 1; cycle <= 3; cycle++)
        {
            Log($"Starting cycle {cycle}.");
            await runtime.SetEnabledAsync(id, true);
            RequireState(PackageState.Running);
            if (cycle == 1) RequireDormant();
            else RequireResumedHidden();
            await Command("show");
            var entryPoint = (Type)hostType!.GetField("entryPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(activeHost)!;
            var events = AssemblyLoadContext.GetLoadContext(entryPoint.Assembly)!.LoadFromAssemblyName(new AssemblyName("Microsoft.Win32.SystemEvents"));
            if (events.GetName().Version?.Major != 9 || (sharedEvents is not null && !ReferenceEquals(sharedEvents, events)))
                throw new InvalidOperationException($"Cycle {cycle}: SystemEvents was not shared across resumes.");
            sharedEvents = events;
            var window = new IntPtr((long)InvokeEntryPoint(hostType!, activeHost!, "GetEmbeddedWindowHandle")!);
            if (!IsWindowVisible(window) || Read("IsVisible") is not true)
                throw new InvalidOperationException($"Cycle {cycle}: re-enabled island is not visible.");
            var render = await CaptureNativeWindowAsync(window, Path.Combine(outputDirectory, $"classisland-cycle-{cycle}.png"));
            if (!render.HasVisibleContent)
                throw new InvalidOperationException($"Cycle {cycle}: re-enabled island did not render: {render}");
            // Embedded startup suppresses the splash, so this native preference
            // is a harmless marker for checking retained state across resumes.
            NativeUi(hostType!, activeHost!, app =>
            {
                var settings = app.GetType().GetProperty("Settings")!.GetValue(app)!;
                var marker = settings.GetType().GetProperty("IsSplashEnabled")!;
                if (cycle > 1 && (bool)marker.GetValue(settings)! != ((cycle - 1) % 2 == 0))
                    throw new InvalidOperationException($"Cycle {cycle}: native settings were not retained.");
                marker.SetValue(settings, cycle % 2 == 0);
            });
            await Command("hide");
            await Command("settings");
            if (IsWindowVisible(window) || Read("IsVisible") is not false)
                throw new InvalidOperationException($"Cycle {cycle}: settings revealed a hidden island.");
            var windows = await GetNativeWindowsAsync(hostType!, activeHost!);
            var oldShow = tray.Menus.Single().Commands.Single(command => command.Id == "show");
            await Task.Run(() => runtime.SetEnabledAsync(id, false));
            RequireState(PackageState.Disabled);
            VerifySuspended(hostType!, activeHost!, windows);
            if (tray.IsRegistered(id, oldShow))
                throw new InvalidOperationException($"Cycle {cycle}: old tray command remains registered.");
            finalWindows = windows;
            Log($"PASS: cycle {cycle}; rendered {render}; suspended {windows.Length} windows; page and tray removed.");
        }

        await runtime.StopAsync();
        RequireState(PackageState.Stopped);
        VerifyStopped(hostType!, activeHost!, finalWindows!);
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "Settings.json")));
        if (saved.RootElement.GetProperty("IsMainWindowVisible").GetBoolean() ||
            saved.RootElement.GetProperty("IsSplashEnabled").GetBoolean())
            throw new InvalidOperationException("Final shutdown did not preserve native settings.");
        Log($"PASS: final shutdown destroyed {finalWindows!.Length} windows and saved settings.");
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
        // Upstream settings navigation can finish asynchronously after the command
        // returns. Wait for both real windows rather than accepting a no-op navigation.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            handles.Clear();
            NativeUi(hostType, host, app =>
            {
                var lifetime = app.GetType().GetProperty("DesktopLifetime")!.GetValue(app)!;
                foreach (var window in (IEnumerable)lifetime.GetType().GetProperty("Windows")!.GetValue(lifetime)!)
                {
                    var handle = window!.GetType().GetMethod("TryGetPlatformHandle")!.Invoke(window, null)!;
                    handles.Add((IntPtr)handle.GetType().GetProperty("Handle")!.GetValue(handle)!);
                }
            });
            if (handles.Count >= 2 && handles.All(handle => handle != IntPtr.Zero)) return handles.ToArray();
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
            windows.Any(window => !IsWindow(window) || IsWindowVisible(window)))
            throw new InvalidOperationException("ClassIsland suspension stopped its runtime or left a native window visible.");
        var entry = (Type)type.GetField("entryPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        if (entry.GetProperty("IsEmbeddedRunning")!.GetValue(null) is not true)
            throw new InvalidOperationException("ClassIsland suspension released its embedded app.");
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
}
