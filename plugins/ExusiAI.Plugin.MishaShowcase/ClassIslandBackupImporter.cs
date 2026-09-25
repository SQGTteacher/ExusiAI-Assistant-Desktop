using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed record ClassIslandBackupSummary(
    string ArchivePath,
    string RootPrefix,
    int ProfileFileCount,
    int ConfigFileCount,
    long TotalUncompressedBytes,
    IReadOnlyList<string> PreservedEntries)
{
    public int TotalFileCount => PreservedEntries.Count;
}

internal sealed record ImportedClassIslandBackup(
    string SettingsPath,
    string SourceArchivePath,
    ClassIslandBackupSummary Summary);

/// <summary>
/// Reads the same Settings.json + Profiles + Config structure written by ClassIsland 2.x
/// FileFolderService.CreateBackupAsync. The import target is always an isolated ExusiAI copy.
/// </summary>
internal static class ClassIslandBackupImporter
{
    private const int MaximumEntries = 10_000;
    private const long MaximumSingleFileBytes = 64L * 1024 * 1024;
    private const long MaximumTotalBytes = 512L * 1024 * 1024;

    public static ClassIslandBackupSummary Inspect(string archivePath)
    {
        var source = Path.GetFullPath(archivePath);
        if (!File.Exists(source))
            throw new FileNotFoundException("未找到 ClassIsland 备份压缩包。", source);
        if (!string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ClassIsland 自动备份必须是 ZIP 文件。");

        using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > MaximumEntries)
            throw new InvalidDataException($"备份包含过多条目（{archive.Entries.Count:N0}），已拒绝解析。");

        var rootPrefix = ResolveRootPrefix(archive);
        var preserved = new List<string>();
        long totalBytes = 0;
        var profiles = 0;
        var configs = 0;

        foreach (var entry in archive.Entries)
        {
            var relative = NormalizeEntry(entry.FullName, rootPrefix);
            if (relative is null || string.IsNullOrEmpty(entry.Name) || !ShouldPreserve(relative))
                continue;

            ValidateSize(entry, ref totalBytes);
            preserved.Add(relative);
            if (relative.StartsWith("Profiles/", StringComparison.OrdinalIgnoreCase)) profiles++;
            else if (relative.StartsWith("Config/", StringComparison.OrdinalIgnoreCase)) configs++;
        }

        if (!preserved.Any(x => x.Equals("Settings.json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("压缩包中未找到 ClassIsland 根目录 Settings.json。");

        return new(
            source,
            rootPrefix,
            profiles,
            configs,
            totalBytes,
            preserved.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static async Task<ImportedClassIslandBackup> ImportAsync(
        string archivePath,
        string destinationRoot,
        CancellationToken cancellationToken = default)
    {
        var summary = Inspect(archivePath);
        Directory.CreateDirectory(destinationRoot);

        var archiveName = SanitizeName(Path.GetFileNameWithoutExtension(summary.ArchivePath));
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(summary.ArchivePath.ToUpperInvariant())))[..10];
        var destination = Path.Combine(
            destinationRoot,
            $"{archiveName}-{hash}-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
        var staging = destination + ".staging";

        Directory.CreateDirectory(staging);
        try
        {
            using var stream = new FileStream(summary.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            long totalBytes = 0;

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = NormalizeEntry(entry.FullName, summary.RootPrefix);
                if (relative is null || string.IsNullOrEmpty(entry.Name) || !ShouldPreserve(relative))
                    continue;

                ValidateSize(entry, ref totalBytes);
                var target = ResolveInsideRoot(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                await using var input = entry.Open();
                await using var output = new FileStream(
                    target,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);
                await input.CopyToAsync(output, cancellationToken);
            }

            var settingsPath = Path.Combine(staging, "Settings.json");
            var parsedSettings = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath, cancellationToken)) as JsonObject
                ?? throw new InvalidDataException("备份中的 Settings.json 根节点无效。");
            _ = parsedSettings.Count;

            await File.WriteAllTextAsync(
                Path.Combine(staging, ".exusiai-source.txt"),
                $"SourceBackup={summary.ArchivePath}{Environment.NewLine}Imported={DateTimeOffset.Now:O}{Environment.NewLine}",
                Encoding.UTF8,
                cancellationToken);

            Directory.Move(staging, destination);
            return new(
                Path.Combine(destination, "Settings.json"),
                summary.ArchivePath,
                summary);
        }
        catch
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch
            {
            }
            throw;
        }
    }

    private static string ResolveRootPrefix(ZipArchive archive)
    {
        if (archive.Entries.Any(x =>
                NormalizeSlashes(x.FullName).Equals("Settings.json", StringComparison.OrdinalIgnoreCase)))
            return string.Empty;

        var candidates = archive.Entries
            .Select(x => NormalizeSlashes(x.FullName))
            .Where(x => x.EndsWith("/Settings.json", StringComparison.OrdinalIgnoreCase))
            .Select(x => x[..^"Settings.json".Length])
            .OrderBy(x => x.Count(ch => ch == '/'))
            .ThenBy(x => x.Length)
            .ToArray();

        foreach (var prefix in candidates)
        {
            if (archive.Entries.Any(x =>
            {
                var name = NormalizeSlashes(x.FullName);
                return name.StartsWith(prefix + "Profiles/", StringComparison.OrdinalIgnoreCase) ||
                       name.StartsWith(prefix + "Config/", StringComparison.OrdinalIgnoreCase);
            }))
                return prefix;
        }

        return candidates.FirstOrDefault() ?? string.Empty;
    }

    private static string? NormalizeEntry(string entryName, string rootPrefix)
    {
        var normalized = NormalizeSlashes(entryName);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains(':'))
            throw new InvalidDataException($"压缩包包含无效路径：{entryName}");

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(x => x is "." or ".."))
            throw new InvalidDataException($"压缩包包含路径穿越条目：{entryName}");

        if (!string.IsNullOrEmpty(rootPrefix))
        {
            if (!normalized.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return null;
            normalized = normalized[rootPrefix.Length..];
        }

        return normalized.TrimStart('/');
    }

    private static string NormalizeSlashes(string path) => path.Replace('\\', '/').TrimStart();

    private static bool ShouldPreserve(string relative) =>
        relative.Equals("Settings.json", StringComparison.OrdinalIgnoreCase) ||
        relative.StartsWith("Profiles/", StringComparison.OrdinalIgnoreCase) ||
        relative.StartsWith("Config/", StringComparison.OrdinalIgnoreCase);

    private static void ValidateSize(ZipArchiveEntry entry, ref long totalBytes)
    {
        if (entry.Length < 0 || entry.Length > MaximumSingleFileBytes)
            throw new InvalidDataException($"备份条目过大：{entry.FullName}");

        totalBytes = checked(totalBytes + entry.Length);
        if (totalBytes > MaximumTotalBytes)
            throw new InvalidDataException("备份解压后的配置数据超过安全上限。");
    }

    private static string ResolveInsideRoot(string root, string relative)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"压缩包条目越过工作区边界：{relative}");
        return candidate;
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var value = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(value) ? "ClassIslandBackup" : value;
    }
}
