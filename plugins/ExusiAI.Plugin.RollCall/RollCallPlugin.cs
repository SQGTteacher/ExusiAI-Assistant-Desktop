using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.RollCall;

public sealed class RollCallPlugin : ExtensionPluginBase, IWpfNavigationExtension
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Context.Logger.Information("Roll call plugin started.");
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        Context.Logger.Information("Roll call plugin stopped.");
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("roll-call.classroom", "课堂点名", "◉", static () => new RollCallPage())
    ];
}
