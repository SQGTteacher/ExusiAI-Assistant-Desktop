using ExusiAI.Plugin.ClassIsland;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class ClassIslandImportTests
{
    [Fact]
    public async Task ImportCopiesOriginalDataAndPreservesPreviousFilesInBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), "classisland-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "original");
        var destination = Path.Combine(root, "embedded");
        try
        {
            Directory.CreateDirectory(Path.Combine(source, "Profiles"));
            Directory.CreateDirectory(Path.Combine(source, "Config", "Themes", "custom"));
            File.WriteAllText(Path.Combine(source, "Settings.json"), "{\"SelectedProfile\":\"new\"}");
            File.WriteAllText(Path.Combine(source, "Profiles", "new.json"), "{}");
            File.WriteAllText(Path.Combine(source, "Config", "Themes", "custom", "theme.json"), "{}");
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "Settings.json"), "{\"SelectedProfile\":\"old\"}");
            var stopped = false;

            var backup = await ClassIslandDataImporter.ImportAsync(source, destination,
                () => stopped = true, () => stopped);

            Assert.True(stopped);
            Assert.Contains("new", File.ReadAllText(Path.Combine(destination, "Settings.json")));
            Assert.True(File.Exists(Path.Combine(destination, "Profiles", "new.json")));
            Assert.True(File.Exists(Path.Combine(destination, "Config", "Themes", "custom", "theme.json")));
            Assert.Contains("old", File.ReadAllText(Path.Combine(backup, "Settings.json")));
            Assert.Contains("new", File.ReadAllText(Path.Combine(source, "Settings.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InvalidSourceDoesNotStopHostOrReplaceData()
    {
        var root = Path.Combine(Path.GetTempPath(), "classisland-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "original");
        var destination = Path.Combine(root, "embedded");
        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(source, "Settings.json"), "[]");
            File.WriteAllText(Path.Combine(destination, "Settings.json"), "{}");
            var stopped = false;

            await Assert.ThrowsAsync<InvalidDataException>(() => ClassIslandDataImporter.ImportAsync(
                source, destination, () => stopped = true, () => stopped));

            Assert.False(stopped);
            Assert.Equal("{}", File.ReadAllText(Path.Combine(destination, "Settings.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HostThatFailsToStopLeavesExistingDataUntouched()
    {
        var root = Path.Combine(Path.GetTempPath(), "classisland-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "original");
        var destination = Path.Combine(root, "embedded");
        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(source, "Settings.json"), "{\"Value\":2}");
            File.WriteAllText(Path.Combine(destination, "Settings.json"), "{\"Value\":1}");
            var stopRequested = false;

            await Assert.ThrowsAsync<IOException>(() => ClassIslandDataImporter.ImportAsync(source,
                destination, () => stopRequested = true, () => false));

            Assert.True(stopRequested);
            Assert.Contains("1", File.ReadAllText(Path.Combine(destination, "Settings.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
