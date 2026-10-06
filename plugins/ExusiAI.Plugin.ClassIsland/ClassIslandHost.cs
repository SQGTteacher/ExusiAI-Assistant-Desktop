using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
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
    private Thread? thread;
    private Type? entryPoint;
    private HostTheme? pendingTheme;
    private readonly object themeGate = new();
    private readonly string themePreferencePath;
    private bool followHostTheme;
    private bool disposed;
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

    internal bool IsVisible { get; private set; }

    internal bool Start()
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
            AssemblyLoadContext.Default.Resolving += ResolveAssembly;
            thread = new Thread(Run) { IsBackground = true, Name = "ClassIsland original Avalonia host" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        if (!ready.Task.Wait(TimeSpan.FromSeconds(30)))
        {
            LastStartupError = "Native ClassIsland did not finish starting within 30 seconds.";
            return false;
        }
        if (!ready.Task.Result || !thread.IsAlive)
        {
            LastStartupError ??= "Native ClassIsland stopped before its window was ready.";
            return false;
        }
        try
        {
            (entryPoint?.GetMethod("SetEmbeddedVisible")
                ?? throw new MissingMethodException("ClassIsland.Desktop.Program.SetEmbeddedVisible"))
                .Invoke(null, [true]);
            IsVisible = true;
            LastStartupError = null;
            return true;
        }
        catch (Exception exception)
        {
            LastStartupError = exception.GetBaseException().Message;
            Trace.TraceError("Embedded ClassIsland could not show its window: {0}", exception);
            return false;
        }
    }

    internal void Hide()
    {
        IsVisible = false;
        if (ready.Task.IsCompletedSuccessfully && ready.Task.Result)
            try { entryPoint?.GetMethod("SetEmbeddedVisible")?.Invoke(null, [false]); }
            catch (TargetInvocationException) { /* Host shutdown can race with UI dispatch. */ }
    }

    internal void OpenSettings()
    {
        if (ready.Task.IsCompletedSuccessfully && ready.Task.Result)
            entryPoint?.GetMethod("OpenEmbeddedSettings")?.Invoke(null, null);
    }

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

    private Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name is null) return null;
        var path = Path.Combine(nativeDirectory, name.Name + ".dll");
        return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    }

    private void Run()
    {
        try
        {
            var desktop = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(nativeDirectory, "ClassIsland.Desktop.dll"));
            var application = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(nativeDirectory, "ClassIsland.dll"));
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
        finally { IsVisible = false; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Interlocked.Exchange(ref stopRequested, 1);
        Hide();
        if (entryPoint is not null)
            try { entryPoint?.GetMethod("StopEmbedded")?.Invoke(null, null); }
            catch (TargetInvocationException) { /* The Avalonia dispatcher may already be gone. */ }
        if (thread is not null && !thread.Join(TimeSpan.FromSeconds(15)))
            Trace.TraceWarning("Embedded ClassIsland did not stop within fifteen seconds.");
        AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
    }
}
