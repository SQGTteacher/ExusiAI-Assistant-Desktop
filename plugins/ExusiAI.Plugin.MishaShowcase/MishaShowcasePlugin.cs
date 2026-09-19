using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.MishaShowcase;

public sealed class MishaShowcasePlugin : ExtensionPluginBase, IWpfNavigationExtension
{
    private MishaPlatformStore store = null!;

    public override async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);
        store = new MishaPlatformStore();
        context.Logger.Information("ClassIsland 2.2 Misha feature port initialized; porter: SQGTteacher.");
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Context.Logger.Information("ClassIsland 2.2 Misha port started.");
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        Context.Logger.Information("ClassIsland 2.2 Misha port stopped.");
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("misha.settings", "ClassIsland 2.2 Misha", "◫", () => new MishaSettingsHostPage(store))
    ];
}
