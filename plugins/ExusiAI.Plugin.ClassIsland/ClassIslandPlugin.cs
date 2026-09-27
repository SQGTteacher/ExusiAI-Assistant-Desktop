using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;
using ExusiAI.Plugin.ClassIsland.Core;

namespace ExusiAI.Plugin.ClassIsland;

public sealed class ClassIslandPlugin : ExtensionPluginBase, IWpfNavigationExtension
{
    private ClassIslandRuntimeHost runtime = null!;
    private ClassIslandCoreService core = null!;

    public override async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);

        core = new ClassIslandCoreService(context.Logger);
        await core.InitializeAsync(cancellationToken);

        runtime = new ClassIslandRuntimeHost(context.Logger);
        await runtime.InitializeAsync(cancellationToken);

        context.Logger.Information(
            $"ClassIsland module initialized. Runtime compatibility={ClassIslandRuntimeDescriptor.RuntimeVersion}, " +
            $"release={ClassIslandRuntimeDescriptor.RuntimeReleaseCommit}, Misha baseline={ClassIslandRuntimeDescriptor.MishaBaselineCommit}.");
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await core.InitializeAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await core.ShutdownAsync(cancellationToken);
        await runtime.StopAsync(cancellationToken);
        Context.Logger.Information("ClassIsland module stopped.");
    }

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("classisland.runtime", "ClassIsland", "◫", () => new ClassIslandSyncPage(runtime))
    ];
}
