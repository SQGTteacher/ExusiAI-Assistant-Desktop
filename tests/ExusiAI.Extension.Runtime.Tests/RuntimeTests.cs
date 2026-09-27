using ExusiAI.Plugin.ClassIsland;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class RuntimeTests
{
    [Fact]
    public void ClassIslandRuntimeDescriptorUsesNativeRuntime()
    {
        Assert.Equal("ClassIsland/ClassIsland", ClassIslandRuntimeDescriptor.UpstreamRepository);
        Assert.Equal("2.1.0.1", ClassIslandRuntimeDescriptor.RuntimeVersion);
        Assert.Equal("develop/v2/misha-alpha", ClassIslandRuntimeDescriptor.MishaBranch);
    }
}
