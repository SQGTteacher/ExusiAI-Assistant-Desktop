using System.IO.Compression;
using System.Text;
using ExusiAI.Plugin.ClassIsland;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class ClassIslandRuntimeIntegrationTests
{
    [Fact]
    public void RuntimeDescriptorPinsExactUpstreamBaselines()
    {
        Assert.Equal("ClassIsland/ClassIsland", ClassIslandRuntimeDescriptor.UpstreamRepository);
        Assert.Equal("2.1.0.1", ClassIslandRuntimeDescriptor.RuntimeVersion);
        Assert.Equal("15273f82c9d2d55929df83b5fb806e68ee4547c0", ClassIslandRuntimeDescriptor.RuntimeReleaseCommit);
        Assert.Equal("develop/v2/misha-alpha", ClassIslandRuntimeDescriptor.MishaBranch);
        Assert.Equal("08808615899d1a4abb8e0ef576bf1e247adde10f", ClassIslandRuntimeDescriptor.MishaBaselineCommit);
        Assert.Equal("app-2.1.0.1-0", ClassIslandRuntimeDescriptor.AppFolderName);
    }

    [Fact]
    public void RuntimeLayoutMatchesNativeFolderLauncherContract()
    {
        var root = Path.Combine("root", "Runtime");
        Assert.Equal(Path.Combine(root, "ClassIsland.exe"), ClassIslandRuntimeDescriptor.LauncherPath(root));
        Assert.Equal(Path.Combine(root, "app-2.1.0.1-0", "ClassIsland.Desktop.exe"), ClassIslandRuntimeDescriptor.DesktopPath(root));
        Assert.Equal(Path.Combine(root, "data"), ClassIslandRuntimeDescriptor.DataDirectory(root));
    }

    [Fact]
    public async Task SyncReplacesOnlyManagedNativeEntries()
    {
        using var root = new TemporaryDirectory();
        var data = Path.Combine(root.Path, "data");
        Directory.CreateDirectory(Path.Combine(data, "Profiles"));
        Directory.CreateDirectory(Path.Combine(data, "Config"));
        Directory.CreateDirectory(Path.Combine(data, "Logs"));
        await File.WriteAllTextAsync(Path.Combine(data, "Settings.json"), "{\"old\":true}");
        await File.WriteAllTextAsync(Path.Combine(data, "Profiles", "old.json"), "old");
        await File.WriteAllTextAsync(Path.Combine(data, "Config", "old.json"), "old");
        await File.WriteAllTextAsync(Path.Combine(data, "Logs", "keep.log"), "keep");

        var archivePath = Path.Combine(root.Path, "Auto_Backup.zip");
        using (var file = File.Create(archivePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Write(archive, "backup/Settings.json", "{\"Theme\":2,\"FutureField\":true}");
            Write(archive, "backup/Profiles/profile.json", "{\"Name\":\"Class A\"}");
            Write(archive, "backup/Config/Components/default.json", "{\"Components\":[]}");
            Write(archive, "backup/Backups/ignored.zip", "ignored");
        }

        var summary = await ClassIslandBackupImporter.ImportIntoDataDirectoryAsync(archivePath, data);
        Assert.Equal(3, summary.TotalFileCount);
        Assert.Contains("FutureField", await File.ReadAllTextAsync(Path.Combine(data, "Settings.json")));
        Assert.True(File.Exists(Path.Combine(data, "Profiles", "profile.json")));
        Assert.False(File.Exists(Path.Combine(data, "Profiles", "old.json")));
        Assert.True(File.Exists(Path.Combine(data, "Config", "Components", "default.json")));
        Assert.False(File.Exists(Path.Combine(data, "Config", "old.json")));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(data, "Logs", "keep.log")));
    }

    [Fact]
    public void SyncRejectsTraversal()
    {
        using var root = new TemporaryDirectory();
        var archivePath = Path.Combine(root.Path, "bad.zip");
        using (var file = File.Create(archivePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Write(archive, "Settings.json", "{}");
            Write(archive, "../outside.txt", "bad");
        }
        Assert.Throws<InvalidDataException>(() => ClassIslandBackupImporter.Inspect(archivePath));
        Assert.False(File.Exists(Path.Combine(root.Path, "outside.txt")));
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "exusiai-classisland-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
