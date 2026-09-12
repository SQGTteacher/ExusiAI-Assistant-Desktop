using System.Collections.Immutable;
using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Marketplace;

public interface IPackageCatalog
{
    Task<IReadOnlyList<PackageManifest>> GetPackagesAsync(CancellationToken cancellationToken = default);
}

public interface IPackageRepository
{
    Task<IReadOnlyList<PackageManifest>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

public interface IPackageInstaller
{
    Task<bool> InstallAsync(PackageManifest package, CancellationToken cancellationToken = default);
}

public sealed class PlaceholderPackageCatalog : IPackageCatalog, IPackageRepository, IPackageInstaller
{
    public Task<IReadOnlyList<PackageManifest>> GetPackagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PackageManifest>>(ImmutableArray<PackageManifest>.Empty);

    public Task<IReadOnlyList<PackageManifest>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PackageManifest>>(ImmutableArray<PackageManifest>.Empty);

    public Task<bool> InstallAsync(PackageManifest package, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
