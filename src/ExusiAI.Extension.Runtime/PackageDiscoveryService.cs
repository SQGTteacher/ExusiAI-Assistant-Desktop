using System.Collections.Immutable;
using System.Text.Json;
using ExusiAI.Extension.Abstractions;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Extension.Runtime;

public sealed record DiscoveredPackage(string RootPath, string ManifestPath, PackageManifest Manifest);
public sealed record PackageDiscoveryFailure(string Path, string Code, string Message);
public sealed record PackageDiscoveryResult(ImmutableArray<DiscoveredPackage> Packages, ImmutableArray<PackageDiscoveryFailure> Failures);

public sealed class PackageDiscoveryService(ManifestParser parser, ManifestValidator validator, ILogger<PackageDiscoveryService> logger)
{
    public async Task<PackageDiscoveryResult> DiscoverAsync(string packagesRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagesRoot);
        var packages = ImmutableArray.CreateBuilder<DiscoveredPackage>();
        var failures = ImmutableArray.CreateBuilder<PackageDiscoveryFailure>();
        if (!Directory.Exists(packagesRoot)) return new([], []);

        IEnumerable<string> directories;
        try { directories = Directory.EnumerateDirectories(packagesRoot).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Cannot enumerate package root {PackageRoot}.", packagesRoot);
            return new([], [new(packagesRoot, "package-root-unavailable", "The package root cannot be read.")]);
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(directory, "package.json");
            if (!File.Exists(manifestPath))
            {
                failures.Add(new(directory, "missing-manifest", "package.json was not found."));
                continue;
            }

            try
            {
                var manifest = await parser.ParseAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                var validation = validator.Validate(manifest, directory);
                if (!validation.IsValid)
                {
                    failures.Add(new(directory, "invalid-manifest", string.Join("; ", validation.Errors.Select(x => $"{x.Code}: {x.Message}"))));
                    continue;
                }
                if (!ids.Add(manifest.Id))
                {
                    failures.Add(new(directory, "duplicate-id", $"Package id '{manifest.Id}' is duplicated."));
                    continue;
                }
                packages.Add(new(directory, manifestPath, manifest));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning(exception, "Package manifest {ManifestPath} could not be read.", manifestPath);
                failures.Add(new(directory, "manifest-read-failed", "The manifest could not be parsed safely."));
            }
        }
        return new(packages.ToImmutable(), failures.ToImmutable());
    }
}
