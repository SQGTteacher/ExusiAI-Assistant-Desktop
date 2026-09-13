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
        for (var attempt = 0; attempt < 5 && loadContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.False(loadContext.IsAlive);
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
        Assert.Equal(new[]
        {
            "misha.dashboard", "misha.schedule", "misha.components", "misha.automation",
            "misha.extensions", "misha.data", "misha.about"
        }, pages.Select(page => page.Route));

        Exception? pageFailure = null;
        var pageThread = new Thread(() =>
        {
            try
            {
                foreach (var page in pages)
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
            }
            catch (Exception exception) { pageFailure = exception; }
        });
        pageThread.SetApartmentState(ApartmentState.STA);
        pageThread.Start();
        Assert.True(pageThread.Join(TimeSpan.FromSeconds(15)), "WPF page smoke test timed out.");
        Assert.Null(pageFailure);
    }

    [Fact]
    public void ClassIslandProfileInteropImportsExportsAndPreservesUnknownFields()
    {
        const string subjectId = "11111111-1111-4111-8111-111111111111";
        const string layoutId = "22222222-2222-4222-8222-222222222222";
        var json = """
        {
          "Name":"兼容性测试",
          "FutureField":{"keep":true},
          "Subjects":{"SUBJECT_ID":{"Name":"数学","Initial":"数","TeacherName":"周老师","IsOutDoor":false}},
          "TimeLayouts":{"LAYOUT_ID":{"Name":"标准时间表","Layouts":[{"StartTime":"08:00:00","EndTime":"08:40:00","TimeType":0}]}},
          "ClassPlans":{"33333333-3333-4333-8333-333333333333":{"Name":"周一","TimeLayoutId":"LAYOUT_ID","IsEnabled":true,"TimeRule":{"WeekCountDiv":1},"Classes":[{"SubjectId":"SUBJECT_ID","IsEnabled":true}]}}
        }
        """.Replace("SUBJECT_ID", subjectId, StringComparison.Ordinal).Replace("LAYOUT_ID", layoutId, StringComparison.Ordinal);
        var state = MishaPlatformState.CreateDefault();

        var original = ClassIslandProfileInterop.Parse(json, state);
        var exported = JsonNode.Parse(ClassIslandProfileInterop.Write(state, original))!.AsObject();

        Assert.Equal("兼容性测试", state.ProfileName);
        Assert.Equal("数学", Assert.Single(state.Schedule).Subject);
        Assert.Equal("08:00", Assert.Single(state.Schedule).Start);
        Assert.True(exported["FutureField"]?["keep"]?.GetValue<bool>());
        Assert.NotNull(exported["Subjects"]);
        Assert.NotNull(exported["TimeLayouts"]);
        Assert.NotNull(exported["ClassPlans"]);
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
