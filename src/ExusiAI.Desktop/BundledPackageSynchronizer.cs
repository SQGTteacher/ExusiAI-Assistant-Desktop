using System.IO;
using ExusiAI.Extension.Runtime;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Desktop;

public sealed class BundledPackageSynchronizer(IAppPaths paths, ISettingsService settings, ManifestParser parser, ILogger<BundledPackageSynchronizer> logger)
{
    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        var sourceRoot = Path.Combine(paths.ApplicationDirectory, "packages");
        if (!Directory.Exists(sourceRoot)) return;
        var removed = (settings.Current.RemovedBundledPackages ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Directory.EnumerateDirectories(sourceRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(source, "package.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var manifest = await parser.ParseAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                if (removed.Contains(manifest.Id)) continue;
                var destination = Path.Combine(paths.PackagesDirectory, manifest.Id);
                if (Directory.Exists(destination))
                {
                    var installedManifestPath = Path.Combine(destination, "package.json");
                    if (File.Exists(installedManifestPath))
                    {
                        var installed = await parser.ParseAsync(installedManifestPath, cancellationToken).ConfigureAwait(false);
                        if (SemanticVersion.TryParse(installed.Version, out var current) &&
                            SemanticVersion.TryParse(manifest.Version, out var bundled) && current.CompareTo(bundled) >= 0) continue;
                    }
                }
                ReplaceDirectory(source, destination);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                logger.LogWarning(exception, "Bundled package {PackagePath} could not be synchronized.", source);
            }
        }
    }

    private static void ReplaceDirectory(string source, string destination)
    {
        var staging = destination + $".install-{Guid.NewGuid():N}";
        var backup = destination + $".backup-{Guid.NewGuid():N}";
        CopyDirectory(source, staging);
        try
        {
            if (Directory.Exists(destination)) Directory.Move(destination, backup);
            Directory.Move(staging, destination);
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
        catch
        {
            if (!Directory.Exists(destination) && Directory.Exists(backup)) Directory.Move(backup, destination);
            throw;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
    }
}
