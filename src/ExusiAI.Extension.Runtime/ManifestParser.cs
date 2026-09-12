using System.Text.Json;
using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Extension.Runtime;

public sealed class ManifestParser
{
    public const long MaximumManifestBytes = 256 * 1024;

    public PackageManifest Parse(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ValidateLength(manifestPath);
        using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        return JsonSerializer.Deserialize<PackageManifest>(stream, ManifestJson.Options) ?? throw new JsonException("The manifest is empty.");
    }

    public async Task<PackageManifest> ParseAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ValidateLength(manifestPath);
        await using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<PackageManifest>(stream, ManifestJson.Options, cancellationToken).ConfigureAwait(false)
            ?? throw new JsonException("The manifest is empty.");
    }

    private static void ValidateLength(string path)
    {
        if (new FileInfo(path).Length > MaximumManifestBytes) throw new JsonException("The manifest exceeds the maximum allowed size.");
    }
}
