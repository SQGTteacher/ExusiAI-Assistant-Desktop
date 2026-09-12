using System.Reflection;
using System.Runtime.Loader;

namespace ExusiAI.Extension.Runtime;

public sealed class PackageLoadContext : AssemblyLoadContext
{
    private readonly string mainAssemblyPath;
    private readonly AssemblyDependencyResolver resolver;
    private readonly HashSet<string> sharedAssemblyNames;

    public PackageLoadContext(string mainAssemblyPath, IEnumerable<string> sharedAssemblyNames)
        : base($"ExusiAI.Package.{Path.GetFileNameWithoutExtension(mainAssemblyPath)}.{Guid.NewGuid():N}", isCollectible: true)
    {
        this.mainAssemblyPath = mainAssemblyPath;
        resolver = new(mainAssemblyPath);
        this.sharedAssemblyNames = new(sharedAssemblyNames, StringComparer.OrdinalIgnoreCase);
    }

    public Assembly LoadMainAssembly() => LoadManagedAssembly(mainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not null && sharedAssemblyNames.Contains(assemblyName.Name))
            return Default.Assemblies.FirstOrDefault(x => string.Equals(x.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
        var assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadManagedAssembly(assemblyPath);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is null ? 0 : LoadUnmanagedDllFromPath(libraryPath);
    }

    private Assembly LoadManagedAssembly(string assemblyPath)
    {
        using var source = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        buffer.Position = 0;
        return LoadFromStream(buffer);
    }
}
