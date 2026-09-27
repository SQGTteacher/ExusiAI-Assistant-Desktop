using ExusiAI.Plugin.MishaShowcase;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class ClassIslandEmbeddedHostTests
{
    [Fact]
    public void ModuleCatalogPinsTheMishaAlphaSourceBaseline()
    {
        Assert.Equal("ClassIsland/ClassIsland", ClassIslandEmbeddedModuleCatalog.UpstreamRepository);
        Assert.Equal("develop/v2/misha-alpha", ClassIslandEmbeddedModuleCatalog.UpstreamBranch);
        Assert.Equal("08808615899d1a4abb8e0ef576bf1e247adde10f", ClassIslandEmbeddedModuleCatalog.UpstreamCommit);
        Assert.Equal("ClassIsland/App.Services.xaml.cs", ClassIslandEmbeddedModuleCatalog.UpstreamServiceRegistrationFile);
    }

    [Fact]
    public void ModuleCatalogCoversTheMajorUpstreamRegistrationFamilies()
    {
        var modules = ClassIslandEmbeddedModuleCatalog.All;
        Assert.True(modules.Count >= 90, "Expected the source module map to remain comprehensive, got " + modules.Count + " entries.");
        Assert.Equal(modules.Count, modules.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var group in new[]
        {
            ClassIslandEmbeddedModuleGroup.Service,
            ClassIslandEmbeddedModuleGroup.Component,
            ClassIslandEmbeddedModuleGroup.NotificationProvider,
            ClassIslandEmbeddedModuleGroup.Trigger,
            ClassIslandEmbeddedModuleGroup.Rule,
            ClassIslandEmbeddedModuleGroup.Action,
            ClassIslandEmbeddedModuleGroup.Authorization,
            ClassIslandEmbeddedModuleGroup.Speech,
            ClassIslandEmbeddedModuleGroup.Theme,
            ClassIslandEmbeddedModuleGroup.ProfileTransfer,
            ClassIslandEmbeddedModuleGroup.Tutorial
        })
            Assert.Contains(modules, x => x.Group == group);

        foreach (var registration in new[]
        {
            "SettingsService",
            "ProfileService",
            "LessonsService",
            "ComponentsService",
            "NotificationHostService",
            "WeatherService",
            "AutomationService",
            "PluginService",
            "ManagementService",
            "IpcService",
            "TextComponent",
            "ScheduleComponent",
            "ClassNotificationProvider",
            "OnClassTrigger",
            "classisland.lessons.currentSubject",
            "NotificationAction",
            "PasswordAuthorizeProvider",
            "EdgeTtsService",
            "classisland.classic",
            "classisland.profileTransfer.import.cses"
        })
            Assert.Contains(modules, x => x.UpstreamRegistration.Contains(registration, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmbeddedHostOwnsWorkspaceAndMainWindowLifecycleModules()
    {
        await using var host = new ClassIslandEmbeddedHost();
        Assert.Equal(new[] { "host.workspace", "host.main-window" }, host.RuntimeModuleIds);
    }

    [Fact]
    public void ClassIslandModulesStayInsideTheExusiAIProcessBoundary()
    {
        Assert.All(ClassIslandEmbeddedModuleCatalog.All, module => Assert.True(module.RunsInProcess));
        Assert.DoesNotContain(
            ClassIslandEmbeddedModuleCatalog.All,
            module => module.ExusiAIAdapter.Contains("ClassIsland.exe", StringComparison.OrdinalIgnoreCase));

        var assemblyReferences = typeof(ClassIslandMishaPlugin).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(
            assemblyReferences,
            reference => reference.Name?.StartsWith("ClassIsland", StringComparison.OrdinalIgnoreCase) == true);
    }
}
