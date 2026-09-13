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
        context.Logger.Information("ClassIsland Misha feature port initialized; porter: SQGTteacher.");
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
        new("misha.dashboard", "课表信息岛", "◫", () => new MishaDashboardPage(store)),
        new("misha.schedule", "课表与时间表", "▦", () => new MishaSchedulePage(store)),
        new("misha.components", "组件与显示", "◩", () => new MishaComponentsPage(store)),
        new("misha.automation", "提醒与自动化", "⚡", () => new MishaAutomationPage(store)),
        new("misha.extensions", "内置扩展", "⊞", () => new MishaExtensionsPage(store)),
        new("misha.data", "档案与数据", "⇄", () => new MishaDataPage(store)),
        new("misha.about", "关于与开源", "ⓘ", static () => new MishaAboutPage())
    ];
}
