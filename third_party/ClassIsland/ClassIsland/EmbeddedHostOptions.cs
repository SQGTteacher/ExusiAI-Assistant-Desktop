using System;
using System.IO;

namespace ClassIsland;

/// <summary>
/// Hosts the original Avalonia application inside another desktop process.
/// This must be configured before AppEntry and remains process-wide because
/// the upstream application and its services use static state.
/// </summary>
public sealed class EmbeddedHostOptions
{
    public EmbeddedHostOptions(string dataDirectory, string packageDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        PackageDirectory = Path.GetFullPath(packageDirectory);
        if (DataDirectory == PackageDirectory)
            throw new ArgumentException("The embedded data directory must be separate from the read-only package directory.");
    }

    public string DataDirectory { get; }
    public string PackageDirectory { get; }
}
