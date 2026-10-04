using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

namespace ExusiAI.Plugin.ClassIsland;

/// <summary>
/// Starts the original ClassIsland Avalonia application inside the ExusiAI process.
/// Its assemblies must be built from the checked-in source and packaged locally.
/// </summary>
internal sealed class ClassIslandOriginalHost : IDisposable
{
    private readonly string dataDirectory;
    private readonly string nativeDirectory;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? thread;
    private Type? entryPoint;
    private bool disposed;

    internal ClassIslandOriginalHost(string dataDirectory)
    {
        this.dataDirectory = dataDirectory;
        nativeDirectory = Path.Combine(Path.GetDirectoryName(typeof(ClassIslandPlugin).Assembly.Location)!, "NativeClassIsland");
    }

    internal bool IsAvailable => File.Exists(Path.Combine(nativeDirectory, "ClassIsland.Desktop.dll")) &&
        File.Exists(Path.Combine(nativeDirectory, "ClassIsland.dll"));

    internal bool IsVisible { get; private set; }

    internal bool Start()
    {
        if (disposed || !IsAvailable) return false;
        if (thread is null)
        {
            AssemblyLoadContext.Default.Resolving += ResolveAssembly;
            thread = new Thread(Run) { IsBackground = true, Name = "ClassIsland original Avalonia host" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        if (!ready.Task.Wait(TimeSpan.FromSeconds(15)) || !ready.Task.Result || thread.IsAlive == false) return false;
        try
        {
            entryPoint?.GetMethod("SetEmbeddedVisible")?.Invoke(null, [true]);
            IsVisible = true;
            return true;
        }
        catch (TargetInvocationException) { return false; }
    }

    internal void Stop()
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
                started.AddEventHandler(app, new EventHandler((_, _) => ready.TrySetResult(true)));
            };
            entryPoint!.GetMethod("RunEmbedded")!.Invoke(null, [options, created]);
            ready.TrySetResult(false);
        }
        catch (Exception exception)
        {
            Trace.TraceError("Embedded ClassIsland failed: {0}", exception);
            ready.TrySetResult(false);
        }
        finally { IsVisible = false; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
        if (ready.Task.IsCompletedSuccessfully && ready.Task.Result)
            try { entryPoint?.GetMethod("StopEmbedded")?.Invoke(null, null); }
            catch (TargetInvocationException) { /* The Avalonia dispatcher may already be gone. */ }
        if (thread is not null && !thread.Join(TimeSpan.FromSeconds(5)))
            Trace.TraceWarning("Embedded ClassIsland did not stop within five seconds.");
        AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
    }
}
