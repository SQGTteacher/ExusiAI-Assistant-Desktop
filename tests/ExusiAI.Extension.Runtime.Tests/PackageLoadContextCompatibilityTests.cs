using System.Reflection;
using System.Runtime.Loader;
using ExusiAI.Extension.Runtime;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class PackageLoadContextCompatibilityTests
{
    [Theory]
    [InlineData("ExusiAI.Extension.Abstractions")]
#if WINDOWS
    [InlineData("ExusiAI.Extension.Wpf")]
#endif
    public void PreviousPluginContractReferenceResolvesToTheHost(string assemblyName)
    {
        var hostAssembly = AssemblyLoadContext.Default.LoadFromAssemblyName(new(assemblyName));
        var previousVersion = new Version(0, 2, 0, 0);
        Assert.True(hostAssembly.GetName().Version >= previousVersion,
            $"{assemblyName} must not downgrade the CLR identity used by existing plugins.");
        var context = new PackageLoadContext(hostAssembly.Location, [assemblyName]);
        try
        {
            // Existing preview.4 plugins request this CLR identity, regardless of the new product label.
            var previousReference = new AssemblyName(assemblyName) { Version = previousVersion };
            Assert.Same(hostAssembly, context.LoadFromAssemblyName(previousReference));
        }
        finally
        {
            context.Unload();
        }
    }
}
