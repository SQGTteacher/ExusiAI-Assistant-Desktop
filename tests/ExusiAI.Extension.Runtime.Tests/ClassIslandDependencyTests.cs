using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using ExusiAI.Plugin.ClassIsland;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class ClassIslandDependencyTests
{
    [Fact]
    public void LoadsRidImplementationFromDependencyManifestInsteadOfTopLevelStub()
    {
        var root = Path.Combine(Path.GetTempPath(), "island-dependencies-" + Guid.NewGuid().ToString("N"));
        var native = Path.Combine(root, "NativeClassIsland");
        try
        {
            Directory.CreateDirectory(native);
            var source = typeof(ClassIslandHost).Assembly.Location;
            var name = AssemblyName.GetAssemblyName(source).Name!;
            var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
            var relative = $"runtimes/{rid}/lib/net8.0/{name}.dll";
            var implementation = Path.Combine(native, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(implementation)!);
            File.Copy(source, implementation);
            // The top-level placeholder must never be selected in place of the RID implementation.
            File.WriteAllBytes(Path.Combine(native, name + ".dll"), []);
            var main = Path.Combine(native, "ClassIsland.Desktop.dll");
            File.Copy(source, main);
            var target = ".NETCoreApp,Version=v8.0/" + rid;
            File.WriteAllText(Path.ChangeExtension(main, ".deps.json"), JsonSerializer.Serialize(new
            {
                runtimeTarget = new { name = target, signature = "" },
                targets = new Dictionary<string, object>
                {
                    [target] = new Dictionary<string, object>
                    {
                        [name + "/1.0.0"] = new
                        {
                            runtimeTargets = new Dictionary<string, object>
                            {
                                [relative] = new { rid, assetType = "runtime" }
                            }
                        }
                    }
                },
                libraries = new Dictionary<string, object>
                {
                    [name + "/1.0.0"] = new { type = "package", serviceable = false, sha512 = "" }
                }
            }));
            var unloaded = LoadAndAssert(main, name, implementation);
            // Unload requests collection; Windows releases the mapped DLL only
            // after the loader allocator and its assembly references are collected.
            for (var attempt = 0; attempt < 10 && unloaded.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(unloaded.IsAlive);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndAssert(string main, string name, string implementation)
    {
        var context = new ClassIslandRuntimeLoadContext(main, isCollectible: true);
        var weak = new WeakReference(context);
        try
        {
            var loaded = context.LoadFromAssemblyName(new AssemblyName(name));
            Assert.NotSame(typeof(ClassIslandHost).Assembly, loaded);
            Assert.Equal(Path.GetFullPath(implementation), loaded.Location);
        }
        finally { context.Unload(); }
        return weak;
    }
}
