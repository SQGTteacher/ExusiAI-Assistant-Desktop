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
    private readonly string startupPreferencePath;
    private bool showOnHostStartup;
    private volatile bool disposed;
    private int stopRequested;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private int runtimeExited;
    internal event EventHandler? StateChanged;
    internal bool IsBusy => operationGate.CurrentCount == 0;
    internal string? LastStartupError { get; private set; }

    private void NotifyStateChanged()
    {
        // Subscribers must marshal to their dispatcher; never wait for WPF from Avalonia.
        foreach (EventHandler handler in StateChanged?.GetInvocationList() ?? [])
            try { handler(this, EventArgs.Empty); }
            catch (Exception error) { Trace.TraceWarning("ClassIsland state subscriber failed: {0}", error.Message); }
    }

    private bool Command(Func<bool> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!operationGate.Wait(0)) return false;
        NotifyStateChanged();
        try { return action(); }
        finally { operationGate.Release(); NotifyStateChanged(); }
    }

    internal async Task<string> ImportDataAsync(string source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!operationGate.Wait(0))
            throw new InvalidOperationException("ClassIsland 正在执行其他操作，请稍后再试。");
        NotifyStateChanged();
        try
        {
            return await ClassIslandDataImporter.ImportAsync(source, dataDirectory,
                Dispose, () => IsStopped, cancellationToken).ConfigureAwait(false);
        }
        finally { operationGate.Release(); NotifyStateChanged(); }
    }

    internal ClassIslandHost(string dataDirectory, string packageDirectory)
    {
        this.dataDirectory = dataDirectory;
        startupPreferencePath = Path.Combine(dataDirectory, "ExusiAIStartup.json");
        try
        {
            if (File.Exists(startupPreferencePath))
                showOnHostStartup = System.Text.Json.JsonSerializer.Deserialize<bool>(File.ReadAllText(startupPreferencePath));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Trace.TraceWarning("ClassIsland startup preference could not be read: {0}", error.Message);
        }
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
    internal bool IsStopped => thread is null || Volatile.Read(ref runtimeExited) != 0 || !thread.IsAlive;
    internal bool RequiresRestart => disposed || Volatile.Read(ref stopRequested) != 0 || (thread is not null && IsStopped);
    internal bool ShowOnHostStartup { get { lock (lifecycleGate) return showOnHostStartup; } }

    internal void SetShowOnHostStartup(bool show) => Preference(() => SetShowOnHostStartupCore(show));

    private void Preference(Action action)
    {
        if (!Command(() => { action(); return true; }))
            throw new InvalidOperationException("ClassIsland 正在执行其他操作，请稍后再试。");
    }

    private void SetShowOnHostStartupCore(bool show)
    {
        lock (lifecycleGate)
        {
            Directory.CreateDirectory(dataDirectory);
            var temporary = startupPreferencePath + ".tmp";
            File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(show));
            File.Move(temporary, startupPreferencePath, overwrite: true);
            showOnHostStartup = show;
        }
    }

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

    internal bool Start() => StartWithCancellation(CancellationToken.None);

    internal bool StartWithCancellation(CancellationToken cancellationToken) => Command(() => EnsureStarted(true, cancellationToken), cancellationToken);

    private bool EnsureStarted(bool showIsland, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var cancellation = cancellationToken.Register(RequestStop);
        Thread runningThread;
        lock (lifecycleGate)
        {
            if (disposed || Volatile.Read(ref stopRequested) != 0)
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
        if (!ready.Task.Wait(TimeSpan.FromSeconds(30), cancellationToken))
        {
            LastStartupError = "Native ClassIsland did not finish starting within 30 seconds.";
            RequestStop();
            return false;
        }
        lock (lifecycleGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (disposed || Volatile.Read(ref stopRequested) != 0 || !ready.Task.Result || !runningThread.IsAlive)
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

    private void RequestStop()
    {
        Interlocked.Exchange(ref stopRequested, 1);
        try { entryPoint?.GetMethod("StopEmbedded")?.Invoke(null, null); }
        catch (TargetInvocationException exception)
        {
            Trace.TraceWarning("ClassIsland stop request failed: {0}", exception.GetBaseException().Message);
        }
    }

    internal void Hide() => HideWithCancellation(CancellationToken.None);

    internal bool HideWithCancellation(CancellationToken cancellationToken) =>
        Command(() => { HideCore(); return true; }, cancellationToken);

    private void HideCore()
    {
        lock (lifecycleGate)
        {
            if (disposed) return;
            if (ready.Task.IsCompletedSuccessfully && ready.Task.Result)
                InvokeControl("SetEmbeddedVisible", [false]);
        }
    }

    internal async Task SuspendAsync(CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        NotifyStateChanged();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (lifecycleGate)
            {
                if (!disposed && ready.Task.IsCompletedSuccessfully && ready.Task.Result)
                    InvokeControl("SuspendEmbedded");
            }
        }
        finally
        {
            operationGate.Release();
            NotifyStateChanged();
        }
    }

    internal bool OpenSettings() => OpenPage("settings");

    internal bool OpenPage(string page) => OpenPageWithCancellation(page, CancellationToken.None);

    internal bool OpenPageWithCancellation(string page, CancellationToken cancellationToken) =>
        Command(() => OpenPageCore(page, cancellationToken), cancellationToken);

    private bool OpenPageCore(string page, CancellationToken cancellationToken)
    {
        if (page is not ("settings" or "profile" or "edit" or "class-swap"))
            throw new ArgumentException("Unsupported ClassIsland page.", nameof(page));
        if (!EnsureStarted(showIsland: false, cancellationToken)) return false;
        lock (lifecycleGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (disposed || Volatile.Read(ref stopRequested) != 0) return false;
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

    internal void SetFollowHostTheme(bool follow) => Preference(() => SetFollowHostThemeCore(follow));

    private void SetFollowHostThemeCore(bool follow)
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
            optionsType.GetProperty("StartHidden")!.SetValue(options, true);
            Action<object> created = app =>
            {
                app.GetType().GetEvent("EmbeddedFailure")?.AddEventHandler(app,
                    new EventHandler<Exception>((_, failure) =>
                    {
                        LastStartupError = failure.ToString();
                        Trace.TraceError("Embedded ClassIsland failed: {0}", failure);
                        ready.TrySetResult(false);
                        NotifyStateChanged();
                    }));
                app.GetType().GetEvent("EmbeddedVisibilityChanged")?.AddEventHandler(app,
                    new EventHandler((_, _) => NotifyStateChanged()));
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
                    NotifyStateChanged();
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
        finally
        {
            Volatile.Write(ref runtimeExited, 1);
            NotifyStateChanged();
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
        NotifyStateChanged();
        // Avalonia owns process-wide native state; its runtime context is intentionally not collectible.
    }
}
