using System.Text.Json.Nodes;
using ExusiAI.Plugin.MishaShowcase;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class MishaSettingsParityTests
{
    [Fact]
    public async Task ImportedWorkspaceBindingSurvivesRestartAndUsesOnlyExusiAICopy()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "classisland-source");
        Directory.CreateDirectory(source);
        var sourceSettings = Path.Combine(source, "Settings.json");
        await File.WriteAllTextAsync(sourceSettings, """
        {
          "Theme":2,
          "SelectedProfile":"",
          "FutureMishaField":{"keep":true}
        }
        """);

        var storage = Path.Combine(root.Path, "exusiai-store");
        var first = new MishaPlatformStore(storage);
        await first.AttachWorkspaceAsync(sourceSettings);
        Assert.NotNull(first.Workspace);
        Assert.NotEqual(Path.GetFullPath(sourceSettings), first.Workspace!.SettingsPath);

        first.Workspace.Set("Theme", 1);
        await first.SaveWorkspaceSettingsAsync();

        var restored = new MishaPlatformStore(storage);
        Assert.True(await restored.RestoreLastWorkspaceAsync());
        Assert.Equal(1, restored.Workspace!.GetInt("Theme"));
        Assert.Equal(Path.GetFullPath(source), restored.SourceRootDirectory);

        var original = JsonNode.Parse(await File.ReadAllTextAsync(sourceSettings))!.AsObject();
        Assert.Equal(2, original["Theme"]!.GetValue<int>());
        Assert.True(original["FutureMishaField"]!["keep"]!.GetValue<bool>());
    }

    [Fact]
    public async Task CorruptWorkspaceBindingDoesNotBreakPluginStartup()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(root.Path);
        await File.WriteAllTextAsync(Path.Combine(root.Path, "workspace-state.json"), "not-json");

        var store = new MishaPlatformStore(root.Path);
        Assert.False(await store.RestoreLastWorkspaceAsync());
        Assert.Null(store.Workspace);
        Assert.Null(store.Profile);
    }

    [Fact]
    public void SettingsCatalogCoversMishaSettingsNavigationSurface()
    {
        var ids = MishaSettingsCatalog.Categories.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in new[]
        {
            "general", "clock", "storage", "privacy", "refreshing", "advanced", "components-settings",
            "appearance", "notification", "window", "weather", "automation-settings", "update", "plugins",
            "themes", "management", "compat"
        })
            Assert.Contains(id, ids);
    }

    [Fact]
    public async Task SettingsWorkspaceRoundTripsKnownAndUnknownFieldsWithBackup()
    {
        using var root = new TemporaryDirectory();
        var settingsPath = Path.Combine(root.Path, "Settings.json");
        await File.WriteAllTextAsync(settingsPath, """
        {
          "Theme":2,
          "IsNotificationEnabled":true,
          "CurrentComponentConfig":"Default",
          "FutureMishaField":{"keep":true},
          "FutureNullField":null
        }
        """);

        var workspace = await ClassIslandWorkspace.LoadAsync(settingsPath);
        workspace.Set("Theme", 1);
        workspace.Set("IsNotificationEnabled", false);
        await workspace.SaveSettingsAsync();

        var saved = JsonNode.Parse(await File.ReadAllTextAsync(settingsPath))!.AsObject();
        Assert.Equal(1, saved["Theme"]!.GetValue<int>());
        Assert.False(saved["IsNotificationEnabled"]!.GetValue<bool>());
        Assert.True(saved["FutureMishaField"]!["keep"]!.GetValue<bool>());
        Assert.True(saved.ContainsKey("FutureNullField"));
        Assert.Null(saved["FutureNullField"]);
        Assert.True(File.Exists(settingsPath + ".bak"));

        var compat = MishaSettingsCatalog.Categories.Single(x => x.Id == "compat");
        Assert.Contains("FutureMishaField", MishaSettingsCatalog.ResolveKeys(compat, saved));
        Assert.Contains("FutureNullField", MishaSettingsCatalog.ResolveKeys(compat, saved));
        Assert.DoesNotContain("Theme", MishaSettingsCatalog.ResolveKeys(compat, saved));
    }

    [Fact]
    public void AllMishaSettingsPagesConstructAndLayoutAgainstRealSettingsJson()
    {
        using var root = new TemporaryDirectory();
        var settingsPath = Path.Combine(root.Path, "Settings.json");
        File.WriteAllText(settingsPath, """
        {
          "Theme":2,
          "ExactTimeServer":"ntp.aliyun.com",
          "IsExactTimeEnabled":true,
          "IsAutoBackupEnabled":true,
          "CurrentComponentConfig":"Default",
          "CurrentAutomationConfig":"Default",
          "IsAutomationEnabled":false,
          "IsNotificationEnabled":true,
          "CityName":"北京 (北京, 中国)",
          "FutureMishaField":{"keep":true}
        }
        """);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var store = new MishaPlatformStore(Path.Combine(root.Path, "misha-store"));
                store.AttachWorkspaceAsync(settingsPath).GetAwaiter().GetResult();
                foreach (var category in MishaSettingsCatalog.Categories)
                {
                    var page = new MishaSettingsCategoryPage(store, category);
                    page.Measure(new System.Windows.Size(1280, 800));
                    page.Arrange(new System.Windows.Rect(0, 0, 1280, 800));
                    page.UpdateLayout();
                }

                var host = new MishaSettingsHostPage(store);
                host.Measure(new System.Windows.Size(1280, 800));
                host.Arrange(new System.Windows.Rect(0, 0, 1280, 800));
                host.UpdateLayout();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Misha settings page smoke test timed out.");
        Assert.Null(failure);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "exusiai-misha-settings-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (!Directory.Exists(Path)) return;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(Path, true);
                    return;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    if (attempt == 4) throw;
                    Thread.Sleep(25);
                }
            }
        }
    }
}
