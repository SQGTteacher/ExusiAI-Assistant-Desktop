using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.ClassIsland;

public sealed record ClassIslandBackupSummary(
    string ArchivePath,
    string RootPrefix,
    int ProfileFileCount,
    int ConfigFileCount,
    long TotalUncompressedBytes,
    IReadOnlyList<string> PreservedEntries)
{
    public int TotalFileCount => PreservedEntries.Count;
}

internal static class ClassIslandBackupImporter
{
    private const int MaximumEntries = 10_000;
    private const long MaximumSingleFileBytes = 64L * 1024 * 1024;
    private const long MaximumTotalBytes = 512L * 1024 * 1024;

    public static ClassIslandBackupSummary Inspect(string archivePath)
    {
        var source = Path.GetFullPath(archivePath);
        if (!File.Exists(source)) throw new FileNotFoundException("未找到 ClassIsland 备份压缩包。", source);
        if (!string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ClassIsland 自动备份必须是 ZIP 文件。");

        using var archive = ZipFile.OpenRead(source);
        if (archive.Entries.Count > MaximumEntries)
            throw new InvalidDataException($"备份包含过多条目（{archive.Entries.Count:N0}）。");

        var prefix = ResolveRootPrefix(archive);
        var preserved = new List<string>();
        long total = 0;
        var profiles = 0;
        var configs = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var relative = NormalizeEntry(entry.FullName, prefix);
            if (relative is null || string.IsNullOrEmpty(entry.Name) || !ShouldPreserve(relative)) continue;
            ValidateEntry(entry, relative, paths);
            ValidateSize(entry, ref total);
            preserved.Add(relative);
            if (relative.StartsWith("Profiles/", StringComparison.OrdinalIgnoreCase)) profiles++;
            if (relative.StartsWith("Config/", StringComparison.OrdinalIgnoreCase)) configs++;
        }

        if (!preserved.Any(x => x.Equals("Settings.json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("压缩包中未找到 ClassIsland 根目录 Settings.json。");

        return new(source, prefix, profiles, configs, total,
            preserved.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static async Task<ClassIslandBackupSummary> ImportIntoDataDirectoryAsync(
        string archivePath,
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        var summary = Inspect(archivePath);
        var dataRoot = Path.GetFullPath(dataDirectory);
        var parent = Directory.GetParent(dataRoot)?.FullName
            ?? throw new InvalidOperationException("ClassIsland data 目录无有效父目录。");
        Directory.CreateDirectory(parent);

        var token = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(parent, "data.sync-" + token);
        var rollback = Path.Combine(parent, "data.rollback-" + token);
        var keepRollback = false;
        Directory.CreateDirectory(staging);
        try
        {
            await ExtractAsync(summary, staging, cancellationToken);
            _ = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(staging, "Settings.json"), cancellationToken)) as JsonObject
                ?? throw new InvalidDataException("备份中的 Settings.json 根节点无效。");

            Directory.CreateDirectory(dataRoot);
            BackupManaged(dataRoot, rollback);
            try
            {
                DeleteManaged(dataRoot);
                MoveManaged(staging, dataRoot);
            }
            catch
            {
                DeleteManaged(dataRoot);
                try { RestoreManaged(rollback, dataRoot); }
                catch { keepRollback = true; }
                throw;
            }

            TryDelete(rollback);
            return summary;
        }
        finally
        {
            TryDelete(staging);
            if (!keepRollback) TryDelete(rollback);
        }
    }

    private static async Task ExtractAsync(ClassIslandBackupSummary summary, string staging, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(summary.ArchivePath);
        long total = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeEntry(entry.FullName, summary.RootPrefix);
            if (relative is null || string.IsNullOrEmpty(entry.Name) || !ShouldPreserve(relative)) continue;
            ValidateEntry(entry, relative, paths);
            ValidateSize(entry, ref total);
            var target = ResolveInsideRoot(staging, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static void BackupManaged(string dataRoot, string rollback)
    {
        Directory.CreateDirectory(rollback);
        var settings = Path.Combine(dataRoot, "Settings.json");
        if (File.Exists(settings)) File.Copy(settings, Path.Combine(rollback, "Settings.json"), true);
        CopyDirectory(Path.Combine(dataRoot, "Profiles"), Path.Combine(rollback, "Profiles"));
        CopyDirectory(Path.Combine(dataRoot, "Config"), Path.Combine(rollback, "Config"));
    }

    private static void RestoreManaged(string rollback, string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        var settings = Path.Combine(rollback, "Settings.json");
        if (File.Exists(settings)) File.Copy(settings, Path.Combine(dataRoot, "Settings.json"), true);
        CopyDirectory(Path.Combine(rollback, "Profiles"), Path.Combine(dataRoot, "Profiles"));
        CopyDirectory(Path.Combine(rollback, "Config"), Path.Combine(dataRoot, "Config"));
    }

    private static void MoveManaged(string staging, string dataRoot)
    {
        File.Move(Path.Combine(staging, "Settings.json"), Path.Combine(dataRoot, "Settings.json"), true);
        MoveDirectory(Path.Combine(staging, "Profiles"), Path.Combine(dataRoot, "Profiles"));
        MoveDirectory(Path.Combine(staging, "Config"), Path.Combine(dataRoot, "Config"));
    }

    private static void DeleteManaged(string dataRoot)
    {
        var settings = Path.Combine(dataRoot, "Settings.json");
        if (File.Exists(settings)) File.Delete(settings);
        foreach (var name in new[] { "Profiles", "Config" })
        {
            var path = Path.Combine(dataRoot, name);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void MoveDirectory(string source, string destination)
    {
        if (Directory.Exists(source)) Directory.Move(source, destination);
    }

    private static string ResolveRootPrefix(ZipArchive archive)
    {
        if (archive.Entries.Any(x => NormalizeSlashes(x.FullName).Equals("Settings.json", StringComparison.OrdinalIgnoreCase)))
            return "";
        var candidates = archive.Entries.Select(x => NormalizeSlashes(x.FullName))
            .Where(x => x.EndsWith("/Settings.json", StringComparison.OrdinalIgnoreCase))
            .Select(x => x[..^"Settings.json".Length])
            .OrderBy(x => x.Count(ch => ch == '/')).ThenBy(x => x.Length).ToArray();
        return candidates.FirstOrDefault(prefix => archive.Entries.Any(x =>
        {
            var name = NormalizeSlashes(x.FullName);
            return name.StartsWith(prefix + "Profiles/", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(prefix + "Config/", StringComparison.OrdinalIgnoreCase);
        })) ?? candidates.FirstOrDefault() ?? "";
    }

    private static string? NormalizeEntry(string entryName, string rootPrefix)
    {
        var normalized = NormalizeSlashes(entryName);
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains(':'))
            throw new InvalidDataException($"压缩包包含无效路径：{entryName}");
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(x => x is "." or ".."))
            throw new InvalidDataException($"压缩包包含路径穿越条目：{entryName}");
        if (!string.IsNullOrEmpty(rootPrefix))
        {
            if (!normalized.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) return null;
            normalized = normalized[rootPrefix.Length..];
        }
        return normalized.TrimStart('/');
    }

    private static string NormalizeSlashes(string path) => path.Replace('\\', '/').TrimStart();
    private static void ValidateEntry(ZipArchiveEntry entry, string relative, HashSet<string> paths)
    {
        // ZIP external attributes encode Unix file types in the upper 16 bits.
        if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            throw new InvalidDataException($"备份包含符号链接：{entry.FullName}");
        if (!paths.Add(relative))
            throw new InvalidDataException($"备份包含重复路径：{entry.FullName}");
    }
    private static bool ShouldPreserve(string relative) =>
        relative.Equals("Settings.json", StringComparison.OrdinalIgnoreCase) ||
        relative.StartsWith("Profiles/", StringComparison.OrdinalIgnoreCase) ||
        relative.StartsWith("Config/", StringComparison.OrdinalIgnoreCase);

    private static void ValidateSize(ZipArchiveEntry entry, ref long total)
    {
        if (entry.Length < 0 || entry.Length > MaximumSingleFileBytes)
            throw new InvalidDataException($"备份条目过大：{entry.FullName}");
        total = checked(total + entry.Length);
        if (total > MaximumTotalBytes) throw new InvalidDataException("备份解压后的配置数据超过安全上限。");
    }

    private static string ResolveInsideRoot(string root, string relative)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"压缩包条目越过工作区边界：{relative}");
        return target;
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}
