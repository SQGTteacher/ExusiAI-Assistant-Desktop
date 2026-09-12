using ExusiAI.Extension.Abstractions;
using ExusiAI.Extension.Runtime;
using ExusiAI.Extension.Wpf;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Desktop;

public sealed class WpfExtensionCoordinator(ExtensionRuntime runtime, WpfNavigationRegistry registry, ILogger<WpfExtensionCoordinator> logger) : IDisposable
{
    private readonly HashSet<string> attachedPackages = new(StringComparer.OrdinalIgnoreCase);
    private bool started;

    public void Start()
    {
        if (started) return;
        started = true;
        runtime.EntriesChanged += Runtime_OnEntriesChanged;
        Synchronize();
    }

    public void Dispose()
    {
        if (!started) return;
        runtime.EntriesChanged -= Runtime_OnEntriesChanged;
        foreach (var packageId in attachedPackages.ToArray()) registry.Unregister(packageId);
        attachedPackages.Clear();
        started = false;
        GC.SuppressFinalize(this);
    }

    private void Runtime_OnEntriesChanged(object? sender, EventArgs e) => Synchronize();

    private void Synchronize()
    {
        var runningEntries = runtime.Entries
            .Where(x => x.State == PackageState.Running && x.Instance is IWpfNavigationExtension)
            .ToDictionary(x => x.Package.Manifest.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var packageId in attachedPackages.Where(x => !runningEntries.ContainsKey(x)).ToArray())
        {
            registry.Unregister(packageId);
            attachedPackages.Remove(packageId);
        }

        foreach (var (packageId, entry) in runningEntries.Where(x => !attachedPackages.Contains(x.Key)))
        {
            try
            {
                registry.Register(packageId, ((IWpfNavigationExtension)entry.Instance!).GetNavigationPages());
                attachedPackages.Add(packageId);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "WPF contributions from {PackageId} were rejected.", packageId);
            }
        }
    }
}
