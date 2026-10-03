using System.Reflection;
using System.Runtime.Versioning;
using ClassIsland;

#if EXUSIAI_EMBEDDED_SOURCE
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyInformationalVersion("2.2.0-exusiai-embedded")]
#elif NIX
[assembly: AssemblyVersion("0.0.0.0")]
[assembly: AssemblyInformationalVersion("NIXBUILD+NIXBUILD_LONG_HASH")]
#else
[assembly: AssemblyVersion(GitInfo.Tag)]
[assembly: AssemblyInformationalVersion($"{GitInfo.Tag}+{GitInfo.CommitHash}")]
#endif

[assembly: AssemblyTitle("ClassIsland")]
[assembly: AssemblyProduct("ClassIsland")]
#if NETCOREAPP
// [assembly: SupportedOSPlatform("Windows")]
#endif
#if Platforms_MacOs
[assembly:SupportedOSPlatform("macos")]
#endif
 
