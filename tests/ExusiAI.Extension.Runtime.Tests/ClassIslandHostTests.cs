using ExusiAI.Plugin.ClassIsland;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class ClassIslandHostTests
{
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
