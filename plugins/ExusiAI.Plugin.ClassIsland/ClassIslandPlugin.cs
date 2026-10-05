using System.IO;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.ClassIsland;

public sealed class ClassIslandPlugin : ExtensionPluginBase, IWpfNavigationExtension, IWpfHostThemeExtension
{
    private ClassIslandHost host = null!;

    public override async Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        await base.InitializeAsync(context, cancellationToken);
        var packageDirectory = (context as IExtensionPackageContext)?.PackageDirectory
            ?? throw new InvalidOperationException("ClassIsland 需要插件包目录。");
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExusiAI", "ClassIsland");
        host = new ClassIslandHost(dataDirectory, packageDirectory);
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await Task.Run(host.Start, cancellationToken))
            Context.Logger.Warning($"原版 ClassIsland 无法启动：{host.LastStartupError}");
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Forwards the shell theme to the embedded island. The host keeps the last
    /// value and applies it once Avalonia finishes starting, so a theme pushed
    /// during startup is not dropped.
    /// </summary>
    public void ApplyHostTheme(HostTheme theme)
    {
        if (host is not null) host.ApplyTheme(theme);
    }

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("classisland.core", "ClassIsland", "◫", () => new ClassIslandPage(host))
    ];
}
