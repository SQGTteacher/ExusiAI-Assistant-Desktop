using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace ExusiAI.Plugin.ClassIsland;

/// <summary>
/// Keeps the original runtime's package dependencies separate from WPF's already-loaded versions.
/// The host exchanges only reflection calls, primitives and framework delegates across this boundary.
/// </summary>
internal sealed class ClassIslandRuntimeLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;
    private readonly string directory;

    internal ClassIslandRuntimeLoadContext(string mainAssemblyPath, bool isCollectible = false)
        : base("ExusiAI.ClassIsland.Runtime", isCollectible)
    {
        directory = Path.GetDirectoryName(Path.GetFullPath(mainAssemblyPath))!;
        resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        var path = resolver.ResolveAssemblyToPath(name);
        if (path is null && name.Name is { } simpleName)
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, simpleName + ".dll"));
            if (string.Equals(Path.GetDirectoryName(candidate), directory, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                path = candidate;
        }
        // Framework assemblies absent from the package are shared with the .NET host.
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string name)
    {
        var path = resolver.ResolveUnmanagedDllToPath(name);
        return path is null ? 0 : LoadUnmanagedDllFromPath(path);
    }
}
