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
}
