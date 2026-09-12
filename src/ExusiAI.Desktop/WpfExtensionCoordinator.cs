using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.Runtime;
using ExusiAI.Extension.Wpf;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Desktop;

public sealed class WpfExtensionCoordinator(WpfNavigationRegistry registry, ILogger<WpfExtensionCoordinator> logger)
{
    private readonly HashSet<string> attachedPackages = new(StringComparer.OrdinalIgnoreCase);

    public void Attach(IEnumerable<ExtensionRuntimeEntry> entries)
    {
        foreach (var entry in entries.Where(x => x.State == PackageState.Running && x.Instance is IWpfNavigationExtension))
        {
            var packageId = entry.Package.Manifest.Id;
            if (!attachedPackages.Add(packageId)) continue;
            try { registry.Register(packageId, ((IWpfNavigationExtension)entry.Instance!).GetNavigationPages()); }
            catch (Exception exception)
            {
                attachedPackages.Remove(packageId);
                logger.LogError(exception, "WPF contributions from {PackageId} were rejected.", packageId);
            }
        }
    }

    public void DetachAll()
    {
        foreach (var packageId in attachedPackages.ToArray()) registry.Unregister(packageId);
        attachedPackages.Clear();
    }
}
