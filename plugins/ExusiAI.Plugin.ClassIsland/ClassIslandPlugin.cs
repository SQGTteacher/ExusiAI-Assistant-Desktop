using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.ClassIsland;

public sealed class ClassIslandPlugin : ExtensionPluginBase, IWpfNavigationExtension
{
    private ClassIslandCoreService core = null!;

    public override async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);
        core = new ClassIslandCoreService(context.Logger);
        await core.InitializeAsync(cancellationToken);
        context.Logger.Information($"ClassIsland native plugin initialized. Misha baseline={ClassIslandRuntimeDescriptor.MishaBaselineCommit}.");
    }

    public override Task StartAsync(CancellationToken cancellationToken) => core.StartAsync(cancellationToken);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await core.StopAsync(cancellationToken);
        Context.Logger.Information("ClassIsland native plugin stopped.");
    }

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("classisland.core", "ClassIsland", "◫", () => new ClassIslandSyncPage(core))
    ];
}
