using System.IO;
using System.Diagnostics;
using System.Reflection;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.ClassIsland;

/// <summary>
/// Starts the original ClassIsland Avalonia application inside the ExusiAI process.
/// Its assemblies must be built from the checked-in source and packaged locally.
/// </summary>
internal sealed class ClassIslandHost : IDisposable
{
    private readonly string dataDirectory;
    private readonly string nativeDirectory;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object lifecycleGate = new();
    private Thread? thread;
    private Type? entryPoint;
    private HostTheme? pendingTheme;
    private readonly object themeGate = new();
    private readonly string themePreferencePath;
    private bool followHostTheme;
    private volatile bool disposed;
    private int stopRequested;
    internal string? LastStartupError { get; private set; }

    internal ClassIslandHost(string dataDirectory, string packageDirectory)
    {
        this.dataDirectory = dataDirectory;
        themePreferencePath = Path.Combine(dataDirectory, "ExusiAIHostTheme.json");
        try
        {
            if (File.Exists(themePreferencePath))
                followHostTheme = System.Text.Json.JsonSerializer.Deserialize<bool>(File.ReadAllText(themePreferencePath));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Trace.TraceWarning("ClassIsland host theme preference could not be read: {0}", error.Message);
        }
        nativeDirectory = Path.Combine(Path.GetFullPath(packageDirectory), "NativeClassIsland");
    }

    internal bool IsAvailable => File.Exists(Path.Combine(nativeDirectory, "ClassIsland.Desktop.dll")) &&
        File.Exists(Path.Combine(nativeDirectory, "ClassIsland.dll"));

    internal string DataDirectory => dataDirectory;
    internal bool IsStopped => thread is null || !thread.IsAlive;

    internal bool IsVisible
    {
        get
        {
            lock (lifecycleGate)
            {
                if (disposed || thread is not { IsAlive: true } || !ready.Task.IsCompletedSuccessfully || !ready.Task.Result)
                    return false;
                try { return InvokeControl("GetEmbeddedVisible") is true; }
                catch (Exception exception)
                {
                    Trace.TraceWarning("ClassIsland visibility could not be read: {0}", exception.GetBaseException().Message);
                    return false;
                }
            }
        }
    }

    internal bool Start() => EnsureStarted(showIsland: true);

    private bool EnsureStarted(bool showIsland)
    {
        Thread runningThread;
        lock (lifecycleGate)
        {
            if (disposed)
            {
                LastStartupError = "ClassIsland 已停止，需要重新启动 ExusiAI。";
                return false;
            }
            if (!IsAvailable)
            {
                LastStartupError = $"Native ClassIsland assemblies are missing from {nativeDirectory}.";
                return false;
            }
            if (thread is null)
            {
                try
                {
                    thread = new Thread(Run) { IsBackground = true, Name = "ClassIsland original Avalonia host" };
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                }
                catch (Exception exception)
                {
                    LastStartupError = exception.GetBaseException().Message;
                    ready.TrySetResult(false);
                    disposed = true;
                    thread = null;
                    return false;
                }
            }
            runningThread = thread;
        }
        if (!ready.Task.Wait(TimeSpan.FromSeconds(30)))
        {
            LastStartupError = "Native ClassIsland did not finish starting within 30 seconds.";
            return false;
        }
        lock (lifecycleGate)
        {
            if (disposed || !ready.Task.Result || !runningThread.IsAlive)
            {
                LastStartupError ??= "Native ClassIsland stopped before its window was ready.";
                return false;
            }
            try
            {
                if (showIsland) InvokeControl("SetEmbeddedVisible", [true]);
                LastStartupError = null;
                return true;
            }
            catch (Exception exception)
            {
                LastStartupError = exception.GetBaseException().Message;
                Trace.TraceError("Embedded ClassIsland command failed: {0}", exception);
                return false;
            }
        }
    }

    internal void Hide()
    {
        lock (lifecycleGate)
        {
            if (disposed) return;
            if (ready.Task.IsCompletedSuccessfully && ready.Task.Result)
                InvokeControl("SetEmbeddedVisible", [false]);
        }
    }

    internal bool OpenSettings() => OpenPage("settings");

    internal bool OpenPage(string page)
    {
        if (page is not ("settings" or "profile" or "edit" or "class-swap"))
            throw new ArgumentException("Unsupported ClassIsland page.", nameof(page));
        if (!EnsureStarted(showIsland: false)) return false;
        lock (lifecycleGate)
        {
            if (disposed) return false;
            try
            {
                InvokeControl("OpenEmbeddedPage", [page]);
                return true;
            }
            catch (Exception exception)
            {
                LastStartupError = exception.GetBaseException().Message;
                return false;
            }
        }
    }

