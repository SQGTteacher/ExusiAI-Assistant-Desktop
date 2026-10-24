using System.Reflection;
using System.Text.Json;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExusiAI.ClassIsland.Tests;

public sealed class SuspendablePluginRuntimeTests
{
    [Fact]
    public async Task DisableAndReenableRetainInitializedInstanceUntilTerminalStop()
    {
        var root = await CreatePackageAsync();
        try
        {
            var runtime = CreateRuntime();
            await runtime.DiscoverAsync(root);
            await runtime.StartAsync();
            var instance = Assert.Single(runtime.Entries).Instance!;
            Assert.Equal(1, Read(instance, "InitializeCount"));
            Assert.Equal(1, Read(instance, "StartCount"));

            await runtime.SetEnabledAsync("test.suspendable", false);
            var disabled = Assert.Single(runtime.Entries);
            Assert.Equal(PackageState.Disabled, disabled.State);
            Assert.Null(disabled.Instance);
            Assert.Equal(1, Read(instance, "SuspendCount"));
            Assert.Equal(0, Read(instance, "StopCount"));
            Assert.Equal(0, Read(instance, "DisposeCount"));

            await runtime.SetEnabledAsync("test.suspendable", true);
            var resumed = Assert.Single(runtime.Entries);
            Assert.Equal(PackageState.Running, resumed.State);
            Assert.Same(instance, resumed.Instance);
            Assert.Equal(1, Read(instance, "InitializeCount"));
            Assert.Equal(1, Read(instance, "ResumeCount"));

            await runtime.SetEnabledAsync("test.suspendable", false);
            await runtime.DisposeAsync();
            Assert.Equal(1, Read(instance, "StopCount"));
            Assert.Equal(1, Read(instance, "DisposeCount"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RemovingSuspendedPluginPerformsTerminalStop()
    {
        var root = await CreatePackageAsync();
        try
        {
            await using var runtime = CreateRuntime();
            await runtime.DiscoverAsync(root);
            await runtime.StartAsync();
            var instance = Assert.Single(runtime.Entries).Instance!;
            await runtime.SetEnabledAsync("test.suspendable", false);

            await runtime.RemoveAsync("test.suspendable");

            Assert.Empty(runtime.Entries);
            Assert.Equal(1, Read(instance, "StopCount"));
            Assert.Equal(1, Read(instance, "DisposeCount"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoveryCannotDiscardSuspendedInstance()
    {
        var root = await CreatePackageAsync();
        try
        {
            await using var runtime = CreateRuntime();
            await runtime.DiscoverAsync(root);
            await runtime.StartAsync();
            var instance = Assert.Single(runtime.Entries).Instance!;
            await runtime.SetEnabledAsync("test.suspendable", false);

            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.DiscoverAsync(root));

            Assert.Equal(PackageState.Disabled, Assert.Single(runtime.Entries).State);
            Assert.Equal(0, Read(instance, "StopCount"));
            Assert.Equal(0, Read(instance, "DisposeCount"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CancellationRestoresRunningOrDisabledState()
    {
        var root = await CreatePackageAsync();
        try
        {
            await using var runtime = CreateRuntime();
            await runtime.DiscoverAsync(root);
            await runtime.StartAsync();
            var instance = Assert.Single(runtime.Entries).Instance!;

            Set(instance, "BlockSuspend", true);
            using (var cancellation = new CancellationTokenSource())
            {
                var disabling = runtime.SetEnabledAsync("test.suspendable", false, cancellation.Token);
                await WaitForTrueAsync(instance, "SuspendEntered");
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disabling);
            }
            Assert.Equal(PackageState.Running, Assert.Single(runtime.Entries).State);
            Assert.Same(instance, Assert.Single(runtime.Entries).Instance);

            Set(instance, "BlockSuspend", false);
            await runtime.SetEnabledAsync("test.suspendable", false);
            Set(instance, "BlockResume", true);
            using (var cancellation = new CancellationTokenSource())
            {
                var enabling = runtime.SetEnabledAsync("test.suspendable", true, cancellation.Token);
                await WaitForTrueAsync(instance, "ResumeEntered");
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enabling);
            }
            Assert.Equal(PackageState.Disabled, Assert.Single(runtime.Entries).State);
            Assert.Null(Assert.Single(runtime.Entries).Instance);

            Set(instance, "BlockResume", false);
            await runtime.SetEnabledAsync("test.suspendable", true);
            Assert.Equal(PackageState.Running, Assert.Single(runtime.Entries).State);
            Assert.Same(instance, Assert.Single(runtime.Entries).Instance);
        }
        finally { Directory.Delete(root, true); }
    }

    private static ExtensionRuntime CreateRuntime() => new(
        new(new(), new(), NullLogger<PackageDiscoveryService>.Instance),
        NullLogger<ExtensionRuntime>.Instance);

    private static int Read(object instance, string property) =>
        (int)instance.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance)!;

    private static void Set(object instance, string property, bool value) =>
        instance.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(instance, value);

    private static async Task WaitForTrueAsync(object instance, string property)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((bool)instance.GetType().GetProperty(property)!.GetValue(instance)!) return;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Plugin did not enter {property}.");
    }

    private static async Task<string> CreatePackageAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "exusiai-suspendable-tests", Guid.NewGuid().ToString("N"));
        var package = Path.Combine(root, "package");
        Directory.CreateDirectory(package);
        var manifest = new PackageManifest
        {
            SchemaVersion = 1,
            Id = "test.suspendable",
            Type = PackageType.Plugin,
            DisplayName = "Suspendable test plugin",
            Version = "1.0.0",
            ApiVersion = "1",
            Publisher = "ExusiAI",
            MinimumHostVersion = "0.2.0-preview.4",
            EntryPoint = new()
            {
                Assembly = "ExusiAI.ClassIsland.Tests.dll",
                Type = typeof(PortableSuspendablePlugin).FullName!
            }
        };
        await File.WriteAllTextAsync(Path.Combine(package, "package.json"),
            JsonSerializer.Serialize(manifest, ManifestJson.Options));
        File.Copy(typeof(SuspendablePluginRuntimeTests).Assembly.Location,
            Path.Combine(package, "ExusiAI.ClassIsland.Tests.dll"));
        return root;
    }
}

public sealed class PortableSuspendablePlugin : ISuspendablePlugin, IDisposable
{
    public int InitializeCount { get; private set; }
    public int StartCount { get; private set; }
    public int SuspendCount { get; private set; }
    public int ResumeCount { get; private set; }
    public int StopCount { get; private set; }
    public int DisposeCount { get; private set; }
    public bool BlockSuspend { get; set; }
    public bool SuspendEntered { get; private set; }
    public bool BlockResume { get; set; }
    public bool ResumeEntered { get; private set; }

    public Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InitializeCount++;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCount++;
        return Task.CompletedTask;
    }

    public async Task SuspendAsync(CancellationToken cancellationToken)
    {
        SuspendEntered = true;
        if (BlockSuspend) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        SuspendCount++;
    }

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        ResumeEntered = true;
        if (BlockResume) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        ResumeCount++;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCount++;
        return Task.CompletedTask;
    }

    public void Dispose() => DisposeCount++;
}
