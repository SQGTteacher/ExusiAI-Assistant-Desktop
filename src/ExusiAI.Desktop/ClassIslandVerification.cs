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
            var core = runtimeContext.LoadFromAssemblyName(new AssemblyName("ClassIsland.Core"));
            var pluginInfo = Activator.CreateInstance(core.GetType("ClassIsland.Core.Models.Plugin.PluginInfo", throwOnError: true)!)!;
            var application = runtimeContext.LoadFromAssemblyName(new AssemblyName("ClassIsland"));
            var pluginDirectory = Path.Combine(outputDirectory, "plugin-sdk-probe");
            Directory.CreateDirectory(pluginDirectory);
            var pluginPath = Path.Combine(pluginDirectory, "SdkProbe.dll");
            File.Copy(entryPoint.Assembly.Location, pluginPath, overwrite: true);
            var yaml = runtimeContext.LoadFromAssemblyName(new AssemblyName("YamlDotNet"));
            File.Copy(yaml.Location, Path.Combine(pluginDirectory, "YamlDotNet.dll"), overwrite: true);
            var markdown = runtimeContext.LoadFromAssemblyName(new AssemblyName("Markdown.Avalonia"));
            File.Copy(markdown.Location, Path.Combine(pluginDirectory, "Markdown.Avalonia.dll"), overwrite: true);
            const string target = ".NETCoreApp,Version=v8.0";
            const string library = "ProbeLibraries/1.0.0";
            File.WriteAllText(Path.ChangeExtension(pluginPath, ".deps.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                runtimeTarget = new { name = target, signature = "" },
                targets = new Dictionary<string, object>
                {
                    [target] = new Dictionary<string, object>
                    {
                        [library] = new { runtime = new Dictionary<string, object> { ["YamlDotNet.dll"] = new { }, ["Markdown.Avalonia.dll"] = new { } } }
                    }
                },
                libraries = new Dictionary<string, object>
                {
                    [library] = new { type = "project", serviceable = false, sha512 = "" }
                }
            }));
            var nested = (AssemblyLoadContext)Activator.CreateInstance(
                application.GetType("ClassIsland.PluginLoadContext", throwOnError: true)!,
                pluginInfo, pluginPath, false)!;
            foreach (var name in new[] { "ClassIsland.Core", "ClassIsland.Shared", "Avalonia.Controls", "Microsoft.Extensions.DependencyInjection.Abstractions", "Markdown.Avalonia", "SoundFlow" })
                if (!ReferenceEquals(runtimeContext.LoadFromAssemblyName(new AssemblyName(name)), nested.LoadFromAssemblyName(new AssemblyName(name))))
                    throw new InvalidOperationException($"ClassIsland plugin SDK identity mismatch: {name}");
            var dependentInfo = Activator.CreateInstance(pluginInfo.GetType())!;
            var dependentManifest = dependentInfo.GetType().GetProperty("Manifest")!.GetValue(dependentInfo)!;
            const string dependencyId = "exusiai.sdk-verification.dependency";
            dependentManifest.GetType().GetProperty("Id")!.SetValue(dependentManifest, dependencyId);
            var dependentDirectory = Path.Combine(outputDirectory, "plugin-dependency-probe");
            Directory.CreateDirectory(dependentDirectory);
            var dependentPath = Path.Combine(dependentDirectory, "DependencyProbe.dll");
            File.Copy(entryPoint.Assembly.Location, dependentPath, overwrite: true);
            var dependent = (AssemblyLoadContext)Activator.CreateInstance(nested.GetType(), dependentInfo, dependentPath, false)!;
            var dependency = Activator.CreateInstance(core.GetType("ClassIsland.Core.Models.Plugin.PluginDependency", throwOnError: true)!)!;
            dependency.GetType().GetProperty("Id")!.SetValue(dependency, dependencyId);
            var manifest = pluginInfo.GetType().GetProperty("Manifest")!.GetValue(pluginInfo)!;
            ((System.Collections.IList)manifest.GetType().GetProperty("Dependencies")!.GetValue(manifest)!).Add(dependency);
            var contexts = (System.Collections.IDictionary)application.GetType("ClassIsland.Services.PluginService", throwOnError: true)!
                .GetField("PluginLoadContexts", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            contexts.Add(dependencyId, dependent);
            try
            {
                var privateYaml = nested.LoadFromAssemblyName(new AssemblyName("YamlDotNet"));
                if (ReferenceEquals(yaml, privateYaml) || AssemblyLoadContext.GetLoadContext(privateYaml) != nested)
                    throw new InvalidOperationException("Plugin-private YAML dependency was intercepted by a dependency plugin or the runtime.");
            }
            finally { contexts.Remove(dependencyId); }
            var events = runtimeContext.LoadFromAssemblyName(new AssemblyName("Microsoft.Win32.SystemEvents"));
            if (events.GetName().Version?.Major != 9)
                throw new InvalidOperationException($"Incorrect shared SystemEvents version: {events.FullName}");
            File.WriteAllText(Path.Combine(outputDirectory, "result.txt"),
                "PASS: packaged ClassIsland started in the WPF process; island visible, settings opened, island hidden; isolated SystemEvents 9 and nested plugin SDK identities verified.");
        }
        finally
        {
            if (host is not null) await Task.Run(host.Dispose);
            context.Unload();
        }
    }
}