    private object? InvokeControl(string method, object?[]? arguments = null) =>
        (entryPoint?.GetMethod(method)
            ?? throw new MissingMethodException($"ClassIsland.Desktop.Program.{method}"))
        .Invoke(null, arguments);

    internal bool FollowHostTheme { get { lock (themeGate) return followHostTheme; } }

    internal void SetFollowHostTheme(bool follow)
    {
        lock (themeGate)
        {
            // Persist first: a failed save must not leave the UI reporting a saved choice.
            Directory.CreateDirectory(dataDirectory);
            var temporary = themePreferencePath + ".tmp";
            File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(follow));
            File.Move(temporary, themePreferencePath, overwrite: true);
            followHostTheme = follow;
            if (!disposed && ready.Task.IsCompletedSuccessfully && ready.Task.Result)
                InvokeTheme();
        }
    }

    internal void ApplyTheme(HostTheme theme)
    {
        lock (themeGate)
        {
            pendingTheme = theme;
            if (!disposed && followHostTheme && ready.Task.IsCompletedSuccessfully && ready.Task.Result)
                InvokeTheme();
        }
    }

    // Called under themeGate, so startup and shell updates dispatch in the same order.
    private void InvokeTheme()
    {
        try
        {
            var method = entryPoint?.GetMethod("SetEmbeddedTheme")
                ?? throw new MissingMethodException("ClassIsland.Desktop.Program.SetEmbeddedTheme");
            if (followHostTheme && pendingTheme is { } theme)
                method.Invoke(null, [theme.IsDark, theme.Accent]);
            else
                method.Invoke(null, [null, null]);
        }
        catch (Exception exception)
        {
            Trace.TraceError("Embedded ClassIsland could not apply the host theme: {0}", exception);
        }
    }

    private void Run()
    {
        try
        {
            var desktopPath = Path.Combine(nativeDirectory, "ClassIsland.Desktop.dll");
            // WPF has already loaded System.Text.Json/SystemEvents 8.x. The upstream
            // runtime must bind its own versions before default-context fallback.
            var runtimeContext = new ClassIslandRuntimeLoadContext(desktopPath);
            using var reflectionScope = runtimeContext.EnterContextualReflection();
            var desktop = runtimeContext.LoadFromAssemblyPath(desktopPath);
            var application = runtimeContext.LoadFromAssemblyPath(Path.Combine(nativeDirectory, "ClassIsland.dll"));
            entryPoint = desktop.GetType("ClassIsland.Desktop.Program", throwOnError: true);
            var optionsType = application.GetType("ClassIsland.EmbeddedHostOptions", throwOnError: true)!;
            var options = Activator.CreateInstance(optionsType, dataDirectory, nativeDirectory)!;
            Action<object> created = app =>
            {
                var started = app.GetType().GetEvent("AppStarted")
                    ?? throw new MissingMemberException("ClassIsland.App.AppStarted");
                started.AddEventHandler(app, new EventHandler((_, _) =>
                {
                    lock (themeGate)
                    {
                        ready.TrySetResult(true);
                        if (followHostTheme) InvokeTheme();
                    }
                    if (Volatile.Read(ref stopRequested) != 0)
                        entryPoint?.GetMethod("StopEmbedded")?.Invoke(null, null);
                }));
                app.GetType().GetEvent("EmbeddedRestartRequested")?.AddEventHandler(app,
                    new EventHandler((_, _) =>
                    {
                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        dispatcher?.BeginInvoke((Action)(() => System.Windows.MessageBox.Show(
                            "ClassIsland 的这项设置需要重启 ExusiAI 后生效。",
                            "需要重启 ExusiAI", System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Information)));
                    }));
            };
            entryPoint!.GetMethod("RunEmbedded")!.Invoke(null, [options, created]);
            if (!ready.Task.IsCompleted)
            {
                LastStartupError = "Native ClassIsland exited during startup.";
                ready.TrySetResult(false);
            }
        }
        catch (Exception exception)
        {
            LastStartupError = exception.GetBaseException().Message;
            Trace.TraceError("Embedded ClassIsland failed: {0}", exception);
            ready.TrySetResult(false);
        }
    }

    public void Dispose()
    {
        Thread? stoppingThread;
        lock (lifecycleGate)
        {
            if (disposed) return;
            disposed = true;
            Interlocked.Exchange(ref stopRequested, 1);
            stoppingThread = thread;
            if (entryPoint is not null)
                try { InvokeControl("StopEmbedded"); }
                catch (TargetInvocationException) { /* The dispatcher may already be gone. */ }
        }
        // Never join while holding the gate: concurrent commands must observe stopped state.
        if (stoppingThread is not null && !stoppingThread.Join(TimeSpan.FromSeconds(15)))
            Trace.TraceWarning("Embedded ClassIsland did not stop within fifteen seconds.");
        // Avalonia owns process-wide native state; its runtime context is intentionally not collectible.
    }
}
