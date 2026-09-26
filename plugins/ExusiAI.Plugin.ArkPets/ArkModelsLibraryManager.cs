using System.IO;
using System.IO.Compression;

namespace ExusiAI.Plugin.ArkPets;

public sealed record ArkModelsVerificationResult(
    int TotalModels,
    int AvailableModels,
    IReadOnlyList<string> MissingModelKeys)
{
    public int MissingModels => MissingModelKeys.Count;
    public bool IsHealthy => MissingModels == 0;
}

internal static class ArkModelsLibraryManager
{
    private const long MaximumSingleModelBytes = 256L * 1024 * 1024;
    private const int MaximumSingleModelEntries = 32;

    public static ArkModelsVerificationResult Verify(ArkModelsCatalog catalog)
    {
        var missing = catalog.Models
            .Where(model => !model.IsAvailable)
            .Select(model => model.Key)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(catalog.Models.Count, catalog.Models.Count - missing.Length, missing);
    }

    public static async Task ExportAsync(
        ArkModelsCatalog catalog,
        string destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (string.IsNullOrWhiteSpace(catalog.RootDirectory) ||
            !File.Exists(Path.Combine(catalog.RootDirectory, "models_data.json")))
            throw new InvalidOperationException("当前没有可导出的 Ark-Models 模型库。");

        var root = Path.GetFullPath(catalog.RootDirectory);
        var destinationPath = Path.GetFullPath(destination);
        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("导出路径无效。");
        Directory.CreateDirectory(destinationDirectory);

        var temporary = destinationPath + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);

        try
        {
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var source in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var fullSource = Path.GetFullPath(source);
                        if (string.Equals(fullSource, destinationPath, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(fullSource, temporary, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var relative = Path.GetRelativePath(root, fullSource).Replace('\\', '/');
                        if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                            continue;

                        var entry = archive.CreateEntry($"ArkModels/{relative}", CompressionLevel.Optimal);
                        await using var input = new FileStream(
                            fullSource,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            81920,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await using var entryStream = entry.Open();
                        await input.CopyToAsync(entryStream, cancellationToken);
                    }
                }

                await output.FlushAsync(cancellationToken);
            }

            File.Move(temporary, destinationPath, true);
        }
        catch
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    public static async Task<string> ImportAsync(
        string archivePath,
        string destinationRoot,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(archivePath);
        if (!File.Exists(source))
            throw new FileNotFoundException("未找到 Ark-Models ZIP 文件。", source);

        Directory.CreateDirectory(destinationRoot);
        var importDirectory = Path.Combine(
            Path.GetFullPath(destinationRoot),
            $"ArkModels-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
        Directory.CreateDirectory(importDirectory);

        try
        {
            var rootPrefix = importDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using var archive = ZipFile.OpenRead(source);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(entry.FullName)) continue;

                var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var target = Path.GetFullPath(Path.Combine(importDirectory, relative));
                if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(target, importDirectory, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"模型压缩包包含越界路径：{entry.FullName}");

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

            var dataset = Directory.EnumerateFiles(importDirectory, "models_data.json", SearchOption.AllDirectories)
                .OrderBy(path => Path.GetRelativePath(importDirectory, path).Count(ch => ch is '/' or '\\'))
                .FirstOrDefault();
            if (dataset is null)
                throw new InvalidDataException("模型压缩包中没有 models_data.json。");

            var modelRoot = Path.GetDirectoryName(dataset)
                ?? throw new InvalidDataException("models_data.json 路径无效。");
            _ = await ArkModelsDataset.LoadAsync(modelRoot, cancellationToken);
            return modelRoot;
        }
        catch
        {
            try { Directory.Delete(importDirectory, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    public static async Task<ArkPetModel> ImportSingleModelAsync(
        ArkModelsCatalog catalog,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (catalog.Models.Count == 0 || string.IsNullOrWhiteSpace(catalog.RootDirectory))
            throw new InvalidOperationException("请先加载包含 models_data.json 的 Ark-Models 总模型库。");

        var source = Path.GetFullPath(archivePath);
        if (!File.Exists(source))
            throw new FileNotFoundException("未找到单模型 ZIP 文件。", source);

        using var archive = ZipFile.OpenRead(source);
        var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
        if (files.Length == 0 || files.Length > MaximumSingleModelEntries)
            throw new InvalidDataException("单模型压缩包为空或包含过多文件。");
        if (files.Sum(entry => entry.Length) > MaximumSingleModelBytes)
            throw new InvalidDataException("单模型压缩包解压后超过 256 MB。");

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = entry.FullName.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized.Split('/').Any(part => part == "..") ||
                !entries.TryAdd(entry.Name, entry))
                throw new InvalidDataException($"单模型压缩包包含不安全或重复的路径：{entry.FullName}");
        }

        var names = entries.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = catalog.Models.Where(model =>
        {
            var required = model.AssetFiles.Values.SelectMany(value => value).ToArray();
            return required.Length > 0 && required.All(names.Contains);
        }).ToArray();
        if (candidates.Length == 0)
            throw new InvalidDataException("压缩包中的资源文件无法与当前 models_data.json 中的模型对应。");
        if (candidates.Length > 1)
            throw new InvalidDataException($"压缩包同时匹配 {candidates.Length} 个模型，无法安全确定目标。");

        var model = candidates[0];
        Directory.CreateDirectory(model.AssetDirectory);
        foreach (var fileName in model.AssetFiles.Values.SelectMany(value => value).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[fileName];
            var destination = Path.Combine(model.AssetDirectory, fileName);
            var temporary = destination + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            try
            {
                await using (var input = entry.Open())
                await using (var output = new FileStream(
                    temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                    await input.CopyToAsync(output, cancellationToken);
                File.Move(temporary, destination, true);
            }
            catch
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                throw;
            }
        }

        if (!model.IsAvailable)
            throw new InvalidDataException("模型文件写入后仍未通过完整性校验。");
        return model;
    }
}
