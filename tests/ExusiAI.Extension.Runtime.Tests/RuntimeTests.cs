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
                    new MishaDashboardPage(store),
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
