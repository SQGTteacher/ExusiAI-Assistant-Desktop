using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.ArkPets;

public sealed class ArkPetsPlugin : ExtensionPluginBase, IWpfNavigationExtension
{
    private ArkPetsController controller = null!;

    public override async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);
        controller = new ArkPetsController(context.Logger);
        await controller.LoadAsync(cancellationToken);
        context.Logger.Information("ArkPets desktop-pet integration initialized.");
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await controller.StartAsync(cancellationToken);
        Context.Logger.Information("ArkPets desktop-pet integration started.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await controller.DisposeAsync();
        Context.Logger.Information("ArkPets desktop-pet integration stopped.");
    }

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("ark-pets.desktop", "ArkPets 桌宠", "♙", () => new ArkPetsPage(controller))
    ];
}
