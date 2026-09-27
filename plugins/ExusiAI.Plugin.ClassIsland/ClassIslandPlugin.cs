using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.ClassIsland;

public sealed class ClassIslandPlugin : ExtensionPluginBase, IWpfNavigationExtension
{
    private ClassIslandRuntimeHost runtime = null!;

    public override async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);
        runtime = new ClassIslandRuntimeHost(context.Logger);
        await runtime.InitializeAsync(cancellationToken);
        context.Logger.Information(
            $"ClassIsland native-runtime integration initialized. Runtime={ClassIslandRuntimeDescriptor.RuntimeVersion}, " +
            $"release={ClassIslandRuntimeDescriptor.RuntimeReleaseCommit}, Misha baseline={ClassIslandRuntimeDescriptor.MishaBaselineCommit}.");
    }

    public override Task StartAsync(CancellationToken cancellationToken) => runtime.StartAsync(cancellationToken);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await runtime.StopAsync(cancellationToken);
        Context.Logger.Information("ClassIsland native-runtime integration stopped.");
    }

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("classisland.runtime", "ClassIsland", "◫", () => new ClassIslandSyncPage(runtime))
    ];
}
