using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.Runtime;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Desktop;

/// <summary>Exercises the packaged plugin in the real WPF process with isolated ClassIsland data.</summary>
internal static class ClassIslandVerification
{
    internal static async Task RunAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var package = Path.Combine(AppContext.BaseDirectory, "packages", "exusiai.classisland");
        var data = Path.Combine(outputDirectory, "ClassIslandData");
        var context = new PackageLoadContext(Path.Combine(package, "ExusiAI.Plugin.ClassIsland.dll"),
            ["ExusiAI.Extension.Abstractions", "ExusiAI.Extension.Wpf"]);
        IDisposable? host = null;
        try
        {
            // Force the same shared assembly identities used by normal plugin discovery.
            _ = typeof(IExusiAIPlugin).Assembly;
            _ = typeof(IWpfTrayExtension).Assembly;
            var assembly = context.LoadMainAssembly();
            var type = assembly.GetType("ExusiAI.Plugin.ClassIsland.ClassIslandHost", throwOnError: true)!;
            host = (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, args: [data, package], culture: null)!;
            object? Invoke(string method) => type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null);
            string? Error() => type.GetProperty("LastStartupError", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) as string;
            if (!await Task.Run(() => Invoke("Start") is true))
                throw new InvalidOperationException(Error() ?? "ClassIsland startup failed.");
            if (type.GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) is not true)
                throw new InvalidOperationException("ClassIsland started without a visible information island.");
            if (!await Task.Run(() => Invoke("OpenSettings") is true))
                throw new InvalidOperationException(Error() ?? "ClassIsland settings failed.");
            Invoke("Hide");
            if (type.GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) is not false)
                throw new InvalidOperationException("ClassIsland information island did not hide.");
            var entryPoint = (Type)type.GetField("entryPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            var runtimeContext = AssemblyLoadContext.GetLoadContext(entryPoint.Assembly)!;
            if (runtimeContext == AssemblyLoadContext.Default)
                throw new InvalidOperationException("ClassIsland runtime dependencies are not isolated.");
            var events = runtimeContext.LoadFromAssemblyName(new AssemblyName("Microsoft.Win32.SystemEvents"));
            if (events.GetName().Version?.Major != 9)
                throw new InvalidOperationException($"Incorrect shared SystemEvents version: {events.FullName}");
            File.WriteAllText(Path.Combine(outputDirectory, "result.txt"),
                "PASS: packaged ClassIsland started in the WPF process; island visible, settings opened, island hidden; isolated SystemEvents 9 loaded.");
        }
        finally
        {
            if (host is not null) await Task.Run(host.Dispose);
            context.Unload();
        }
    }
}
