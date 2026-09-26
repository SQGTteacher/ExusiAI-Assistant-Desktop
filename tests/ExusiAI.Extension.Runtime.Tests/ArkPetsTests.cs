using System.IO.Compression;
using System.IO;
using System.Text.Json;
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

        var settings = new ArkPetsSettings
        {
            ModelRoot = root.Path,
            FavoriteModelKeys = ["103_angel"],
            CanvasColor = "#00FF00FF",
            InitialPositionX = 0.4,
            InitialPositionY = 0.6,
            LauncherSolidExit = false,
            RenderOutlineEmphasis = 5,
            TransitionDuration = 0.6,
            TransitionType = "LINEAR"
        };
        var path = await ArkPetsConfigWriter.WriteAsync(settings, catalog, model);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();

        Assert.Equal(Path.GetFullPath(asset).Replace('\\', '/'), json["character_asset"]!.GetValue<string>().Replace('\\', '/'));
        Assert.Equal("能天使", json["character_label"]!.GetValue<string>());
        Assert.False(json["enable_telemetry"]!.GetValue<bool>());
        Assert.Equal("angel.skel", json["character_files"]![".skel"]!.GetValue<string>());
        Assert.NotNull(json["character_favorites"]!["103_angel"]);
        Assert.Equal("#00FF00FF", json["canvas_color"]!.GetValue<string>());
        Assert.Equal(0.4, json["initial_position_x"]!.GetValue<double>(), 3);
        Assert.Equal(0.6, json["initial_position_y"]!.GetValue<double>(), 3);
        Assert.True(json["launcher_solid_exit"]!.GetValue<bool>());
        Assert.Equal(5, json["render_outline_emphasis"]!.GetValue<int>());
        Assert.Equal(0.6, json["transition_duration"]!.GetValue<double>(), 3);
        Assert.Equal("LINEAR", json["transition_type"]!.GetValue<string>());
    }

    [Fact]
    public async Task ModelLibraryVerificationAndZipRoundTripWork()
    {
        using var root = new TestDirectory();
        var source = Path.Combine(root.Path, "source");
        var asset = Path.Combine(source, "models", "103_angel");
        Directory.CreateDirectory(asset);
        await File.WriteAllTextAsync(Path.Combine(asset, "angel.atlas"), "");
        await File.WriteAllTextAsync(Path.Combine(asset, "angel.skel"), "");
        await File.WriteAllTextAsync(Path.Combine(asset, "angel.png"), "");
        await File.WriteAllTextAsync(Path.Combine(source, "models_data.json"), """
        {
          "storageDirectory": { "Operator": "models" },
          "sortTags": { "Operator": "干员" },
          "arkPetsCompatibility": [3, 5, 0],
          "data": {
            "103_angel": {
              "type": "Operator",
              "name": "能天使",
              "appellation": "Exusiai",
              "assetList": {
                ".atlas": "angel.atlas",
                ".skel": "angel.skel",
                ".png": ["angel.png"]
              }
            }
          }
        }
        """);

        var catalog = await ArkModelsDataset.LoadAsync(source);
        var verification = ArkModelsLibraryManager.Verify(catalog);
        Assert.True(verification.IsHealthy);
        Assert.Equal(1, verification.AvailableModels);

        var zip = Path.Combine(root.Path, "ArkPetsModels.zip");
        await ArkModelsLibraryManager.ExportAsync(catalog, zip);
        Assert.True(File.Exists(zip));
        using (var exported = ZipFile.OpenRead(zip))
            Assert.Contains(exported.Entries, entry => entry.FullName.Equals("ArkModels/models_data.json", StringComparison.Ordinal));

        var importedRoot = await ArkModelsLibraryManager.ImportAsync(zip, Path.Combine(root.Path, "imports"));
        var imported = await ArkModelsDataset.LoadAsync(importedRoot);
        var importedModel = Assert.Single(imported.Models);
        Assert.Equal("103_angel", importedModel.Key);
        Assert.True(importedModel.IsAvailable);

        File.Delete(Path.Combine(importedModel.AssetDirectory, "angel.png"));
        var missing = ArkModelsLibraryManager.Verify(imported);
        Assert.False(missing.IsHealthy);
        Assert.Equal("103_angel", Assert.Single(missing.MissingModelKeys));
    }

    [Fact]
    public async Task ModelLibraryImportRejectsPathTraversal()
    {
        using var root = new TestDirectory();
        var zip = Path.Combine(root.Path, "malicious.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escape.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("escape");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ArkModelsLibraryManager.ImportAsync(zip, Path.Combine(root.Path, "imports")));
        Assert.False(File.Exists(Path.Combine(root.Path, "escape.txt")));
    }

    [Fact]
    public void UpstreamReleaseParserSelectsPortableZipAndDigest()
    {
        const string json = """
        {
          "tag_name": "v3.13.1",
          "assets": [
            {
              "name": "ArkPets-v3.13.1-Setup.exe",
              "browser_download_url": "https://example.invalid/setup.exe"
            },
            {
              "name": "ArkPets-v3.13.1.zip",
              "browser_download_url": "https://example.invalid/ArkPets-v3.13.1.zip",
              "digest": "sha256:17728d6385309f453d1d36ae1e048324bc030d4d363895f17b165814781b69c9"
            }
          ]
        }
        """;

        var release = ArkPetsUpstreamManager.ParseRuntimeRelease(json);

        Assert.Equal("3.13.1", release.Version);
        Assert.Equal("ArkPets-v3.13.1.zip", release.AssetName);
        Assert.Equal("17728d6385309f453d1d36ae1e048324bc030d4d363895f17b165814781b69c9", release.Sha256);
    }

    [Fact]
    public async Task RuntimeArchiveExtractionRejectsPathTraversal()
    {
        using var root = new TestDirectory();
        var zip = Path.Combine(root.Path, "runtime.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escape.exe");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("escape");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ArkPetsUpstreamManager.ExtractArchiveSafeAsync(zip, Path.Combine(root.Path, "runtime")));
        Assert.False(File.Exists(Path.Combine(root.Path, "escape.exe")));
    }

    [Fact]
    public void IpcCodecParsesArkPetsLoginAndSerializesControl()
    {
        var uuid = Guid.NewGuid();
        var nameBytes = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("能天使"));
        var login = JsonSerializer.Serialize(new
        {
            uuid = uuid.ToString(),
            operation = "LOGIN",
            msg = new { bytes = nameBytes, encoding = "UTF-8" }
        });

        var parsed = ArkPetsIpcMessage.Parse(login);

        Assert.NotNull(parsed);
        Assert.Equal(uuid, parsed!.Uuid);
        Assert.Equal(ArkPetsIpcOperation.Login, parsed.Operation);
        Assert.Equal("能天使", parsed.MessageText);

        var outbound = ArkPetsIpcMessage.Serialize(Guid.NewGuid(), ArkPetsIpcOperation.TransparentMode);
        Assert.Contains("\"operation\":\"TRANSPARENT_MODE\"", outbound);
    }

    [Fact]
    public async Task IpcServerHandlesHandshakeLoginAndHostControl()
    {
        await using var server = new ArkPetsIpcServer();
        await server.StartAsync();

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, server.Port);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };

        var uuid = Guid.NewGuid();
        var nameBytes = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Exusiai"));
        var login = JsonSerializer.Serialize(new
        {
            uuid = uuid.ToString(),
            operation = "LOGIN",
            msg = new { bytes = nameBytes, encoding = "UTF-8" }
        });
        await writer.WriteLineAsync(login);

        for (var index = 0; index < 20 && server.Clients.Count == 0; index++)
            await Task.Delay(25);

        var registered = Assert.Single(server.Clients);
        Assert.Equal("Exusiai", registered.Name);

        Assert.True(await server.SendAsync(uuid, ArkPetsIpcOperation.KeepAction));
        var control = await reader.ReadLineAsync();
        Assert.NotNull(control);
        var controlMessage = ArkPetsIpcMessage.Parse(control!);
        Assert.NotNull(controlMessage);
        Assert.Equal(ArkPetsIpcOperation.KeepAction, controlMessage!.Operation);
        Assert.True(Assert.Single(server.Clients).ManualMode);
    }

    [Fact]
    public async Task IpcServerRejectsAnotherArkPetsHost()
    {
        await using var first = new ArkPetsIpcServer();
        await first.StartAsync();

        await using var second = new ArkPetsIpcServer();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => second.StartAsync());

        Assert.Contains("另一个 ArkPets 控制服务", exception.Message);
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
