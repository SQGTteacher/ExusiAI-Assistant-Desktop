using System.IO.Compression;
using ClassIsland.Helpers;

namespace ExusiAI.ClassIsland.Tests;

public sealed class PluginPackageInstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ExusiAI-ClassIsland-" + Guid.NewGuid().ToString("N"));
    private string Plugins => Path.Combine(root, "Plugins");

    [Fact]
    public void InstallsPackageWithNestedEntranceAndAssets()
    {
        var package = CreatePackage(("bin/Probe.dll", typeof(PluginPackageInstallerTests).Assembly.Location),
            ("assets/icon.txt", null));

        PluginPackageInstaller.Install(package, Plugins, "example.plugin", @"bin\Probe.dll");

        Assert.True(File.Exists(Path.Combine(Plugins, "example.plugin", "bin", "Probe.dll")));
        Assert.True(File.Exists(Path.Combine(Plugins, "example.plugin", "assets", "icon.txt")));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UpdatePreservesExistingStateMarkers(bool disabled, bool uninstalling)
    {
        var target = CreateOldPlugin(disabled, uninstalling);
        var package = CreatePackage(("Probe.dll", typeof(PluginPackageInstallerTests).Assembly.Location),
            (".disabled", null), (".uninstall", null));

        PluginPackageInstaller.Install(package, Plugins, "example.plugin", "Probe.dll");

        Assert.Equal(disabled, File.Exists(Path.Combine(target, ".disabled")));
        Assert.Equal(uninstalling, File.Exists(Path.Combine(target, ".uninstall")));
        Assert.False(File.Exists(Path.Combine(target, "old.txt")));
    }

    [Fact]
    public void NewInstallIgnoresPackageStateMarkers()
    {
        var package = CreatePackage(("Probe.dll", typeof(PluginPackageInstallerTests).Assembly.Location),
            (".disabled", null), (".uninstall", null));
        PluginPackageInstaller.Install(package, Plugins, "example.plugin", "Probe.dll");
        Assert.False(File.Exists(Path.Combine(Plugins, "example.plugin", ".disabled")));
        Assert.False(File.Exists(Path.Combine(Plugins, "example.plugin", ".uninstall")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("nested/plugin")]
    [InlineData("CON")]
    [InlineData("trailing.")]
    [InlineData("bad:name")]
    public void RejectsUnsafePluginIds(string pluginId)
    {
        var package = CreatePackage(("Probe.dll", typeof(PluginPackageInstallerTests).Assembly.Location));
        Assert.Throws<InvalidDataException>(() => PluginPackageInstaller.Install(package, Plugins, pluginId, "Probe.dll"));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/rooted.txt")]
    [InlineData("C:/drive.txt")]
    [InlineData("folder/../escape.txt")]
    [InlineData("folder/trailing. /file.txt")]
    public void RejectsUnsafeArchivePaths(string archivePath)
    {
        var package = CreatePackage(("Probe.dll", typeof(PluginPackageInstallerTests).Assembly.Location), (archivePath, null));
        Assert.Throws<InvalidDataException>(() => PluginPackageInstaller.Install(package, Plugins, "example.plugin", "Probe.dll"));
        Assert.False(Directory.Exists(Path.Combine(Plugins, "example.plugin")));
    }

    [Fact]
    public void RejectsCaseInsensitiveDuplicates()
    {
        var package = CreatePackage(("Probe.dll", typeof(PluginPackageInstallerTests).Assembly.Location),
            ("assets/icon.txt", null), ("Assets/Icon.txt", null));
        Assert.Throws<InvalidDataException>(() => PluginPackageInstaller.Install(package, Plugins, "example.plugin", "Probe.dll"));
    }

    [Fact]
    public void RejectsFileDirectoryCollision()
    {
        var package = CreatePackage(("Probe.dll", typeof(PluginPackageInstallerTests).Assembly.Location),
            ("assets", null), ("assets/icon.txt", null));
        Assert.ThrowsAny<IOException>(() => PluginPackageInstaller.Install(package, Plugins, "example.plugin", "Probe.dll"));
    }

    [Fact]
    public void RejectsLinkEntries()
    {
        Directory.CreateDirectory(root);
        var package = Path.Combine(root, Guid.NewGuid() + ".cipx");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            CopyFile(archive.CreateEntry("Probe.dll"), typeof(PluginPackageInstallerTests).Assembly.Location);
            var link = archive.CreateEntry("link");
            link.ExternalAttributes = 0xA000 << 16;
            using var writer = new StreamWriter(link.Open());
            writer.Write("target");
        }
        Assert.Throws<InvalidDataException>(() => PluginPackageInstaller.Install(package, Plugins, "example.plugin", "Probe.dll"));
    }

    [Theory]
    [InlineData("Missing.dll", false)]
    [InlineData("Probe.dll", true)]
    public void InvalidEntranceKeepsPreviousVersion(string entrance, bool includeInvalidAssembly)
    {
        var target = CreateOldPlugin(disabled: true, uninstalling: false);
        var package = includeInvalidAssembly ? CreatePackage(("Probe.dll", null)) : CreatePackage(("asset.txt", null));

        Assert.ThrowsAny<Exception>(() => PluginPackageInstaller.Install(package, Plugins, "example.plugin", entrance));

        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "old.txt")));
        Assert.True(File.Exists(Path.Combine(target, ".disabled")));
    }

    private string CreateOldPlugin(bool disabled, bool uninstalling)
    {
        var target = Path.Combine(Plugins, "example.plugin");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "old.txt"), "old");
        if (disabled) File.WriteAllText(Path.Combine(target, ".disabled"), "");
        if (uninstalling) File.WriteAllText(Path.Combine(target, ".uninstall"), "");
        return target;
    }

    private string CreatePackage(params (string Name, string? Source)[] entries)
    {
        Directory.CreateDirectory(root);
        var package = Path.Combine(root, Guid.NewGuid() + ".cipx");
        using var archive = ZipFile.Open(package, ZipArchiveMode.Create);
        foreach (var (name, source) in entries)
        {
            var entry = archive.CreateEntry(name);
            if (source is not null) CopyFile(entry, source);
            else using (entry.Open()) { }
        }
        return package;
    }

    private static void CopyFile(ZipArchiveEntry entry, string source)
    {
        using var input = File.OpenRead(source);
        using var output = entry.Open();
        input.CopyTo(output);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
