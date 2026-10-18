using ExusiAI.Plugin.ClassIsland;
using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.Wpf;
using System.Reflection;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class ClassIslandHostTests
{
    [Fact]
    public async Task ActivationAndContributionRegistrationDoNotStartNativeGui()
    {
        var root = Path.Combine(Path.GetTempPath(), "classisland-activation-" + Guid.NewGuid().ToString("N"));
        var plugin = new ClassIslandPlugin();
        await plugin.InitializeAsync(new ProbeContext(), CancellationToken.None);
        var hostField = typeof(ClassIslandPlugin).GetField("host", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((ClassIslandHost)hostField.GetValue(plugin)!).Dispose();
        hostField.SetValue(plugin, new ClassIslandHost(root, root));
        try
        {
            await plugin.StartAsync(CancellationToken.None);
            Assert.Single(plugin.GetNavigationPages());
            Assert.Equal(6, plugin.GetTrayCommands().Count);
            plugin.ApplyHostTheme(new HostTheme(true, "#C84450"));
            var host = (ClassIslandHost)typeof(ClassIslandPlugin)
                .GetField("host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plugin)!;
            Assert.True(host.IsStopped);
            Assert.False(host.IsVisible);
            Assert.Null(host.LastStartupError);
        }
        finally { await plugin.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task ActivationHonorsCancellationWithoutLaunchingGui()
    {
        var plugin = new ClassIslandPlugin();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            plugin.StartAsync(new CancellationToken(canceled: true)));
    }

    private sealed class ProbeContext : IExtensionPackageContext, IExtensionLogger
    {
        public string PackageId => "exusiai.classisland";
        public string PackageDirectory => Path.Combine(Path.GetTempPath(), "missing-classisland-runtime");
        public IExtensionLogger Logger => this;
        public void Information(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    [Fact]
    public void StartupPreferenceIsOptInPersistentAndDoesNotLaunchRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "classisland-startup-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var host = new ClassIslandHost(root, root))
            {
                Assert.False(host.ShowOnHostStartup);
                host.SetShowOnHostStartup(true);
                Assert.True(host.IsStopped);
            }
            using (var host = new ClassIslandHost(root, root))
            {
                Assert.True(host.ShowOnHostStartup);
                host.SetShowOnHostStartup(false);
            }
            using var restored = new ClassIslandHost(root, root);
            Assert.False(restored.ShowOnHostStartup);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void StartupPreferenceSaveFailureKeepsSavedChoice()
    {
        var root = Path.Combine(Path.GetTempPath(), "classisland-startup-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var host = new ClassIslandHost(root, root);
            host.SetShowOnHostStartup(true);
            Directory.CreateDirectory(Path.Combine(root, "ExusiAIStartup.json.tmp"));
            var error = Record.Exception(() => host.SetShowOnHostStartup(false));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.True(host.ShowOnHostStartup);
            using var restored = new ClassIslandHost(root, root);
            Assert.True(restored.ShowOnHostStartup);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void MalformedStartupPreferenceFallsBackToOnDemand()
    {
        var root = Path.Combine(Path.GetTempPath(), "classisland-startup-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "ExusiAIStartup.json"), "broken");
            using var host = new ClassIslandHost(root, root);
            Assert.False(host.ShowOnHostStartup);
            Assert.False(host.RequiresRestart);
            host.Dispose();
            Assert.True(host.RequiresRestart);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ConcurrentCommandsAfterStopCannotLaunchRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "classisland-host-" + Guid.NewGuid().ToString("N"));
        try
        {
            var native = Path.Combine(root, "NativeClassIsland");
            Directory.CreateDirectory(native);
            File.WriteAllBytes(Path.Combine(native, "ClassIsland.Desktop.dll"), []);
            File.WriteAllBytes(Path.Combine(native, "ClassIsland.dll"), []);
            using var host = new ClassIslandHost(Path.Combine(root, "Data"), root);
            Assert.True(host.IsAvailable);
            host.Dispose();
            Parallel.For(0, 16, _ =>
            {
                Assert.False(host.Start());
                Assert.False(host.OpenSettings());
                host.Hide();
                host.Dispose();
            });
            Assert.True(host.IsStopped);
            Assert.False(host.IsVisible);
            Assert.Contains("重新启动", host.LastStartupError);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("profile")]
    [InlineData("edit")]
    [InlineData("class-swap")]
    public void MissingRuntimeReportsFailureForEveryNativeEntry(string page)
    {
        var root = Path.Combine(Path.GetTempPath(), "classisland-host-" + Guid.NewGuid().ToString("N"));
        using var host = new ClassIslandHost(root, root);
        Assert.False(host.OpenPage(page));
        Assert.True(host.IsStopped);
        Assert.Contains("assemblies are missing", host.LastStartupError);
    }
}
