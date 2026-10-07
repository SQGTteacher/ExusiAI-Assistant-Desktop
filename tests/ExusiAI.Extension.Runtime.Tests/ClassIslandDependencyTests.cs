using System.Reflection;
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
        ClassIslandRuntimeLoadContext? context = null;
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
            context = new ClassIslandRuntimeLoadContext(main, isCollectible: true);
            // An identically named assembly is already loaded by the host. Private loading
            // must still choose the packaged implementation instead of reusing that assembly.
            var loaded = context.LoadFromAssemblyName(new AssemblyName(name));
            Assert.NotSame(typeof(ClassIslandHost).Assembly, loaded);
            Assert.Equal(Path.GetFullPath(implementation), loaded.Location);
        }
        finally
        {
            context?.Unload();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
