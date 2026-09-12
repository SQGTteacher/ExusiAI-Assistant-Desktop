using System.Text.Json;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.Runtime;
using ExusiAI.Extension.SDK;
using ExusiAI.Extension.Wpf;
using ExusiAI.Plugin.Sample;
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
        var package = Path.Combine(root.Path, "sample");
        await WriteManifestAsync(package, ValidManifest);
        File.Copy(typeof(SamplePlugin).Assembly.Location, Path.Combine(package, "ExusiAI.Plugin.Sample.dll"));
        File.Copy(typeof(ExtensionPluginBase).Assembly.Location, Path.Combine(package, "ExusiAI.Extension.SDK.dll"));
        await using var runtime = new ExtensionRuntime(CreateDiscovery(), NullLogger<ExtensionRuntime>.Instance);
        await runtime.DiscoverAsync(root.Path);
        await runtime.StartAsync();
        var entry = Assert.Single(runtime.Entries);
        Assert.Equal(PackageState.Running, entry.State);
        Assert.NotNull(entry.Instance);
        Assert.NotEqual(System.Runtime.Loader.AssemblyLoadContext.Default, System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(entry.Instance!.GetType().Assembly));
        var navigation = Assert.IsAssignableFrom<IWpfNavigationExtension>(entry.Instance);
        Assert.Equal("sample.hello", Assert.Single(navigation.GetNavigationPages()).Route);
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
