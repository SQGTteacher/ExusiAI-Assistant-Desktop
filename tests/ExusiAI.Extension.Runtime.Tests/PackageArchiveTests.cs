using System.IO;
using System.IO.Compression;
using ExusiAI.Extension.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class PackageArchiveTests
{
    [Fact]
    public async Task InstallsAndExportsValidatedPackage()
    {
        using var root = new TestDirectory();
        var zip = Path.Combine(root.Path, "plugin.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Write(archive, "demo/package.json", Manifest);
            Write(archive, "demo/Demo.dll", "binary");
        }
        var service = CreateService();
        var packages = Path.Combine(root.Path, "packages");
        var result = await service.InstallAsync(zip, packages, Path.Combine(root.Path, "temp"));

        Assert.Equal("example.demo", result.Package.Manifest.Id);
        Assert.True(File.Exists(Path.Combine(packages, "example.demo", "package.json")));
        var exported = Path.Combine(root.Path, "exported.zip");
        await service.ExportAsync(result.Package, exported);
        using var exportedArchive = ZipFile.OpenRead(exported);
        Assert.Contains(exportedArchive.Entries, item => item.FullName == "example.demo/package.json");
    }

    [Fact]
    public async Task RejectsArchivePathTraversalWithoutWritingOutsideStaging()
    {
        using var root = new TestDirectory();
        var zip = Path.Combine(root.Path, "unsafe.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) Write(archive, "../outside.txt", "unsafe");
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(zip, Path.Combine(root.Path, "packages"), Path.Combine(root.Path, "temp")));
        Assert.False(File.Exists(Path.Combine(root.Path, "outside.txt")));
    }

    private static PackageArchiveService CreateService()
    {
        var discovery = new PackageDiscoveryService(new ManifestParser(), new ManifestValidator(), NullLogger<PackageDiscoveryService>.Instance);
        return new(discovery);
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    private const string Manifest = """
    {
      "schemaVersion": 1,
      "id": "example.demo",
      "type": "plugin",
      "name": "Demo",
      "version": "1.0.0",
      "apiVersion": "1",
      "publisher": "Tests",
      "minimumHostVersion": "0.2.0-preview.4",
      "entryPoint": { "assembly": "Demo.dll", "type": "Demo.Plugin" },
      "permissions": []
    }
    """;

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ExusiAI.Tests", Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
