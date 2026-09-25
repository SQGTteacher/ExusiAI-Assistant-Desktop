using System.Windows.Controls;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Extension.TestPlugin;

public sealed class TestPlugin : ExtensionPluginBase, IWpfNavigationExtension
{
    public override async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken) =>
        await base.InitializeAsync(context, cancellationToken);

    public override Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("test.page", "测试页", "T", static () => new UserControl())
    ];
}
