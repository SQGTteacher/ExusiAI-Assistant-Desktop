using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.MishaShowcase;

public sealed class MishaShowcasePlugin : ExtensionPluginBase, IWpfNavigationExtension
{
    public override async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);
        context.Logger.Information("Misha platform showcase initialized from the independent ExusiAI implementation.");
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Context.Logger.Information("Misha platform showcase started.");
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        Context.Logger.Information("Misha platform showcase stopped.");
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("misha.dashboard", "米沙仪表板", "◫", static () => new MishaDashboardPage()),
        new("misha.reference", "范本说明", "◇", static () => new MishaReferencePage())
    ];
}
