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
        var plugin = new ClassIslandPlugin();
        await plugin.InitializeAsync(new ProbeContext(), CancellationToken.None);
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
