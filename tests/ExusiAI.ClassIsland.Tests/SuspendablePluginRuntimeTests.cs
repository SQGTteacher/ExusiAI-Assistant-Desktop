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

    [Theory]
    [InlineData("Suspend")]
    [InlineData("Resume")]
    [InlineData("Stop")]
    public async Task CancellationAfterPartialTransitionTerminatesInstance(string operation)
    {
        var root = await CreatePackageAsync();
        try
        {
            await using var runtime = CreateRuntime();
            await runtime.DiscoverAsync(root);
            await runtime.StartAsync();
            var instance = Assert.Single(runtime.Entries).Instance!;

            if (operation is "Resume" or "Stop")
                await runtime.SetEnabledAsync("test.suspendable", false);
            Set(instance, "Block" + operation, true);
            if (operation == "Stop") Set(instance, "FailStop", true);
            var states = new List<PackageState>();
            runtime.EntriesChanged += (_, _) => states.Add(Assert.Single(runtime.Entries).State);
            using var cancellation = new CancellationTokenSource();
            var transition = operation == "Stop"
                ? runtime.StopAsync(cancellation.Token)
                : runtime.SetEnabledAsync("test.suspendable", operation == "Resume", cancellation.Token);
            await WaitForTrueAsync(instance, operation + "Entered");
            Assert.Equal(operation == "Resume", ReadBool(instance, "SessionActive"));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transition);

            var failed = Assert.Single(runtime.Entries);
            Assert.Equal(PackageState.Failed, failed.State);
            Assert.Null(failed.Instance);
            Assert.Equal(operation == "Resume" ? "plugin-start-cancelled" : "plugin-stop-cancelled", failed.FailureCode);
            Assert.DoesNotContain(PackageState.Running, states);
            Assert.DoesNotContain(PackageState.Disabled, states);
            Assert.True(Read(instance, "StopCount") >= 1);
            Assert.Equal(1, Read(instance, "DisposeCount"));
            Assert.False(ReadBool(instance, "SessionActive"));
            await runtime.SetEnabledAsync("test.suspendable", true);
            Assert.Equal(PackageState.Running, Assert.Single(runtime.Entries).State);
            Assert.NotSame(instance, Assert.Single(runtime.Entries).Instance);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeTransitionPreservesExistingState(bool suspended)
    {
        var root = await CreatePackageAsync();
        try
        {
            await using var runtime = CreateRuntime();
            await runtime.DiscoverAsync(root);
            await runtime.StartAsync();
            var instance = Assert.Single(runtime.Entries).Instance!;
            if (suspended) await runtime.SetEnabledAsync("test.suspendable", false);
            var before = Assert.Single(runtime.Entries);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                runtime.SetEnabledAsync("test.suspendable", suspended, cancellation.Token));
            Assert.Equal(before, Assert.Single(runtime.Entries));
            Assert.Equal(0, Read(instance, "StopCount"));
            Assert.Equal(0, Read(instance, "DisposeCount"));
            if (suspended) await runtime.SetEnabledAsync("test.suspendable", true);
            Assert.Same(instance, Assert.Single(runtime.Entries).Instance);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CancellationDuringFirstStartCleansPartialInstance()
    {
        var root = await CreatePackageAsync();
        try
        {
            await using var runtime = CreateRuntime();
            await runtime.DiscoverAsync(root);
            object? instance = null;
            runtime.EntriesChanged += (_, _) =>
            {
                var entry = Assert.Single(runtime.Entries);
                if (entry.State != PackageState.Initialized) return;
                instance = entry.Instance!;
                Set(instance, "BlockStart", true);
            };
            using var cancellation = new CancellationTokenSource();
            var starting = runtime.StartAsync(cancellation.Token);
            Assert.NotNull(instance);
            await WaitForTrueAsync(instance!, "StartEntered");
            Assert.True(ReadBool(instance!, "SessionActive"));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
            var failed = Assert.Single(runtime.Entries);
            Assert.Equal(PackageState.Failed, failed.State);
            Assert.Equal("plugin-start-cancelled", failed.FailureCode);
            Assert.Null(failed.Instance);
            Assert.Equal(1, Read(instance!, "StopCount"));
            Assert.Equal(1, Read(instance!, "DisposeCount"));
            Assert.False(ReadBool(instance!, "SessionActive"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SuspendFailureUsesTerminalCleanupAndDoesNotReportDisabled()
    {
        var root = await CreatePackageAsync();
        try
        {
            await using var runtime = CreateRuntime();
            await runtime.DiscoverAsync(root);
            await runtime.StartAsync();
            var instance = Assert.Single(runtime.Entries).Instance!;
            Set(instance, "FailSuspend", true);

            await runtime.SetEnabledAsync("test.suspendable", false);

            var failed = Assert.Single(runtime.Entries);
            Assert.Equal(PackageState.Failed, failed.State);
            Assert.Null(failed.Instance);
            Assert.Equal(1, Read(instance, "StopCount"));
            Assert.Equal(1, Read(instance, "DisposeCount"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ResumeFailureUsesTerminalCleanupAndDoesNotReportRunning()
    {
        var root = await CreatePackageAsync();
        try
        {
            await using var runtime = CreateRuntime();
            await runtime.DiscoverAsync(root);
            await runtime.StartAsync();
            var instance = Assert.Single(runtime.Entries).Instance!;
            await runtime.SetEnabledAsync("test.suspendable", false);
            Set(instance, "FailResume", true);

            await runtime.SetEnabledAsync("test.suspendable", true);

            var failed = Assert.Single(runtime.Entries);
            Assert.Equal(PackageState.Failed, failed.State);
            Assert.Null(failed.Instance);
            Assert.Equal(1, Read(instance, "StopCount"));
            Assert.Equal(1, Read(instance, "DisposeCount"));
        }
        finally { Directory.Delete(root, true); }
    }

    private static ExtensionRuntime CreateRuntime() => new(
        new(new(), new(), NullLogger<PackageDiscoveryService>.Instance),
        NullLogger<ExtensionRuntime>.Instance);

    private static int Read(object instance, string property) =>
        (int)instance.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance)!;

    private static bool ReadBool(object instance, string property) =>
        (bool)instance.GetType().GetProperty(property)!.GetValue(instance)!;

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
    public bool FailSuspend { get; set; }
    public bool BlockResume { get; set; }
    public bool ResumeEntered { get; private set; }
    public bool FailResume { get; set; }
    public bool SessionActive { get; private set; }
    public bool BlockStart { get; set; }
    public bool StartEntered { get; private set; }
    public bool BlockStop { get; set; }
    public bool StopEntered { get; private set; }
    public bool FailStop { get; set; }

    public Task InitializeAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InitializeCount++;
        return Task.CompletedTask;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SessionActive = true;
        StartEntered = true;
        if (BlockStart) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        StartCount++;
    }

    public async Task SuspendAsync(CancellationToken cancellationToken)
    {
        SessionActive = false;
        SuspendEntered = true;
        if (BlockSuspend) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (FailSuspend) throw new InvalidOperationException("Suspend failed.");
        SuspendCount++;
    }

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        SessionActive = true;
        ResumeEntered = true;
        if (BlockResume) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (FailResume) throw new InvalidOperationException("Resume failed.");
        ResumeCount++;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCount++;
        SessionActive = false;
        StopEntered = true;
        if (BlockStop && cancellationToken.CanBeCanceled)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (FailStop) throw new InvalidOperationException("Stop failed.");
    }

    public void Dispose()
    {
        DisposeCount++;
        SessionActive = false;
    }
}
