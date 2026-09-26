using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace ExusiAI.Plugin.ArkPets;

internal sealed record ArkPetsRuntimeRelease(
    string Version,
    string AssetName,
    Uri DownloadUri,
    string? Sha256);

internal sealed record ArkPetsDownloadProgress(string Stage, long ReceivedBytes, long? TotalBytes)
{
    public double? Ratio => TotalBytes is > 0
        ? Math.Clamp((double)ReceivedBytes / TotalBytes.Value, 0, 1)
        : null;
}

internal sealed class ArkPetsUpstreamManager : IDisposable
{
    private const string LatestReleaseApi = "https://api.github.com/repos/isHarryh/Ark-Pets/releases/latest";
    private const string ModelsArchiveUrl = "https://github.com/isHarryh/Ark-Models/archive/refs/heads/main.zip";
    private readonly Func<string?> proxyProvider;

    public ArkPetsUpstreamManager(Func<string?> proxyProvider)
    {
        this.proxyProvider = proxyProvider;
    }

    public async Task<ArkPetsRuntimeRelease> QueryLatestRuntimeAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(proxyProvider());
        var json = await client.GetStringAsync(LatestReleaseApi, cancellationToken);
        return ParseRuntimeRelease(json);
    }

    public async Task<(string RuntimePath, string Version)> InstallLatestRuntimeAsync(
        IProgress<ArkPetsDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var release = await QueryLatestRuntimeAsync(cancellationToken);
        var localRoot = LocalRoot;
        var runtimeRoot = Path.Combine(localRoot, "runtime", SanitizeDirectoryName(release.Version));

        var existing = Directory.Exists(runtimeRoot)
            ? Directory.EnumerateFiles(runtimeRoot, "ArkPets.exe", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (!string.IsNullOrWhiteSpace(existing))
            return (existing, release.Version);

        var downloads = Path.Combine(localRoot, "downloads");
        Directory.CreateDirectory(downloads);
        var archivePath = Path.Combine(downloads, release.AssetName);
        await DownloadFileAsync(release.DownloadUri, archivePath, release.Sha256, progress, cancellationToken);

        var temporaryRoot = runtimeRoot + ".installing-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            await ExtractArchiveSafeAsync(archivePath, temporaryRoot, cancellationToken);
            var executable = Directory.EnumerateFiles(temporaryRoot, "ArkPets.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (executable is null)
                throw new InvalidDataException("ArkPets 上游便携 ZIP 中没有 ArkPets.exe。");

            var relativeExecutable = Path.GetRelativePath(temporaryRoot, executable);
            if (Directory.Exists(runtimeRoot))
                Directory.Delete(runtimeRoot, true);
            Directory.Move(temporaryRoot, runtimeRoot);
            return (Path.Combine(runtimeRoot, relativeExecutable), release.Version);
        }
        catch
        {
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    public async Task<string> InstallLatestModelsAsync(
        IProgress<ArkPetsDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var downloads = Path.Combine(LocalRoot, "downloads");
        Directory.CreateDirectory(downloads);
        var archivePath = Path.Combine(downloads, "ArkModels-main.zip");
        await DownloadFileAsync(new Uri(ModelsArchiveUrl), archivePath, null, progress, cancellationToken);

        var destinationRoot = Path.Combine(LocalRoot, "libraries");
        return await ArkModelsLibraryManager.ImportAsync(archivePath, destinationRoot, cancellationToken);
    }

    public void Dispose()
    {
    }

    internal static ArkPetsRuntimeRelease ParseRuntimeRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagNode) && tagNode.ValueKind == JsonValueKind.String
            ? tagNode.GetString() ?? ""
            : "";
        if (string.IsNullOrWhiteSpace(tag))
            throw new InvalidDataException("ArkPets GitHub Release 缺少版本标签。");

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("ArkPets GitHub Release 缺少资产列表。");

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameNode) && nameNode.ValueKind == JsonValueKind.String
                ? nameNode.GetString() ?? ""
                : "";
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                continue;

            var url = asset.TryGetProperty("browser_download_url", out var urlNode) && urlNode.ValueKind == JsonValueKind.String
                ? urlNode.GetString()
                : null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var downloadUri))
                continue;

            var digest = asset.TryGetProperty("digest", out var digestNode) && digestNode.ValueKind == JsonValueKind.String
                ? digestNode.GetString()
                : null;
            var sha256 = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
                ? digest["sha256:".Length..]
                : null;

            return new ArkPetsRuntimeRelease(tag.TrimStart('v', 'V'), name, downloadUri, sha256);
        }

        throw new InvalidDataException("ArkPets GitHub Release 中没有 ZIP 便携包。");
    }

    internal static async Task ExtractArchiveSafeAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        var root = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(destinationDirectory, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"ArkPets ZIP 包含越界路径：{entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(
                target,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    internal static string LocalRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExusiAI", "arkpets");

    private async Task DownloadFileAsync(
        Uri uri,
        string destination,
        string? expectedSha256,
        IProgress<ArkPetsDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(proxyProvider());
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var temporary = destination + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(temporary))
            File.Delete(temporary);

        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var total = response.Content.Headers.ContentLength;
                var buffer = new byte[81920];
                long received = 0;
                while (true)
                {
                    var count = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (count == 0)
                        break;
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    received += count;
                    progress?.Report(new ArkPetsDownloadProgress("download", received, total));
                }
                await output.FlushAsync(cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                await using var stream = File.OpenRead(temporary);
                var digest = await SHA256.HashDataAsync(stream, cancellationToken);
                var actual = Convert.ToHexString(digest);
                if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("ArkPets 运行时 SHA-256 校验失败。");
            }

            File.Move(temporary, destination, true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    private static HttpClient CreateClient(string? proxy)
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(proxy) &&
            Uri.TryCreate(proxy, UriKind.Absolute, out var proxyUri) &&
            proxyUri.Scheme is "http" or "https")
        {
            handler.Proxy = new WebProxy(proxyUri);
            handler.UseProxy = true;
        }

        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ExusiAI-ArkPets/0.2");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string SanitizeDirectoryName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "runtime" : result;
    }
}
