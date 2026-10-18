using System.IO;
using System.IO.Compression;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        var installProbe = PreparePluginInstallProbes(data);
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
            if (!await Task.Run(() => Invoke("OpenSettings") is true))
                throw new InvalidOperationException(Error() ?? "ClassIsland cold settings startup failed.");
            if (type.GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) is not false)
                throw new InvalidOperationException("Opening settings from a cold start unexpectedly showed the island.");
            var coldWindow = new IntPtr((long)InvokeEntryPoint(type, host, "GetEmbeddedWindowHandle")!);
            if (IsWindowVisible(coldWindow))
                throw new InvalidOperationException("Cold settings startup left the native island window visible.");
            if (!await Task.Run(() => Invoke("Start") is true))
                throw new InvalidOperationException(Error() ?? "ClassIsland startup failed.");
            if (type.GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) is not true)
                throw new InvalidOperationException("ClassIsland started without a visible information island.");
            var preview = Path.Combine(outputDirectory, "classisland-main-window.png");
            var compositionMode = (string)InvokeEntryPoint(type, host, "GetEmbeddedCompositionMode")!;
            var windowHandle = new IntPtr((long)InvokeEntryPoint(type, host, "GetEmbeddedWindowHandle")!);
            var renderStatus = CaptureNativeWindow(windowHandle, preview);
            if (compositionMode != "RedirectionSurface" || !renderStatus.HasVisibleContent)
                throw new InvalidOperationException($"ClassIsland main window did not render correctly: {renderStatus}");
            VerifyPluginInstallProbes(installProbe);
            if (!await Task.Run(() => Invoke("OpenSettings") is true))
                throw new InvalidOperationException(Error() ?? "ClassIsland settings failed.");
            Invoke("Hide");
            if (type.GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) is not false)
                throw new InvalidOperationException("ClassIsland information island did not hide.");
            if (!await Task.Run(() => Invoke("OpenSettings") is true) ||
                type.GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) is not false)
                throw new InvalidOperationException("Opening settings changed the hidden island state.");
            if (!await Task.Run(() => Invoke("Start") is true) ||
                type.GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host) is not true)
                throw new InvalidOperationException("ClassIsland information island could not be shown again.");
            Invoke("Hide");
            var entryPoint = (Type)type.GetField("entryPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            var runtimeContext = AssemblyLoadContext.GetLoadContext(entryPoint.Assembly)!;
            if (runtimeContext == AssemblyLoadContext.Default)
                throw new InvalidOperationException("ClassIsland runtime dependencies are not isolated.");
            var core = runtimeContext.LoadFromAssemblyName(new AssemblyName("ClassIsland.Core"));
            var pluginInfo = Activator.CreateInstance(core.GetType("ClassIsland.Core.Models.Plugin.PluginInfo", throwOnError: true)!)!;
            var application = runtimeContext.LoadFromAssemblyName(new AssemblyName("ClassIsland"));
            VerifyPluginLoadStatus(application);
            var pluginDirectory = Path.Combine(outputDirectory, "plugin-sdk-probe");
            Directory.CreateDirectory(pluginDirectory);
            var pluginPath = Path.Combine(pluginDirectory, "SdkProbe.dll");
            File.Copy(entryPoint.Assembly.Location, pluginPath, overwrite: true);
            var yaml = runtimeContext.LoadFromAssemblyName(new AssemblyName("YamlDotNet"));
            File.Copy(yaml.Location, Path.Combine(pluginDirectory, "YamlDotNet.dll"), overwrite: true);
            var markdown = runtimeContext.LoadFromAssemblyName(new AssemblyName("Markdown.Avalonia"));
            File.Copy(markdown.Location, Path.Combine(pluginDirectory, "Markdown.Avalonia.dll"), overwrite: true);
            foreach (var name in new[] { "Microsoft.Extensions.Configuration.Abstractions", "Microsoft.Extensions.Primitives" })
                File.Copy(runtimeContext.LoadFromAssemblyName(new AssemblyName(name)).Location,
                    Path.Combine(pluginDirectory, name + ".dll"), overwrite: true);
            var consoleLogging = runtimeContext.LoadFromAssemblyName(new AssemblyName("Microsoft.Extensions.Logging.Console"));
            File.Copy(consoleLogging.Location, Path.Combine(pluginDirectory, "Microsoft.Extensions.Logging.Console.dll"), overwrite: true);
            var moonSharp = runtimeContext.LoadFromAssemblyName(new AssemblyName("MoonSharp.Interpreter"));
            File.Copy(moonSharp.Location, Path.Combine(pluginDirectory, "MoonSharp.Interpreter.dll"), overwrite: true);
            const string target = ".NETCoreApp,Version=v8.0";
            const string library = "ProbeLibraries/1.0.0";
            File.WriteAllText(Path.ChangeExtension(pluginPath, ".deps.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                runtimeTarget = new { name = target, signature = "" },
                targets = new Dictionary<string, object>
                {
                    [target] = new Dictionary<string, object>
                    {
                        [library] = new { runtime = new Dictionary<string, object>
                        {
                            ["YamlDotNet.dll"] = new { }, ["Markdown.Avalonia.dll"] = new { },
                            ["Microsoft.Extensions.Configuration.Abstractions.dll"] = new { },
                            ["Microsoft.Extensions.Primitives.dll"] = new { },
                            ["Microsoft.Extensions.Logging.Console.dll"] = new { },
                            ["MoonSharp.Interpreter.dll"] = new { }
                        } }
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
            foreach (var name in new[] { "ClassIsland.Core", "ClassIsland.Shared", "Avalonia.Controls", "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Configuration.Abstractions", "Microsoft.Extensions.Primitives", "Markdown.Avalonia", "SoundFlow", "MoonSharp.Interpreter" })
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
                var privateLogging = nested.LoadFromAssemblyName(new AssemblyName("Microsoft.Extensions.Logging.Console"));
                if (ReferenceEquals(consoleLogging, privateLogging) || AssemblyLoadContext.GetLoadContext(privateLogging) != nested)
                    throw new InvalidOperationException("Plugin-private logging implementation was intercepted by the runtime.");
                // Model a dependency plugin whose exported API exposes runtime
                // logging options through inherited members of an external base type.
                Type baseContract;
                using (runtimeContext.EnterContextualReflection())
                {
                    var builder = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                        new AssemblyName("ExternalContractProbe"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
                    var contract = builder.DefineDynamicModule("Contracts").DefineType("ExternalLoggingBase", TypeAttributes.Public);
                    contract.DefineDefaultConstructor(MethodAttributes.Public);
                    contract.DefineField("Options", consoleLogging.GetType("Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions", throwOnError: true)!, FieldAttributes.Public);
                    baseContract = contract.CreateType()!;
                }
                using (dependent.EnterContextualReflection())
                {
                    var builder = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                        new AssemblyName("DependencyContractProbe"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
                    var contract = builder.DefineDynamicModule("Contracts").DefineType("PublicLoggingContract", TypeAttributes.Public, baseContract);
                    _ = contract.CreateType();
                }
                var consumer = (AssemblyLoadContext)Activator.CreateInstance(nested.GetType(), pluginInfo, pluginPath, false)!;
                if (!ReferenceEquals(consoleLogging, consumer.LoadFromAssemblyName(new AssemblyName("Microsoft.Extensions.Logging.Console"))))
                    throw new InvalidOperationException("Dependency-plugin exported logging contract lost its runtime identity.");
                await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
                {
                    var parallelConsumer = (AssemblyLoadContext)Activator.CreateInstance(nested.GetType(), pluginInfo, pluginPath, false)!;
                    if (!ReferenceEquals(consoleLogging, parallelConsumer.LoadFromAssemblyName(new AssemblyName("Microsoft.Extensions.Logging.Console"))))
                        throw new InvalidOperationException("Concurrent dependency contract resolution lost its runtime identity.");
                })));
            }
            finally { contexts.Remove(dependencyId); }
            var events = runtimeContext.LoadFromAssemblyName(new AssemblyName("Microsoft.Win32.SystemEvents"));
            if (events.GetName().Version?.Major != 9)
                throw new InvalidOperationException($"Incorrect shared SystemEvents version: {events.FullName}");
            File.WriteAllText(Path.Combine(outputDirectory, "result.txt"),
                $"PASS: packaged ClassIsland rendered via {compositionMode} ({renderStatus}); settings opened and island hidden; transactional plugin install and retry verified; isolated SystemEvents 9 and nested plugin SDK identities verified.");
        }
        finally
        {
            if (host is not null) await Task.Run(host.Dispose);
            context.Unload();
        }
    }

    private static object? InvokeEntryPoint(Type hostType, IDisposable host, string method, params object?[] args)
    {
        var entryPoint = (Type)hostType.GetField("entryPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        return entryPoint.GetMethod(method)!.Invoke(null, args);
    }

    private static (string ValidPackage, string InvalidPackage, string ValidDirectory, string InvalidDirectory) PreparePluginInstallProbes(string data)
    {
        const string validId = "exusiai.install-verification.valid";
        const string invalidId = "exusiai.install-verification.invalid";
        var plugins = Path.Combine(data, "Plugins");
        var packages = Path.Combine(data, "Cache", "PluginPackages");
        Directory.CreateDirectory(packages);
        var validDirectory = CreateOldPlugin(plugins, validId);
        var invalidDirectory = CreateOldPlugin(plugins, invalidId);
        var validPackage = Path.Combine(packages, "valid.CIPX");
        var invalidPackage = Path.Combine(packages, "invalid.cipx");
        CreatePluginPackage(validPackage, validId, "Probe.dll", typeof(ClassIslandVerification).Assembly.Location);
        CreatePluginPackage(invalidPackage, invalidId, "Missing.dll", null);
        return (validPackage, invalidPackage, validDirectory, invalidDirectory);
    }

    private static string CreateOldPlugin(string plugins, string id)
    {
        var directory = Path.Combine(plugins, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".disabled"), "");
        File.WriteAllText(Path.Combine(directory, "old.txt"), "old");
        File.WriteAllText(Path.Combine(directory, "manifest.yml"), Manifest(id, "Old.dll"));
        return directory;
    }

    private static void CreatePluginPackage(string path, string id, string entrance, string? assembly)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("manifest.yml").Open()))
            writer.Write(Manifest(id, entrance));
        if (assembly is null) return;
        using var input = File.OpenRead(assembly);
        using var output = archive.CreateEntry(entrance).Open();
        input.CopyTo(output);
    }

    private static string Manifest(string id, string entrance) => $"""
        id: {id}
        name: Install verification
        version: 1.0.0
        apiVersion: 2.0.0.0
        entranceAssembly: {entrance}
        """;

    private static void VerifyPluginInstallProbes((string ValidPackage, string InvalidPackage, string ValidDirectory, string InvalidDirectory) probe)
    {
        if (File.Exists(probe.ValidPackage) || !File.Exists(Path.Combine(probe.ValidDirectory, "Probe.dll")) ||
            File.Exists(Path.Combine(probe.ValidDirectory, "old.txt")) || !File.Exists(Path.Combine(probe.ValidDirectory, ".disabled")))
            throw new InvalidOperationException("Valid queued ClassIsland plugin was not transactionally installed.");
        if (!File.Exists(probe.InvalidPackage) || !File.Exists(Path.Combine(probe.InvalidDirectory, "old.txt")) ||
            !File.Exists(Path.Combine(probe.InvalidDirectory, ".disabled")))
            throw new InvalidOperationException("Invalid queued ClassIsland plugin was not retained with its old version.");
    }

    private static void VerifyPluginLoadStatus(Assembly application)
    {
        var service = application.GetType("ClassIsland.Services.PluginService", throwOnError: true)!;
        var statuses = (IEnumerable)service.GetProperty("PluginLoadedStatus", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "exusiai.install-verification.valid", "exusiai.install-verification.invalid"
        };
        foreach (var info in statuses)
        {
            var manifest = info!.GetType().GetProperty("Manifest")!.GetValue(info)!;
            var id = (string)manifest.GetType().GetProperty("Id")!.GetValue(manifest)!;
            if (!expected.Remove(id)) continue;
            var status = info.GetType().GetProperty("LoadStatus")!.GetValue(info)?.ToString();
            if (status != "Disabled")
                throw new InvalidOperationException($"ClassIsland plugin {id} lost its disabled state: {status}.");
        }
        if (expected.Count != 0)
            throw new InvalidOperationException($"ClassIsland did not index plugin probes: {string.Join(", ", expected)}.");
    }

    private static NativeRenderStatus CaptureNativeWindow(IntPtr window, string path)
    {
        if (window == IntPtr.Zero || !GetWindowRect(window, out var rect))
            throw new InvalidOperationException("ClassIsland has no capturable native window.");
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 1 || height <= 1)
            throw new InvalidOperationException($"ClassIsland native window has invalid bounds {width}x{height}.");

        _ = DwmFlush();
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(window, memory, 2))
                throw new InvalidOperationException("PrintWindow could not capture the ClassIsland top-level surface.");
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);
            var first = BitConverter.ToUInt32(pixels, 0);
            var different = 0;
            var nonBlack = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (BitConverter.ToUInt32(pixels, i) != first) different++;
                if (pixels[i] > 8 || pixels[i + 1] > 8 || pixels[i + 2] > 8) nonBlack++;
            }
            using var output = File.Create(path);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(converted));
            encoder.Save(output);
            return new NativeRenderStatus(width, height, different >= 64 && nonBlack >= 64, different, nonBlack);
        }
        finally
        {
            _ = SelectObject(memory, previous);
            _ = DeleteObject(bitmap);
            _ = DeleteDC(memory);
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private readonly record struct NativeRenderStatus(int Width, int Height, bool HasVisibleContent,
        int DifferentPixels, int NonBlackPixels)
    {
        public override string ToString() => $"{Width}x{Height}; different={DifferentPixels}; nonBlack={NonBlackPixels}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
