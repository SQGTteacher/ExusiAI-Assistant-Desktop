using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.MishaShowcase;

public sealed class MishaShowcasePlugin : ExtensionPluginBase, IWpfNavigationExtension
{
    private MishaPlatformStore store = null!;
    private MishaMainWindowRuntime? mainWindowRuntime;

    public override async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);
        store = new MishaPlatformStore();
        context.Logger.Information("ClassIsland 2.2 Misha feature port initialized; porter: SQGTteacher.");
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        Context.Logger.Information("ClassIsland 2.2 Misha port started.");
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            Context.Logger.Warning("WPF Application is unavailable; ClassIsland main-window runtime was not started.");
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            mainWindowRuntime = new MishaMainWindowRuntime(store, Context.Logger);
            mainWindowRuntime.Start();
        });
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null)
            await dispatcher.InvokeAsync(() =>
            {
                mainWindowRuntime?.Dispose();
                mainWindowRuntime = null;
            });
        Context.Logger.Information("ClassIsland 2.2 Misha port stopped.");
    }

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("misha.settings", "ClassIsland 2.2 Misha", "◫", () => new MishaSettingsHostPage(store))
    ];
}
