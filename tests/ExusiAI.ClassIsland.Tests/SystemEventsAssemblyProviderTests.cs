using System.Reflection;
using System.Runtime.Loader;
using ExusiAI.Extension.Wpf;
using ExusiAI.Plugin.ClassIsland;

namespace ExusiAI.ClassIsland.Tests;

public sealed class SystemEventsAssemblyProviderTests
{
    [Fact]
    public void ConcurrentRuntimeContextsShareOnePackagedSystemEventsImplementation()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "DependencyProbes", "Microsoft.Win32.SystemEvents.dll");
        var results = new Assembly[16];
        Parallel.For(0, results.Length, index =>
        {
            var context = new ClassIslandRuntimeLoadContext(path, isCollectible: true);
            try { results[index] = context.LoadFromAssemblyName(new AssemblyName("Microsoft.Win32.SystemEvents")); }
            finally { context.Unload(); }
        });
        Assert.Equal(9, results[0].GetName().Version!.Major);
        Assert.Equal(Path.GetFullPath(path), results[0].Location);
        Assert.False(AssemblyLoadContext.GetLoadContext(results[0])!.IsCollectible);
        Assert.All(results, candidate => Assert.Same(results[0], candidate));
        Assert.Same(results[0], SystemEventsAssemblyProvider.Load(path));
    }

    [Fact]
    public void RejectsOtherAssemblyIdentities()
    {
        Assert.Throws<ArgumentException>(() => SystemEventsAssemblyProvider.Load(typeof(SystemEventsAssemblyProviderTests).Assembly.Location));
    }
}
