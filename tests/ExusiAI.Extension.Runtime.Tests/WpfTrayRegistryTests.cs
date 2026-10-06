using ExusiAI.Extension.Wpf;
using ExusiAI.Plugin.ClassIsland;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class WpfTrayRegistryTests
{
    private static WpfTrayCommand Command(string id) => new(id, id, _ => Task.CompletedTask);

    [Fact]
    public void InvalidReplacementKeepsExistingContribution()
    {
        var registry = new WpfTrayRegistry();
        var existing = Command("swap");
        registry.Register("classisland", "ClassIsland", [existing]);
        Assert.Throws<ArgumentException>(() => registry.Register("classisland", "ClassIsland",
            [Command("edit"), Command("EDIT")]));
        Assert.True(registry.IsRegistered("classisland", existing));
        Assert.Single(registry.Menus);
    }

    [Fact]
    public void ReplacementAndUninstallInvalidateOldCommands()
    {
        var registry = new WpfTrayRegistry();
        var old = Command("swap");
        var current = Command("swap");
        var changes = 0;
        registry.Changed += (_, _) => changes++;
        registry.Register("classisland", "ClassIsland", [old]);
        registry.Register("CLASSISLAND", "ClassIsland", [current]);
        Assert.False(registry.IsRegistered("classisland", old));
        Assert.True(registry.IsRegistered("classisland", current));
        Assert.Single(registry.Menus);
        registry.Unregister("ClassIsland");
        registry.Unregister("ClassIsland");
        Assert.False(registry.IsRegistered("classisland", current));
        Assert.Empty(registry.Menus);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void PackagesCanUseSameCommandIdWithoutAffectingEachOther()
    {
        var registry = new WpfTrayRegistry();
        var first = Command("settings");
        var second = Command("settings");
        registry.Register("first", "First", [first]);
        registry.Register("second", "Second", [second]);
        registry.Unregister("first");
        Assert.Single(registry.Menus);
        Assert.True(registry.IsRegistered("second", second));
        Assert.False(registry.IsRegistered("second", first));
    }

    [Fact]
    public void ClassIslandContributesOriginalActions()
    {
        var plugin = Assert.IsAssignableFrom<IWpfTrayExtension>(new ClassIslandPlugin());
        Assert.Equal(new[] { "class-swap", "profile", "edit", "settings", "show", "hide" },
            plugin.GetTrayCommands().Select(command => command.Id));
    }
}
