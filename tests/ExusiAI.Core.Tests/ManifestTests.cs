using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Core.Tests;

public sealed class ManifestTests
{
    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.1.0-alpha.1")]
    [InlineData("2.4.1+build.9")]
    public void SemanticVersionAcceptsValidVersions(string value) => Assert.True(SemanticVersion.TryParse(value, out _));

    [Theory]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("v1.0.0")]
    public void SemanticVersionRejectsInvalidVersions(string value) => Assert.False(SemanticVersion.TryParse(value, out _));

    [Fact]
    public void StableVersionSortsAfterPreRelease()
    {
        SemanticVersion.TryParse("1.0.0", out var stable);
        SemanticVersion.TryParse("1.0.0-rc.1", out var candidate);
        Assert.True(stable.CompareTo(candidate) > 0);
    }

    [Fact]
    public void ApiVersionIsIndependentFromPackageVersion()
    {
        Assert.True(ExtensionApiVersion.TryParse("1", out var api));
        Assert.Equal(1, api.Major);
        Assert.False(ExtensionApiVersion.TryParse("1.0.0", out _));
    }
}
