using ExusiAI.Plugin.ArkPets;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class ArkPetsTests
{
    [Fact]
    public void ArkPetsPluginTestsRemainIndependentFromClassIslandRuntime()
    {
        var controller = new ArkPetsController(new TestExtensionLogger());
        Assert.NotNull(controller);
    }
}
