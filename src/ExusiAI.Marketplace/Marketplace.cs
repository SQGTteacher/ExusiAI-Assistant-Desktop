using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Marketplace;

public interface IPackageCatalog
{
    Task<IReadOnlyList<PackageManifest>> SearchAsync(string? query = null, CancellationToken cancellationToken = default);
}

public sealed class LocalPackageCatalog(Func<IEnumerable<PackageManifest>> packageSource) : IPackageCatalog
{
    public Task<IReadOnlyList<PackageManifest>> SearchAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = query?.Trim();
        var packages = packageSource()
            .Where(x => string.IsNullOrWhiteSpace(normalized) || Matches(x, normalized))
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyList<PackageManifest>>(packages);
    }

    private static bool Matches(PackageManifest package, string query) =>
        package.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        package.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        package.Publisher.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        package.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        package.Type.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
}
