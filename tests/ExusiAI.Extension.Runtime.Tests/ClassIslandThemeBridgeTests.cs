using ExusiAI.Extension.Wpf;
using ExusiAI.Plugin.ClassIsland;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class ClassIslandThemeBridgeTests
{
    [Fact]
    public void PluginOptsIntoTheHostThemeContract()
    {
        // The bridge is opt-in. Dropping this interface would silently stop the
        // embedded island from following the shell theme.
        Assert.IsAssignableFrom<IWpfHostThemeExtension>(new ClassIslandPlugin());
    }

    [Fact]
    public void ApplyingHostThemeBeforeInitializationIsIgnored()
    {
        var plugin = new ClassIslandPlugin();
        plugin.ApplyHostTheme(new HostTheme(true, "#C84450"));
    }
}
