using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;

namespace ExusiAI.Plugin.FileViewer;

public sealed class FileViewerPlugin : ExtensionPluginBase, IWpfNavigationExtension
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Context.Logger.Information("File viewer provider host started.");
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        Context.Logger.Information("File viewer provider host stopped.");
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<WpfNavigationPage> GetNavigationPages() =>
    [
        new("file-viewer.settings", "文件查看器", "▤", static () => new FileViewerSettingsPage())
    ];
}
