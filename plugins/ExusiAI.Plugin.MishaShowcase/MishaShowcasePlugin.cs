using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.MishaShowcase;

public class ClassIslandMishaPlugin : ExtensionPluginBase, IWpfNavigationExtension
{
    private ClassIslandEmbeddedHost embeddedHost = null!;

    public override async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);
        embeddedHost = new ClassIslandEmbeddedHost();
        await embeddedHost.InitializeAsync(context, cancellationToken);
        context.Logger.Information("ClassIsland 2.2 Misha source modules are hosted as an ExusiAI plugin; porter: SQGTteacher.");
    }

    public override Task StartAsync(CancellationToken cancellationToken) =>
        embeddedHost.StartAsync(cancellationToken);

    public override Task StopAsync(CancellationToken cancellationToken) =>
        embeddedHost.StopAsync(cancellationToken);

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("misha.settings", "ClassIsland 2.2 Misha", "◫", () => new MishaSettingsHostPage(embeddedHost.Store))
    ];
}

// Retained for binary/package compatibility with earlier preview manifests. New packages use
// ClassIslandMishaPlugin as the entry point.
public sealed class MishaShowcasePlugin : ClassIslandMishaPlugin
{
}
