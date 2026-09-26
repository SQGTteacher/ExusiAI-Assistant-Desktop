using System.Text.Json.Nodes;
using ExusiAI.Plugin.ArkPets;
using ExusiAI.Plugin.MishaShowcase;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class ArkPetsTests
{
    [Fact]
    public async Task DatasetReadsArkModelsSchema()
    {
        using var root = new TestDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "models", "103_angel"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "models", "103_angel", "angel.atlas"), "");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "models", "103_angel", "angel.skel"), "");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "models", "103_angel", "angel.png"), "");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "models_data.json"), """
        {
          "storageDirectory": { "Operator": "models" },
          "sortTags": { "Operator": "干员" },
          "gameDataVersionDescription": "test",
          "gameDataServerRegion": "zh_CN",
          "arkPetsCompatibility": [2, 2, 0],
          "data": {
            "103_angel": {
              "type": "Operator",
              "style": "BuildingDefault",
              "name": "能天使",
              "appellation": "Exusiai",
              "skinGroupName": "默认服装",
              "sortTags": ["Operator"],
              "assetList": {
                ".atlas": "angel.atlas",
                ".skel": "angel.skel",
                ".png": ["angel.png"]
              }
            }
          }
        }
        """);

        var catalog = await ArkModelsDataset.LoadAsync(root.Path);
        var model = Assert.Single(catalog.Models);

        Assert.Equal("能天使", model.Name);
        Assert.Equal("Exusiai", model.Appellation);
        Assert.True(model.IsAvailable);
        Assert.Equal("2.2.0", catalog.Compatibility);
    }

    [Fact]
    public async Task DatasetRejectsStorageTraversal()
    {
        using var root = new TestDirectory();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "models_data.json"), """
        {
          "storageDirectory": { "Operator": "../outside" },
          "data": {
            "103_angel": {
              "type": "Operator",
              "name": "能天使",
              "assetList": { ".atlas": "a.atlas" }
            }
          }
        }
        """);

        await Assert.ThrowsAsync<InvalidDataException>(() => ArkModelsDataset.LoadAsync(root.Path));
    }

    [Fact]
    public async Task ConfigWriterKeepsArkPetsCharacterFields()
    {
        using var root = new TestDirectory();
        var asset = Path.Combine(root.Path, "models", "103_angel");
        Directory.CreateDirectory(asset);
        var model = new ArkPetModel(
            "103_angel", "Operator", "BuildingDefault", "能天使", "Exusiai", "", "默认服装",
            ["Operator"], asset,
            new Dictionary<string, IReadOnlyList<string>>
            {
                [".atlas"] = ["angel.atlas"],
                [".skel"] = ["angel.skel"],
                [".png"] = ["angel.png"]
            });
        var catalog = new ArkModelsCatalog(root.Path, "test", "zh_CN", "2.2.0", new Dictionary<string, string>(), [model]);

        var path = await ArkPetsConfigWriter.WriteAsync(new ArkPetsSettings { ModelRoot = root.Path }, catalog, model);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();

        Assert.Equal("models/103_angel", json["character_asset"]!.GetValue<string>().Replace('\\', '/'));
        Assert.Equal("能天使", json["character_label"]!.GetValue<string>());
        Assert.False(json["enable_telemetry"]!.GetValue<bool>());
        Assert.Equal("angel.skel", json["character_files"]![".skel"]!.GetValue<string>());
    }

    [Fact]
    public void ClassIslandPublisherAndArkPetsReaderShareStableContract()
    {
        using var root = new TestDirectory();
        var path = Path.Combine(root.Path, "classisland-state.json");
        var lesson = new ClassIslandLessonSnapshot("plan", "高一", 0, "数学", "张老师", TimeSpan.FromHours(8), TimeSpan.FromHours(8.75));
        var state = ClassIslandRuntimeStateResolver.Resolve([lesson], DateTime.Today.AddHours(8.25));

        using var publisher = new ClassIslandIntegrationStatePublisher(path);
        publisher.Publish(state);

        var read = ClassIslandStateFile.Read(path);
        Assert.NotNull(read);
        Assert.Equal("OnClass", read!.Phase);
        Assert.Equal("数学", read.Current!.Subject);
        Assert.Equal("张老师", read.Current.Teacher);
    }

    [Fact]
    public async Task DesktopOrganizerMovesOnlyRecentTeachingFiles()
    {
        using var root = new TestDirectory();
        var observed = DateTime.Today.AddHours(10);
        var recent = Path.Combine(root.Path, "课堂演示.pptx");
        var old = Path.Combine(root.Path, "旧讲义.pdf");
        var program = Path.Combine(root.Path, "tool.exe");
        await File.WriteAllTextAsync(recent, "new");
        await File.WriteAllTextAsync(old, "old");
        await File.WriteAllTextAsync(program, "exe");
        File.SetLastWriteTime(recent, observed.AddMinutes(-10));
        File.SetLastWriteTime(old, observed.AddHours(-4));

        var previous = new ClassIslandPetLesson("p", "plan", 0, "数学", "", TimeSpan.FromHours(9.5), TimeSpan.FromHours(10));
        var state = new ClassIslandPetState(
            DateOnly.FromDateTime(observed),
            "Breaking",
            new DateTimeOffset(observed),
            null,
            previous,
            null);

        var moved = await DesktopLessonOrganizer.OrganizeAsync(root.Path, state);

        Assert.Equal(1, moved);
        Assert.False(File.Exists(recent));
        Assert.True(File.Exists(old));
        Assert.True(File.Exists(program));
        Assert.True(Directory.EnumerateFiles(Path.Combine(root.Path, "ExusiAI 课堂整理"), "课堂演示.pptx", SearchOption.AllDirectories).Any());
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ExusiAI.ArkPets.Tests", Guid.NewGuid().ToString("N"));

        public TestDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
