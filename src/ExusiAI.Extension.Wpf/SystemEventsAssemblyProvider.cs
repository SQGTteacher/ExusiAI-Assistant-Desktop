using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace ExusiAI.Extension.Wpf;

/// <summary>Owns one SystemEvents implementation across reloadable UI plugins.</summary>
public static class SystemEventsAssemblyProvider
{
    private static readonly object Gate = new();
    private static Assembly? assembly;

    public static Assembly Load(string implementationPath)
    {
        var path = Path.GetFullPath(implementationPath);
        var requested = AssemblyName.GetAssemblyName(path);
        if (requested.Name != "Microsoft.Win32.SystemEvents")
            throw new ArgumentException("Only the SystemEvents implementation can be shared here.", nameof(implementationPath));
        lock (Gate)
        {
            if (assembly is not null)
            {
                if (assembly.FullName != requested.FullName)
                    throw new InvalidOperationException("SystemEvents 版本已加载。请重启 ExusiAI 后使用新版本。");
                return assembly;
            }
            // SystemEvents 9 registers an AppDomain-named native window class.
            // Loading another copy after plugin unload cannot register it again.
            // This provider lives in the shared WPF contract assembly, not the
            // collectible plugin. WPF's older SystemEvents remains isolated.
            assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate => candidate.FullName == requested.FullName)
                ?? new AssemblyLoadContext("ExusiAI.SystemEvents", isCollectible: false).LoadFromAssemblyPath(path);
            return assembly;
        }
    }
}
