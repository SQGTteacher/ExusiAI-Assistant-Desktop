using System.IO.Compression;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.Runtime;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;
using ExusiAI.Plugin.Sample;
using ExusiAI.Plugin.MishaShowcase;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class RuntimeTests
{
    [Fact]
    public void MishaRefreshQueueCoalescesChangesWithoutDroppingLatestRequest()
    {
        var queue = new CoalescingRefreshQueue();

        Assert.True(queue.Request());
        Assert.True(queue.TakeNext());

        Assert.False(queue.Request());
        Assert.False(queue.Request());
        Assert.True(queue.TakeNext());
        Assert.False(queue.TakeNext());

        Assert.True(queue.Request());
    }

    [Fact]
    public void ClassIslandColorCodecUsesAvaloniaRgbaOrdering()
    {
        Assert.True(ClassIslandColorCodec.TryParse("#000000FF", out var black));
        Assert.Equal(System.Windows.Media.Color.FromArgb(255, 0, 0, 0), black);

        Assert.True(ClassIslandColorCodec.TryParse("#1E90FFFF", out var dodgerBlue));
        Assert.Equal(System.Windows.Media.Color.FromArgb(255, 30, 144, 255), dodgerBlue);
        Assert.Equal("#1E90FFFF", ClassIslandColorCodec.Format(dodgerBlue));

        Assert.True(ClassIslandColorCodec.TryParse("#FF000080", out var halfRed));
        Assert.Equal(System.Windows.Media.Color.FromArgb(128, 255, 0, 0), halfRed);
    }

    [Fact]
    public async Task ClassIslandAutoBackupWithBackslashEntriesSynchronizesNativeWorkspace()
    {
        using var root = new TemporaryDirectory();
        var archivePath = Path.Combine(root.Path, "Auto_Backup_26-9月-24_22-24-22.zip");

        using (var stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Write("Settings.json", """
            {
              "SelectedProfile":"Default.json",
              "CurrentComponentConfig":"Default",
              "Theme":0,
              "BackgroundColor":"#000000FF",
              "FutureField":{"keep":true}
            }
            """);
            Write(@"Profiles\Default.json", """
            {
              "Name":"同步档案",
              "Subjects":{},
              "TimeLayouts":{},
              "ClassPlans":{}
            }
            """);
            Write(@"Config\ComponentLayouts\Default.json", """{"Lines":[]}""");
            Write(@"Config\Automations\Default.json", "[]");
            Write(@"Config\Themes\Example\Styles.axaml", "<Styles />");
            Write(@"Config\Themes\Example\tools\validate.py", "raise RuntimeError('must never execute during sync')");

            void Write(string name, string value)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(value);
            }
        }

        var summary = ClassIslandBackupImporter.Inspect(archivePath);
        Assert.Equal(1, summary.ProfileFileCount);
        Assert.Equal(4, summary.ConfigFileCount);
        Assert.Equal(6, summary.TotalFileCount);

        var store = new MishaPlatformStore(Path.Combine(root.Path, "ExusiAIStore"));
        var synchronized = await store.SyncBackupAsync(archivePath);

        Assert.Equal(summary.TotalFileCount, synchronized.TotalFileCount);
        Assert.NotNull(store.Workspace);
        Assert.NotNull(store.Profile);
        Assert.Equal("同步档案", store.Profile!.Name);
        Assert.Equal(archivePath, store.SourceRootDirectory);
        Assert.True(store.Workspace!.Settings["FutureField"]?["keep"]?.GetValue<bool>());
        Assert.True(File.Exists(Path.Combine(
            store.Workspace.RootDirectory,
            "Config",
            "Themes",
            "Example",
            "Styles.axaml")));
        Assert.True(File.Exists(Path.Combine(
            store.Workspace.RootDirectory,
            "Config",
            "Themes",
            "Example",
            "tools",
            "validate.py")));
    }

    [Fact]
    public async Task ClassIslandThemeCompatibilityParsesAxamlWithoutExecutingThemeContent()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "ClassIslandSource");
        Directory.CreateDirectory(Path.Combine(source, "Profiles"));
        Directory.CreateDirectory(Path.Combine(source, "Config", "Themes", "Glass", "tools"));

        await File.WriteAllTextAsync(Path.Combine(source, "Settings.json"), """
        {
          "SelectedProfile":"Default.json",
          "CurrentComponentConfig":"Default",
          "Theme":2,
          "Opacity":0.75,
          "IsCustomBackgroundColorEnabled":false
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(source, "Profiles", "Default.json"), """
        {
          "Name":"Theme test",
          "Subjects":{},
          "TimeLayouts":{},
          "ClassPlans":{}
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(source, "Config", "EnabledThemes.json"), """
        ["classisland.fluent","dev.test.glass"]
        """);
        await File.WriteAllTextAsync(Path.Combine(source, "Config", "Themes", "Glass", "manifest.yml"), """
        id: dev.test.glass
        name: Glass Test
        author: ExusiAI Tests
        version: '1.0.0.0'
        verticalSafeAreaPx: 24
        """);
        await File.WriteAllTextAsync(Path.Combine(source, "Config", "Themes", "Glass", "Styles.axaml"), """
        <Styles xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:ci="http://classisland.tech/schemas/xaml/core">
          <Styles.Resources>
            <ResourceDictionary>
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Dark">
                  <DrawingBrush x:Key="Test.Surface" Stretch="Fill" TileMode="None">
                    <DrawingBrush.Drawing>
                      <DrawingGroup>
                        <DrawingGroup.Children>
                          <GeometryDrawing>
                            <GeometryDrawing.Geometry>
                              <RectangleGeometry Rect="0,0,100,100" />
                            </GeometryDrawing.Geometry>
                            <GeometryDrawing.Brush>
                              <LinearGradientBrush StartPoint="0%,0%" EndPoint="0%,100%">
                                <GradientStop Offset="0" Color="#40112233" />
                                <GradientStop Offset="1" Color="#C0445566" />
                              </LinearGradientBrush>
                            </GeometryDrawing.Brush>
                          </GeometryDrawing>
                        </DrawingGroup.Children>
                      </DrawingGroup>
                    </DrawingBrush.Drawing>
                  </DrawingBrush>
                  <ConicGradientBrush x:Key="Test.Edge" Center="50%,50%" Angle="0">
                    <GradientStop Offset="0" Color="#F5FFFFFF" />
                    <GradientStop Offset="1" Color="#80445566" />
                  </ConicGradientBrush>
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
          </Styles.Resources>
          <StyleInclude Source="https://example.invalid/never-load.axaml" />
          <Style Selector="Border.line-background">
            <Setter Property="BorderBrush" Value="{DynamicResource Test.Edge}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="BoxShadow" Value="0 5 12 0 #22020B19, inset 0 1 0 0 #B8FFFFFF" />
          </Style>
          <Style Selector="Border[(ci|MainWindowStylesAssist.IsCustomBackgroundColorEnabled)=False].line-background">
            <Setter Property="Background" Value="{DynamicResource Test.Surface}" />
          </Style>
        </Styles>
        """);
        await File.WriteAllTextAsync(
            Path.Combine(source, "Config", "Themes", "Glass", "tools", "validate.py"),
            "raise RuntimeError('must never execute')");

        var store = new MishaPlatformStore(Path.Combine(root.Path, "ExusiAIStore"));
        await store.AttachWorkspaceAsync(Path.Combine(source, "Settings.json"));

        Assert.Equal(new[] { "classisland.fluent", "dev.test.glass" }, store.ThemeSnapshot.EnabledThemeIds);
        Assert.Equal(24, store.ThemeSnapshot.ActualVerticalSafeAreaPx);
        var package = Assert.Single(store.ThemeSnapshot.Packages, x => x.Manifest.Id == "dev.test.glass");
        Assert.NotNull(package.Document);
        Assert.Contains(package.Diagnostics, x => x.Contains("忽略外部 StyleInclude", StringComparison.Ordinal));

        var darkSurface = Assert.IsType<ClassIslandThemeDrawingBrushResource>(
            store.ThemeSnapshot.ResolveResource("Test.Surface", ClassIslandThemeVariant.Dark));
        var firstLayer = Assert.Single(darkSurface.Layers);
        var firstGradient = Assert.IsType<ClassIslandThemeLinearGradientResource>(firstLayer.Brush);
        Assert.Equal((byte)0x40, firstGradient.Stops[0].Color.A);
        Assert.Equal((byte)0x11, firstGradient.Stops[0].Color.R);
        Assert.Equal((byte)0x22, firstGradient.Stops[0].Color.G);
        Assert.Equal((byte)0x33, firstGradient.Stops[0].Color.B);
        Assert.IsType<ClassIslandThemeConicGradientResource>(
            store.ThemeSnapshot.ResolveResource("Test.Edge", ClassIslandThemeVariant.Dark));

        Assert.True(File.Exists(Path.Combine(
            store.Workspace!.RootDirectory,
            "Config",
            "Themes",
            "Glass",
            "tools",
            "validate.py")));

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var border = new System.Windows.Controls.Border
                {
                    Background = System.Windows.Media.Brushes.Black
                };
                ClassIslandThemeWpfAdapter.ApplyIslandStyle(
                    border,
                    store.ThemeSnapshot,
                    ClassIslandThemeVariant.Dark,
                    customBackgroundEnabled: false);

                Assert.IsType<System.Windows.Media.DrawingBrush>(border.Background);
                Assert.Equal(new System.Windows.Thickness(1), border.BorderThickness);
                Assert.IsType<System.Windows.Media.LinearGradientBrush>(border.BorderBrush);
                Assert.IsType<System.Windows.Media.Effects.DropShadowEffect>(border.Effect);

                var custom = new System.Windows.Controls.Border
                {
                    Background = System.Windows.Media.Brushes.Red
                };
                ClassIslandThemeWpfAdapter.ApplyIslandStyle(
                    custom,
                    store.ThemeSnapshot,
                    ClassIslandThemeVariant.Dark,
                    customBackgroundEnabled: true);
                Assert.Same(System.Windows.Media.Brushes.Red, custom.Background);
                Assert.IsType<System.Windows.Media.SolidColorBrush>(custom.BorderBrush);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "ClassIsland theme adapter test timed out.");
        Assert.Null(failure);

        await store.UpdateEnabledThemesAsync(new[] { "dev.test.glass", "classisland.fluent" });
        Assert.Equal(new[] { "dev.test.glass", "classisland.fluent" }, store.ThemeSnapshot.EnabledThemeIds);
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(store.Workspace.EnabledThemesPath))!.AsArray();
        Assert.Equal("dev.test.glass", persisted[0]!.GetValue<string>());
        Assert.Equal("classisland.fluent", persisted[1]!.GetValue<string>());
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("..\\escape.dll")]
    [InlineData("C:\\escape.dll")]
    [InlineData("\\\\server\\share\\plugin.dll")]
    public void ValidatorRejectsUnsafeEntryPointPaths(string assemblyPath)
    {
        var manifest = ValidManifest with { EntryPoint = new() { Assembly = assemblyPath, Type = "Plugin" } };
        var result = new ManifestValidator().Validate(manifest, Path.GetTempPath());
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Code == "entry-assembly");
    }

    [Theory]
    [InlineData("Bad Id", "1.0.0", 1, "1")]
    [InlineData("valid.package", "not-version", 1, "1")]
    [InlineData("valid.package", "1.0.0", 2, "1")]
    [InlineData("valid.package", "1.0.0", 1, "99")]
    public void ValidatorRejectsInvalidIdentityOrCompatibility(string id, string version, int schema, string api)
    {
        var manifest = ValidManifest with { Id = id, Version = version, SchemaVersion = schema, ApiVersion = api };
        Assert.False(new ManifestValidator().Validate(manifest, Path.GetTempPath()).IsValid);
    }

    [Fact]
    public async Task DiscoveryReportsMissingFieldsAndDuplicateIds()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "missing"));
        await WriteManifestAsync(Path.Combine(root.Path, "one"), ValidManifest);
        await WriteManifestAsync(Path.Combine(root.Path, "two"), ValidManifest);
        await File.WriteAllTextAsync(Path.Combine(root.Path, "missing", "package.json"), "{\"schemaVersion\":1}");
        var result = await CreateDiscovery().DiscoverAsync(root.Path);
        Assert.Single(result.Packages);
        Assert.Contains(result.Failures, x => x.Code == "duplicate-id");
        Assert.Contains(result.Failures, x => x.Code == "manifest-read-failed");
    }

    [Fact]
    public async Task DiscoveryCombinesBundledAndUserPackageRoots()
    {
        using var bundled = new TemporaryDirectory();
        using var user = new TemporaryDirectory();
        await WriteManifestAsync(Path.Combine(bundled.Path, "one"), ValidManifest);
        await WriteManifestAsync(Path.Combine(user.Path, "two"), ValidManifest with { Id = "exusiai.user" });
        await using var runtime = new ExtensionRuntime(CreateDiscovery(), NullLogger<ExtensionRuntime>.Instance);
        await runtime.DiscoverAsync([bundled.Path, user.Path]);
        Assert.Equal(2, runtime.Entries.Count);
        Assert.Empty(runtime.DiscoveryFailures);
    }

    [Fact]
    public async Task BadAssemblyFailsWithoutCrashingRuntime()
    {
        using var root = new TemporaryDirectory();
        await WriteManifestAsync(Path.Combine(root.Path, "bad"), ValidManifest);
        await using var runtime = new ExtensionRuntime(CreateDiscovery(), NullLogger<ExtensionRuntime>.Instance);
        await runtime.DiscoverAsync(root.Path);
        await runtime.StartAsync();
        Assert.Equal(PackageState.Failed, Assert.Single(runtime.Entries).State);
    }

    [Fact]
    public async Task BadEntryTypeFailsWithoutCrashingRuntime()
    {
        using var root = new TemporaryDirectory();
        var package = Path.Combine(root.Path, "bad-type");
        await WriteManifestAsync(package, ValidManifest with { EntryPoint = new() { Assembly = "ExusiAI.Plugin.Sample.dll", Type = "Missing.Plugin" } });
        File.Copy(typeof(SamplePlugin).Assembly.Location, Path.Combine(package, "ExusiAI.Plugin.Sample.dll"));
        await using var runtime = new ExtensionRuntime(CreateDiscovery(), NullLogger<ExtensionRuntime>.Instance);
        await runtime.DiscoverAsync(root.Path);
        await runtime.StartAsync();
        Assert.Equal(PackageState.Failed, Assert.Single(runtime.Entries).State);
    }

    [Fact]
    public async Task PluginLifecycleExceptionIsIsolated()
    {
        using var root = new TemporaryDirectory();
        var package = Path.Combine(root.Path, "throwing");
        var manifest = ValidManifest with
        {
            Id = "exusiai.throwing",
            EntryPoint = new() { Assembly = "ExusiAI.Extension.Runtime.Tests.dll", Type = "ExusiAI.Extension.Runtime.Tests.ThrowingPlugin" }
        };
        await WriteManifestAsync(package, manifest);
        File.Copy(typeof(RuntimeTests).Assembly.Location, Path.Combine(package, "ExusiAI.Extension.Runtime.Tests.dll"));
        await using var runtime = new ExtensionRuntime(CreateDiscovery(), NullLogger<ExtensionRuntime>.Instance);
        await runtime.DiscoverAsync(root.Path);
        await runtime.StartAsync();
        var entry = Assert.Single(runtime.Entries);
        Assert.Equal(PackageState.Failed, entry.State);
        Assert.Equal("plugin-start-failed", entry.FailureCode);
    }

    [Fact]
    public async Task SamplePluginLoadsInCollectibleContextAndStarts()
    {
        using var root = new TemporaryDirectory();
        var loadContext = await LoadAndStopSamplePluginAsync(root.Path);
        Assert.True(
            WaitForCollection(loadContext, TimeSpan.FromSeconds(2)),
            "Collectible plugin AssemblyLoadContext did not unload within the bounded GC wait.");
    }

    [Fact]
    public async Task DisabledPluginCanBeEnabledAndDisabledWithoutRestartingHost()
    {
        using var root = new TemporaryDirectory();
        var package = Path.Combine(root.Path, "sample");
        await WriteManifestAsync(package, ValidManifest);
        File.Copy(typeof(SamplePlugin).Assembly.Location, Path.Combine(package, "ExusiAI.Plugin.Sample.dll"));
        File.Copy(typeof(ExtensionPluginBase).Assembly.Location, Path.Combine(package, "ExusiAI.Extension.SDK.dll"));
        await using var runtime = new ExtensionRuntime(CreateDiscovery(), NullLogger<ExtensionRuntime>.Instance);
        await runtime.DiscoverAsync(root.Path);

        await runtime.StartAsync(["exusiai.sample"]);
        Assert.Equal(PackageState.Disabled, Assert.Single(runtime.Entries).State);

        await runtime.SetEnabledAsync("exusiai.sample", true);
        Assert.Equal(PackageState.Running, Assert.Single(runtime.Entries).State);

        await runtime.SetEnabledAsync("exusiai.sample", false);
        var disabled = Assert.Single(runtime.Entries);
        Assert.Equal(PackageState.Disabled, disabled.State);
        Assert.Null(disabled.Instance);
    }

    [Fact]
    public async Task MishaFeaturePortLoadsAndRegistersAllNavigationAreas()
    {
        using var root = new TemporaryDirectory();
        var package = Path.Combine(root.Path, "misha-showcase");
        var manifest = ValidManifest with
        {
            Id = "exusiai.misha-showcase",
            DisplayName = "米沙平台兼容范本",
            EntryPoint = new()
            {
                Assembly = "ExusiAI.Plugin.MishaShowcase.dll",
                Type = "ExusiAI.Plugin.MishaShowcase.MishaShowcasePlugin"
            }
        };
        await WriteManifestAsync(package, manifest);
        File.Copy(typeof(MishaShowcasePlugin).Assembly.Location, Path.Combine(package, "ExusiAI.Plugin.MishaShowcase.dll"));
        File.Copy(typeof(ExtensionPluginBase).Assembly.Location, Path.Combine(package, "ExusiAI.Extension.SDK.dll"));
        await using var runtime = new ExtensionRuntime(CreateDiscovery(), NullLogger<ExtensionRuntime>.Instance);

        await runtime.DiscoverAsync(root.Path);
        await runtime.StartAsync();

        var entry = Assert.Single(runtime.Entries);
        Assert.Equal(PackageState.Running, entry.State);
        var navigation = Assert.IsAssignableFrom<IWpfNavigationExtension>(entry.Instance);
        var pages = navigation.GetNavigationPages().ToArray();
        var page = Assert.Single(pages);
        Assert.Equal("misha.settings", page.Route);
        Assert.Equal("ClassIsland 2.2 Misha", page.Title);

        Exception? pageFailure = null;
        var pageThread = new Thread(() =>
        {
            try
            {
                var view = page.CreateView();
                Assert.NotNull(view);
                view.Measure(new System.Windows.Size(1280, 800));
                view.Arrange(new System.Windows.Rect(0, 0, 1280, 800));
                view.UpdateLayout();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.DataBind,
                    new Action(() => { }));
            }
            catch (Exception exception) { pageFailure = exception; }
        });
        pageThread.SetApartmentState(ApartmentState.STA);
        pageThread.Start();
        Assert.True(pageThread.Join(TimeSpan.FromSeconds(15)), "WPF page smoke test timed out.");
        Assert.Null(pageFailure);
    }

    [Fact]
    public async Task ClassIslandProfileDocumentRoundTripsNativeNodesWithoutInventingExampleData()
    {
        using var root = new TemporaryDirectory();
        var profilePath = Path.Combine(root.Path, "Profile.json");
        const string subjectId = "11111111-1111-4111-8111-111111111111";
        const string layoutId = "22222222-2222-4222-8222-222222222222";
        const string planId = "33333333-3333-4333-8333-333333333333";
        var json = """
        {
          "Name":"兼容性测试",
          "FutureField":{"keep":true},
          "ScheduleType":0,
          "Subjects":{"SUBJECT_ID":{"Name":"数学","Initial":"数","TeacherName":"周老师","IsOutDoor":false,"FutureSubjectField":7}},
          "TimeLayouts":{"LAYOUT_ID":{"Name":"标准时间表","IsOverlay":false,"Layouts":[{"StartTime":"08:00:00","EndTime":"08:40:00","TimeType":0,"IsHideDefault":false,"DefaultClassId":"00000000-0000-0000-0000-000000000000","BreakName":"","FuturePointField":"keep"}]}},
          "ClassPlans":{"PLAN_ID":{"Name":"周一","TimeLayoutId":"LAYOUT_ID","IsEnabled":true,"AssociatedGroup":"ACAF4EF0-E261-4262-B941-34EA93CB4369","TimeRule":{"Type":0,"WeekDay":1,"WeekCountDiv":1,"WeekCountDivTotal":2,"FutureRuleField":9},"Classes":[{"SubjectId":"SUBJECT_ID","IsEnabled":true,"FutureClassField":true}]}},
          "ClassPlanGroups":{},
          "OrderedSchedules":{},
          "ScheduleItems":{}
        }
        """
        .Replace("SUBJECT_ID", subjectId, StringComparison.Ordinal)
        .Replace("LAYOUT_ID", layoutId, StringComparison.Ordinal)
        .Replace("PLAN_ID", planId, StringComparison.Ordinal);
        await File.WriteAllTextAsync(profilePath, json);

        var document = await ClassIslandProfileDocument.LoadAsync(profilePath);
        Assert.Equal("兼容性测试", document.Name);
        Assert.Equal(subjectId, Assert.Single(document.Subjects).Id);
        Assert.Equal(layoutId, Assert.Single(document.TimeLayouts).Id);
        Assert.Equal(planId, Assert.Single(document.ClassPlans).Id);

        var subject = Assert.Single(document.Subjects);
        subject.Name = "高等数学";
        subject.TeacherName = "王老师";

        var lesson = Assert.Single(Assert.Single(document.ClassPlans).Lessons);
        Assert.Equal("高等数学", lesson.SubjectName);
        Assert.Equal("08:00:00", lesson.StartTime);

        await document.SaveAsync();

        var saved = JsonNode.Parse(await File.ReadAllTextAsync(profilePath))!.AsObject();
        Assert.True(saved["FutureField"]?["keep"]?.GetValue<bool>());
        Assert.Equal(7, saved["Subjects"]?[subjectId]?["FutureSubjectField"]?.GetValue<int>());
        Assert.Equal("keep", saved["TimeLayouts"]?[layoutId]?["Layouts"]?[0]?["FuturePointField"]?.GetValue<string>());
        Assert.True(saved["ClassPlans"]?[planId]?["Classes"]?[0]?["FutureClassField"]?.GetValue<bool>());
        Assert.Equal(9, saved["ClassPlans"]?[planId]?["TimeRule"]?["FutureRuleField"]?.GetValue<int>());
        Assert.Equal("高等数学", saved["Subjects"]?[subjectId]?["Name"]?.GetValue<string>());
        Assert.Equal("王老师", saved["Subjects"]?[subjectId]?["TeacherName"]?.GetValue<string>());
        Assert.True(File.Exists(profilePath + ".bak"));
    }

    [Fact]
    public void MishaStoreStartsDetachedAndDoesNotGenerateSampleConfiguration()
    {
        var store = new MishaPlatformStore();
        Assert.Null(store.Workspace);
        Assert.Null(store.Profile);
    }


    [Fact]
    public async Task MishaWorkspaceImportPreservesNativeJsonAndNeverWritesSource()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "ClassIslandSource");
        Directory.CreateDirectory(Path.Combine(source, "Profiles"));
        Directory.CreateDirectory(Path.Combine(source, "Config", "ComponentLayouts"));
        Directory.CreateDirectory(Path.Combine(source, "Config", "Automations"));

        var settingsPath = Path.Combine(source, "Settings.json");
        var originalSettings = "{\n  \"SelectedProfile\":\"Default.json\",\n  \"CurrentComponentConfig\":\"Default\",\n  \"CurrentAutomationConfig\":\"Default\",\n  \"FutureField\":{\"keep\":true}\n}";
        await File.WriteAllTextAsync(settingsPath, originalSettings);
        await File.WriteAllTextAsync(Path.Combine(source, "Profiles", "Default.json"), "{\"Name\":\"原生档案\",\"Subjects\":{},\"TimeLayouts\":{},\"ClassPlans\":{}}");
        await File.WriteAllTextAsync(Path.Combine(source, "Config", "ComponentLayouts", "Default.json"), "{\"Lines\":[],\"FutureLayoutField\":7}");
        await File.WriteAllTextAsync(Path.Combine(source, "Config", "Automations", "Default.json"), "[]");

        var store = new MishaPlatformStore(Path.Combine(root.Path, "ExusiAIStore"));
        await store.AttachWorkspaceAsync(settingsPath);

        Assert.NotNull(store.Workspace);
        Assert.NotEqual(Path.GetFullPath(source), store.Workspace!.RootDirectory);
        Assert.Equal(Path.GetFullPath(source), store.SourceRootDirectory);
        Assert.Equal(originalSettings, await File.ReadAllTextAsync(store.Workspace.SettingsPath));
        Assert.True(File.Exists(Path.Combine(store.Workspace.RootDirectory, "Profiles", "Default.json")));
        Assert.True(File.Exists(Path.Combine(store.Workspace.RootDirectory, "Config", "ComponentLayouts", "Default.json")));
        Assert.True(File.Exists(Path.Combine(store.Workspace.RootDirectory, "Config", "Automations", "Default.json")));

        store.Workspace.Set("IsMainWindowVisible", false);
        await store.SaveWorkspaceSettingsAsync();

        Assert.Equal(originalSettings, await File.ReadAllTextAsync(settingsPath));
        var local = JsonNode.Parse(await File.ReadAllTextAsync(store.Workspace.SettingsPath))!.AsObject();
        Assert.False(local["IsMainWindowVisible"]!.GetValue<bool>());
        Assert.True(local["FutureField"]!["keep"]!.GetValue<bool>());
    }

    [Fact]
    public async Task MishaNativeMainWindowRendererUsesRealComponentProfileWithoutPlaceholders()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "ClassIslandSource");
        Directory.CreateDirectory(Path.Combine(source, "Profiles"));
        Directory.CreateDirectory(Path.Combine(source, "Config", "ComponentLayouts"));
        var settingsPath = Path.Combine(source, "Settings.json");
        await File.WriteAllTextAsync(settingsPath, """
        {
          "SelectedProfile":"Default.json",
          "CurrentComponentConfig":"Default",
          "IsMainWindowVisible":true,
          "Scale":1.0,
          "Opacity":0.5,
          "RadiusX":8
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(source, "Profiles", "Default.json"), """
        {
          "Name":"测试",
          "Subjects":{},
          "TimeLayouts":{},
          "ClassPlans":{}
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(source, "Config", "ComponentLayouts", "Default.json"), """
        {
          "Lines":[{
            "IsMainLine":true,
            "Children":[
              {"Id":"df3f8295-21f6-482e-bada-fa0e5f14bb66","Settings":null},
              {"Id":"9e1af71d-8f77-4b21-a342-448787104dd9","Settings":{"ShowSeconds":true}},
              {"Id":"ee8f66bd-c423-4e7c-ab46-aa9976b00e08","Settings":{"TextContent":"真实文本","FontSize":18}},
              {"Id":"00000000-0000-0000-0000-000000000123","NameCache":"第三方组件","Settings":{"Future":true}}
            ]
          }]
        }
        """);

        var store = new MishaPlatformStore(Path.Combine(root.Path, "ExusiAIStore"));
        await store.AttachWorkspaceAsync(settingsPath);
        var layout = await ClassIslandComponentLayoutDocument.LoadAsync(store.Workspace!.CurrentComponentLayoutPath!);

        Exception? failure = null;
        System.Windows.FrameworkElement? view = null;
        var warnings = new List<string>();
        var thread = new Thread(() =>
        {
            try
            {
                var tickers = new List<Action<DateTime>>();
                view = MishaNativeMainWindowRenderer.Build(store, layout, tickers, warnings.Add);
                foreach (var ticker in tickers) ticker(DateTime.Now);
                view.Measure(new System.Windows.Size(1920, 1080));
                view.Arrange(new System.Windows.Rect(0, 0, view.DesiredSize.Width, view.DesiredSize.Height));
                view.UpdateLayout();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Misha main-window renderer smoke test timed out.");
        Assert.Null(failure);
        Assert.NotNull(view);
        Assert.Contains("00000000-0000-0000-0000-000000000123", warnings);
    }

    [Fact]
    public async Task RemovingMiddleClassTimePointKeepsClassPlanAlignment()
    {
        using var root = new TemporaryDirectory();
        var profilePath = Path.Combine(root.Path, "Profile.json");
        const string s1 = "11111111-1111-4111-8111-111111111111";
        const string s2 = "22222222-2222-4222-8222-222222222222";
        const string s3 = "33333333-3333-4333-8333-333333333333";
        const string layout = "44444444-4444-4444-8444-444444444444";
        const string plan = "55555555-5555-4555-8555-555555555555";
        var json = """
        {
          "Name":"索引测试",
          "Subjects":{
            "S1":{"Name":"第一节"},
            "S2":{"Name":"第二节"},
            "S3":{"Name":"第三节"}
          },
          "TimeLayouts":{
            "LAYOUT":{"Name":"布局","Layouts":[
              {"StartTime":"08:00:00","EndTime":"08:40:00","TimeType":0},
              {"StartTime":"08:40:00","EndTime":"08:50:00","TimeType":1},
              {"StartTime":"08:50:00","EndTime":"09:30:00","TimeType":0},
              {"StartTime":"09:40:00","EndTime":"10:20:00","TimeType":0}
            ]}
          },
          "ClassPlans":{
            "PLAN":{"Name":"课表","TimeLayoutId":"LAYOUT","IsEnabled":true,"TimeRule":{"Type":0,"WeekDay":1},"Classes":[
              {"SubjectId":"S1","IsEnabled":true},
              {"SubjectId":"S2","IsEnabled":true},
              {"SubjectId":"S3","IsEnabled":true}
            ]}
          }
        }
        """
        .Replace("S1", s1, StringComparison.Ordinal)
        .Replace("S2", s2, StringComparison.Ordinal)
        .Replace("S3", s3, StringComparison.Ordinal)
        .Replace("LAYOUT", layout, StringComparison.Ordinal)
        .Replace("PLAN", plan, StringComparison.Ordinal);
        await File.WriteAllTextAsync(profilePath, json);

        var document = await ClassIslandProfileDocument.LoadAsync(profilePath);
        var timePoints = Assert.Single(document.TimeLayouts).Items;
        document.RemoveTimeLayoutItem(layout, timePoints[2].Node);

        var lessons = Assert.Single(document.ClassPlans).Lessons;
        Assert.Equal(2, lessons.Count);
        Assert.Equal("第一节", lessons[0].SubjectName);
        Assert.Equal("第三节", lessons[1].SubjectName);
    }

    [Fact]
    public void MishaRealDataPagesConstructWithoutCreatingConfiguration()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var store = new MishaPlatformStore();
                System.Windows.FrameworkElement[] pages =
                [
                    new MishaWorkspacePage(store),
                    new MishaMainWindowSettingsPage(store),
                    new MishaThemeCompatibilityPage(store),
                    new MishaSyncPage(store),
                    new MishaSubjectsPage(store),
                    new MishaTimeLayoutsPage(store),
                    new MishaClassPlansPage(store),
                    new MishaClassPlanGroupsPage(store),
                    new MishaOrderedSchedulesPage(store),
                    new MishaScheduleModePage(store),
                    new MishaTemporarySchedulePage(store),
                    new MishaDataPage(store),
                    new MishaNativeJsonConfigPage(store, "设置", "测试", workspace => workspace.SettingsPath),
                    new MishaComponentLayoutsPage(store),
                    new MishaAutomationEditorPage(store),
                    new MishaAboutPage()
                ];

                foreach (var page in pages)
                {
                    page.Measure(new System.Windows.Size(1280, 800));
                    page.Arrange(new System.Windows.Rect(0, 0, 1280, 800));
                    page.UpdateLayout();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Misha real-data page smoke test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public async Task ComponentLayoutRoundTripsUnknownSettingsAndNativeComponentIds()
    {
        using var root = new TemporaryDirectory();
        var path = Path.Combine(root.Path, "Components.json");
        await File.WriteAllTextAsync(path, """
        {
          "FutureRoot":{"keep":true},
          "Lines":[
            {
              "IsMainLine":true,
              "FutureLineField":42,
              "Children":[
                {
                  "Id":"df3f8295-21f6-482e-bada-fa0e5f14bb66",
                  "NameCache":"日期",
                  "Settings":{"FutureSetting":"keep"},
                  "FutureComponentField":true
                }
              ]
            }
          ]
        }
        """);

        var document = await ClassIslandComponentLayoutDocument.LoadAsync(path);
        var line = Assert.Single(document.Lines);
        var component = Assert.Single(line.Components);
        Assert.Equal("日期", component.DisplayName);
        Assert.Equal("df3f8295-21f6-482e-bada-fa0e5f14bb66", component.Id);

        document.AddComponent(line, ClassIslandComponentCatalog.BuiltIns.Single(x => x.Name == "时钟"));
        await document.SaveAsync();

        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(saved["FutureRoot"]?["keep"]?.GetValue<bool>());
        Assert.Equal(42, saved["Lines"]?[0]?["FutureLineField"]?.GetValue<int>());
        Assert.True(saved["Lines"]?[0]?["Children"]?[0]?["FutureComponentField"]?.GetValue<bool>());
        Assert.Equal("keep", saved["Lines"]?[0]?["Children"]?[0]?["Settings"]?["FutureSetting"]?.GetValue<string>());
        Assert.Equal("9e1af71d-8f77-4b21-a342-448787104dd9",
            saved["Lines"]?[0]?["Children"]?[1]?["Id"]?.GetValue<string>());
        Assert.Null(saved["Lines"]?[0]?["Children"]?[1]?["Settings"]);
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public async Task AutomationRoundTripsUnknownWorkflowNodesWithoutExecutingActions()
    {
        using var root = new TemporaryDirectory();
        var path = Path.Combine(root.Path, "Automation.json");
        await File.WriteAllTextAsync(path, """
        [
          {
            "FutureWorkflowField":"keep",
            "Triggers":[
              {
                "Id":"classisland.lessons.onClass",
                "Settings":null,
                "FutureTriggerField":1
              }
            ],
            "IsConditionEnabled":false,
            "Ruleset":{"Mode":0,"IsReversed":false,"Groups":[],"FutureRulesetField":2},
            "ActionSet":{
              "Name":"现有工作流",
              "Actions":[
                {
                  "Id":"classisland.showNotification",
                  "Settings":{"Mask":"测试"},
                  "FutureActionField":3
                }
              ],
              "IsEnabled":true,
              "IsRevertEnabled":false,
              "FutureActionSetField":4
            }
          }
        ]
        """);

        var document = await ClassIslandAutomationDocument.LoadAsync(path);
        var workflow = Assert.Single(document.Workflows);
        Assert.Equal("现有工作流", workflow.Name);
        Assert.Equal("上课时", Assert.Single(workflow.Triggers).DisplayName);
        Assert.Equal("显示提醒", Assert.Single(workflow.Actions).DisplayName);

        document.AddTrigger(workflow, ClassIslandAutomationCatalog.Triggers.Single(x => x.Id == "classisland.cron"));
        document.AddAction(workflow, ClassIslandAutomationCatalog.Actions.Single(x => x.Id == "classisland.action.sleep"));
        await document.SaveAsync();

        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsArray();
        Assert.Equal("keep", saved[0]?["FutureWorkflowField"]?.GetValue<string>());
        Assert.Equal(1, saved[0]?["Triggers"]?[0]?["FutureTriggerField"]?.GetValue<int>());
        Assert.Equal(2, saved[0]?["Ruleset"]?["FutureRulesetField"]?.GetValue<int>());
        Assert.Equal(3, saved[0]?["ActionSet"]?["Actions"]?[0]?["FutureActionField"]?.GetValue<int>());
        Assert.Equal(4, saved[0]?["ActionSet"]?["FutureActionSetField"]?.GetValue<int>());
        Assert.Equal("classisland.cron", saved[0]?["Triggers"]?[1]?["Id"]?.GetValue<string>());
        Assert.Null(saved[0]?["Triggers"]?[1]?["Settings"]);
        Assert.Equal("classisland.action.sleep", saved[0]?["ActionSet"]?["Actions"]?[1]?["Id"]?.GetValue<string>());
        Assert.Null(saved[0]?["ActionSet"]?["Actions"]?[1]?["Settings"]);
        Assert.True(File.Exists(path + ".bak"));
    }

    private static bool WaitForCollection(WeakReference reference, TimeSpan timeout)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            if (!reference.IsAlive) return true;
            Thread.Sleep(25);
        }
        while (stopwatch.Elapsed < timeout);

        return !reference.IsAlive;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> LoadAndStopSamplePluginAsync(string rootPath)
    {
        var package = Path.Combine(rootPath, "sample");
        await WriteManifestAsync(package, ValidManifest);
        File.Copy(typeof(SamplePlugin).Assembly.Location, Path.Combine(package, "ExusiAI.Plugin.Sample.dll"));
        File.Copy(typeof(ExtensionPluginBase).Assembly.Location, Path.Combine(package, "ExusiAI.Extension.SDK.dll"));
        var runtime = new ExtensionRuntime(CreateDiscovery(), NullLogger<ExtensionRuntime>.Instance);
        await runtime.DiscoverAsync(rootPath);
        await runtime.StartAsync();
        var entry = Assert.Single(runtime.Entries);
        Assert.Equal(PackageState.Running, entry.State);
        Assert.NotNull(entry.Instance);
        var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(entry.Instance!.GetType().Assembly);
        Assert.NotNull(context);
        Assert.NotEqual(System.Runtime.Loader.AssemblyLoadContext.Default, context);
        var navigation = Assert.IsAssignableFrom<IWpfNavigationExtension>(entry.Instance);
        Assert.Equal("sample.hello", Assert.Single(navigation.GetNavigationPages()).Route);
        var weakReference = new WeakReference(context);
        await runtime.DisposeAsync();
        return weakReference;
    }

    private static PackageDiscoveryService CreateDiscovery() => new(new(), new(), NullLogger<PackageDiscoveryService>.Instance);

    private static async Task WriteManifestAsync(string directory, PackageManifest manifest)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "package.json"), JsonSerializer.Serialize(manifest, ManifestJson.Options));
    }

    private static PackageManifest ValidManifest => new()
    {
        SchemaVersion = 1, Id = "exusiai.sample", Type = PackageType.Plugin, DisplayName = "Sample", Version = "0.1.0", ApiVersion = "1",
        Publisher = "ExusiAI", MinimumHostVersion = "0.1.0", EntryPoint = new() { Assembly = "ExusiAI.Plugin.Sample.dll", Type = "ExusiAI.Plugin.Sample.SamplePlugin" }
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "exusiai-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose()
        {
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
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(25);
                }
            }
        }
    }
}

public sealed class ThrowingPlugin : IExusiAIPlugin
{
    public Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken) => throw new InvalidOperationException("Expected test failure.");
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
