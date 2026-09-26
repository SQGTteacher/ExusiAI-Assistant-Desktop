using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.Runtime;
using ExusiAI.Infrastructure;

namespace ExusiAI.Desktop;

public sealed class PluginPackageManager(
    ExtensionRuntime runtime,
    PackageArchiveService archives,
    ManifestParser parser,
    IAppPaths paths,
    ISettingsService settings)
{
    public string PackagesDirectory => paths.PackagesDirectory;

    public async Task<PackageManifest> ImportAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        var installed = await archives.InstallAsync(archivePath, paths.PackagesDirectory, paths.TempDirectory, cancellationToken);
        try
        {
            await runtime.AddAndStartAsync(installed.Package, cancellationToken);
            var removed = (settings.Current.RemovedBundledPackages ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (removed.Remove(installed.Package.Manifest.Id))
                await settings.SaveAsync(settings.Current with { RemovedBundledPackages = removed.OrderBy(x => x).ToArray() }, cancellationToken);
            return installed.Package.Manifest;
        }
        catch
        {
            try { Directory.Delete(installed.InstalledPath, true); } catch { }
            throw;
        }
    }

    public Task ExportAsync(string packageId, string destinationArchive, CancellationToken cancellationToken = default)
    {
        var package = Find(packageId).Package;
        return archives.ExportAsync(package, destinationArchive, cancellationToken);
    }

    public async Task UninstallAsync(string packageId, CancellationToken cancellationToken = default)
    {
        _ = Find(packageId);
        var package = await runtime.RemoveAsync(packageId, cancellationToken);
        var disabled = (settings.Current.DisabledPackages ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        disabled.Remove(packageId);
        var removed = (settings.Current.RemovedBundledPackages ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (await IsBundledAsync(packageId, cancellationToken)) removed.Add(packageId);
        await settings.SaveAsync(settings.Current with
        {
            DisabledPackages = disabled.OrderBy(x => x).ToArray(),
            RemovedBundledPackages = removed.OrderBy(x => x).ToArray()
        }, cancellationToken);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        PackageArchiveService.Uninstall(package, paths.PackagesDirectory);
    }

    private ExtensionRuntimeEntry Find(string packageId) => runtime.Entries.FirstOrDefault(item => string.Equals(item.Package.Manifest.Id, packageId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"未找到插件 {packageId}。 ");

    private async Task<bool> IsBundledAsync(string packageId, CancellationToken cancellationToken)
    {
        var root = Path.Combine(paths.ApplicationDirectory, "packages");
        if (!Directory.Exists(root)) return false;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var manifestPath = Path.Combine(directory, "package.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var manifest = await parser.ParseAsync(manifestPath, cancellationToken);
                if (string.Equals(manifest.Id, packageId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }
        return false;
    }
}
