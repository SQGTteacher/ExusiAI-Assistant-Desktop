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
    [Fact]
    public void IndependentThemeIsDefaultAndChoiceSurvivesReload()
    {
        var root = Path.Combine(Path.GetTempPath(), "classisland-theme-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var host = new ClassIslandHost(root, root))
            {
                Assert.False(host.FollowHostTheme);
                host.ApplyTheme(new HostTheme(true, "#C84450"));
                Assert.False(host.FollowHostTheme);
                host.SetFollowHostTheme(true);
            }
            using (var restored = new ClassIslandHost(root, root))
            {
                Assert.True(restored.FollowHostTheme);
                restored.SetFollowHostTheme(false);
            }
            using var independent = new ClassIslandHost(root, root);
            Assert.False(independent.FollowHostTheme);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void MalformedPreferenceDoesNotPreventStartup()
    {
        var root = Path.Combine(Path.GetTempPath(), "classisland-theme-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "ExusiAIHostTheme.json"), "{broken");
            using var host = new ClassIslandHost(root, root);
            Assert.False(host.FollowHostTheme);
            host.SetFollowHostTheme(true);
            using var restored = new ClassIslandHost(root, root);
            Assert.True(restored.FollowHostTheme);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void FailedSaveKeepsPreviousThemeChoice()
    {
        var root = Path.Combine(Path.GetTempPath(), "classisland-theme-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            using var host = new ClassIslandHost(root, root);
            host.SetFollowHostTheme(true);
            Directory.CreateDirectory(Path.Combine(root, "ExusiAIHostTheme.json.tmp"));
            Assert.ThrowsAny<Exception>(() => host.SetFollowHostTheme(false));
            Assert.True(host.FollowHostTheme);
            using var restored = new ClassIslandHost(root, root);
            Assert.True(restored.FollowHostTheme);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
