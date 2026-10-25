using System.IO;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.ClassIsland;

public sealed class ClassIslandPlugin : ExtensionPluginBase, ISuspendablePlugin, IDisposable, IWpfNavigationExtension, IWpfHostThemeExtension, IWpfTrayExtension
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
        if (host.ShowOnHostStartup && !await Task.Run(() => host.StartWithCancellation(cancellationToken), cancellationToken))
            Context.Logger.Warning($"ClassIsland 自动显示失败：{host.LastStartupError}");
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        host.Dispose();
        return Task.CompletedTask;
    }

    public Task SuspendAsync(CancellationToken cancellationToken) => host.SuspendAsync(cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken) => host.ResumeAsync(cancellationToken);

    public void Dispose() => host?.Dispose();

    /// <summary>
    /// Forwards the shell theme to the embedded island. The host keeps the last
    /// value and applies it once Avalonia finishes starting, so a theme pushed
    /// during startup is not dropped.
    /// </summary>
    public void ApplyHostTheme(HostTheme theme)
    {
        if (host is not null) host.ApplyTheme(theme);
    }

    public IReadOnlyCollection<WpfTrayCommand> GetTrayCommands() =>
    [
        Command("class-swap", "临时换课", token => host.OpenPageWithCancellation("class-swap", token)),
        Command("profile", "编辑课表", token => host.OpenPageWithCancellation("profile", token)),
        Command("edit", "编辑信息岛组件", token => host.OpenPageWithCancellation("edit", token)),
        Command("settings", "原版设置", token => host.OpenPageWithCancellation("settings", token)),
        Command("show", "显示信息岛", token => host.StartWithCancellation(token)),
        Command("hide", "隐藏信息岛", token => host.HideWithCancellation(token))
    ];

    private WpfTrayCommand Command(string id, string title, Func<CancellationToken, bool> action) => new(id, title,
        async cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await Task.Run(() => action(cancellationToken), cancellationToken))
                throw new InvalidOperationException(host.IsBusy ? "ClassIsland 正在执行其他操作，请稍后再试。" : host.LastStartupError ?? "ClassIsland 当前无法执行此操作。");
        });

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("classisland.core", "ClassIsland", "◫", () => new ClassIslandPage(host))
    ];
}
