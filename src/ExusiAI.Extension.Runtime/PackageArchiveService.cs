using System.IO.Compression;

namespace ExusiAI.Extension.Runtime;

public sealed record PackageInstallResult(DiscoveredPackage Package, string InstalledPath);

public sealed class PackageArchiveService(PackageDiscoveryService discovery)
{
    private const int MaximumEntries = 4096;
    private const long MaximumExpandedBytes = 512L * 1024 * 1024;

    public async Task<PackageInstallResult> InstallAsync(
        string archivePath,
        string packagesRoot,
        string temporaryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!File.Exists(archivePath)) throw new FileNotFoundException("插件压缩包不存在。", archivePath);
        Directory.CreateDirectory(packagesRoot);
        Directory.CreateDirectory(temporaryRoot);
        var staging = Path.Combine(temporaryRoot, $"plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            ExtractSafely(archivePath, staging, cancellationToken);
            var packageRoot = LocatePackageRoot(staging);
            var result = await discovery.DiscoverAsync(Path.GetDirectoryName(packageRoot)!, cancellationToken).ConfigureAwait(false);
            var package = result.Packages.SingleOrDefault(item => PathsEqual(item.RootPath, packageRoot));
            if (package is null)
            {
                var failure = result.Failures.FirstOrDefault(item => PathsEqual(item.Path, packageRoot));
                throw new InvalidDataException(failure?.Message ?? "压缩包中没有有效的 package.json。只允许包含一个插件包。 ");
            }

            var destination = Path.Combine(packagesRoot, package.Manifest.Id);
            if (Directory.Exists(destination))
                throw new IOException($"插件 {package.Manifest.DisplayName} 已安装，请先卸载现有版本。 ");
            Directory.Move(packageRoot, destination);
            return new(new(destination, Path.Combine(destination, "package.json"), package.Manifest), destination);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public Task ExportAsync(DiscoveredPackage package, string destinationArchive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationArchive);
        var destinationFullPath = Path.GetFullPath(destinationArchive);
        var sourceRoot = Path.GetFullPath(package.RootPath);
        if (!Directory.Exists(sourceRoot)) throw new DirectoryNotFoundException("插件目录不存在。 ");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath)!);
        var temporary = destinationFullPath + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
                    archive.CreateEntryFromFile(file, $"{package.Manifest.Id}/{relative}", CompressionLevel.Optimal);
                }
            }
            File.Move(temporary, destinationFullPath, true);
            return Task.CompletedTask;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void Uninstall(DiscoveredPackage package, string packagesRoot)
    {
        var root = Path.GetFullPath(packagesRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var packagePath = Path.GetFullPath(package.RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(packagePath), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只能卸载统一插件目录中的包。 ");
        Directory.Delete(packagePath, recursive: true);
    }

    private static void ExtractSafely(string archivePath, string destination, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntries)
            throw new InvalidDataException("插件压缩包为空或文件数量过多。 ");
        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            expandedBytes += entry.Length;
            if (expandedBytes > MaximumExpandedBytes) throw new InvalidDataException("插件解压后超过 512 MB 安全限制。 ");
            var normalized = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || normalized.Contains(':'))
                throw new InvalidDataException("插件压缩包包含不安全路径。 ");
            var target = Path.GetFullPath(Path.Combine(destination, normalized.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("插件压缩包试图写入目标目录之外。 ");
            if (normalized.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }

    private static string LocatePackageRoot(string staging)
    {
        var manifests = Directory.EnumerateFiles(staging, "package.json", SearchOption.AllDirectories)
            .Where(path => Path.GetRelativePath(staging, path).Split(Path.DirectorySeparatorChar).Length <= 2)
            .ToArray();
        if (manifests.Length != 1) throw new InvalidDataException("压缩包必须包含且只能包含一个根级插件 package.json。 ");
        return Path.GetDirectoryName(manifests[0])!;
    }

    private static bool PathsEqual(string left, string right) => string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { } }
}
