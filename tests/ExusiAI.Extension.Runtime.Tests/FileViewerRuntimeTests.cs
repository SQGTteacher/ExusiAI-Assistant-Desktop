using System.Text.Json;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.Runtime;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;
using ExusiAI.FileViewer.Core;
using ExusiAI.Plugin.FileViewer;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class FileViewerRuntimeTests
{
    [Fact]
    public async Task PackageLocalCoreDependencyLoadsAndPageCanBeCreated()
    {
        var root = Path.Combine(Path.GetTempPath(), "exusiai-file-viewer-tests", Guid.NewGuid().ToString("N"));
        var package = Path.Combine(root, "file-viewer");
        Directory.CreateDirectory(package);
        try
        {
            var manifest = new PackageManifest
            {
                SchemaVersion = 1,
                Id = "exusiai.file-viewer",
                Type = PackageType.Plugin,
                DisplayName = "File Viewer",
                Version = "0.1.0",
                ApiVersion = "1",
                Publisher = "ExusiAI",
                MinimumHostVersion = "0.2.0-preview.4",
                EntryPoint = new()
                {
                    Assembly = "ExusiAI.Plugin.FileViewer.dll",
                    Type = "ExusiAI.Plugin.FileViewer.FileViewerPlugin"
                }
            };
            await File.WriteAllTextAsync(
                Path.Combine(package, "package.json"),
                JsonSerializer.Serialize(manifest, ManifestJson.Options));
            File.Copy(typeof(FileViewerPlugin).Assembly.Location, Path.Combine(package, "ExusiAI.Plugin.FileViewer.dll"));
            File.Copy(typeof(FileViewerProviderRegistry).Assembly.Location, Path.Combine(package, "ExusiAI.FileViewer.Core.dll"));
            File.Copy(typeof(ExtensionPluginBase).Assembly.Location, Path.Combine(package, "ExusiAI.Extension.SDK.dll"));

            await using (var runtime = new ExtensionRuntime(
                new(new(), new(), NullLogger<PackageDiscoveryService>.Instance),
                NullLogger<ExtensionRuntime>.Instance))
            {
                await runtime.DiscoverAsync(root);
                Assert.Empty(runtime.DiscoveryFailures);
                await runtime.StartAsync();

                var entry = Assert.Single(runtime.Entries);
                Assert.Equal(PackageState.Running, entry.State);
                var navigation = Assert.IsAssignableFrom<IWpfNavigationExtension>(entry.Instance);
                var page = Assert.Single(navigation.GetNavigationPages());
                Assert.Equal("file-viewer.open", page.Route);
                Assert.Null(CreatePageOnStaThread(page));
            }
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Directory.Delete(root, true);
        }
    }

    private static Exception? CreatePageOnStaThread(WpfNavigationPage page)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var view = page.CreateView();
                view.Measure(new System.Windows.Size(1280, 800));
                view.Arrange(new System.Windows.Rect(0, 0, 1280, 800));
                view.UpdateLayout();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15))) return new TimeoutException("File Viewer page smoke test timed out.");
        return failure;
    }
}
