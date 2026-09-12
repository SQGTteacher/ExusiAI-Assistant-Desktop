using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using ExusiAI.Extension.Abstractions;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Extension.Runtime;

public sealed record ExtensionRuntimeEntry(
    DiscoveredPackage Package,
    PackageState State,
    IExusiAIPlugin? Instance,
    string? FailureCode,
    string? FailureMessage);

public sealed class ExtensionRuntime : IAsyncDisposable
{
    private static readonly string[] SharedAssemblies = ["ExusiAI.Extension.Abstractions", "ExusiAI.Extension.Wpf"];
    private readonly PackageDiscoveryService discovery;
    private readonly ILogger<ExtensionRuntime> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly List<RuntimeSlot> slots = [];
    private bool disposed;

    public ExtensionRuntime(PackageDiscoveryService discovery, ILogger<ExtensionRuntime> logger)
    {
        this.discovery = discovery;
        this.logger = logger;
    }

    public IReadOnlyList<ExtensionRuntimeEntry> Entries => slots.Select(x => x.Snapshot).ToImmutableArray();
    public ImmutableArray<PackageDiscoveryFailure> DiscoveryFailures { get; private set; } = [];
    public event EventHandler? EntriesChanged;

    public async Task DiscoverAsync(string packagesRoot, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slots.Any(x => x.Snapshot.State is PackageState.Loaded or PackageState.Initialized or PackageState.Running))
                throw new InvalidOperationException("Stop loaded extensions before running discovery again.");

            var result = await discovery.DiscoverAsync(packagesRoot, cancellationToken).ConfigureAwait(false);
            slots.Clear();
            slots.AddRange(result.Packages.Select(x => new RuntimeSlot(new(x, PackageState.Validated, null, null, null))));
            DiscoveryFailures = result.Failures;
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { gate.Release(); }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => StartAsync([], cancellationToken);

    public async Task StartAsync(IEnumerable<string> disabledPackageIds, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(disabledPackageIds);
        var disabled = disabledPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var index = 0; index < slots.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slot = slots[index];
                if (slot.Snapshot.Package.Manifest.Type != PackageType.Plugin || slot.Snapshot.State != PackageState.Validated) continue;
                if (disabled.Contains(slot.Snapshot.Package.Manifest.Id))
                {
                    slot.Snapshot = slot.Snapshot with { State = PackageState.Disabled };
                    EntriesChanged?.Invoke(this, EventArgs.Empty);
                    continue;
                }
                await TryStartOneAsync(slot, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { gate.Release(); }
    }

    public async Task SetEnabledAsync(string packageId, bool enabled, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var slot = slots.FirstOrDefault(x => string.Equals(x.Snapshot.Package.Manifest.Id, packageId, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Extension '{packageId}' was not discovered.");
            if (slot.Snapshot.Package.Manifest.Type != PackageType.Plugin)
                throw new InvalidOperationException("Only plugin packages can be enabled or disabled.");

            if (enabled)
            {
                if (slot.Snapshot.State == PackageState.Running) return;
                slot.Snapshot = slot.Snapshot with { State = PackageState.Validated, FailureCode = null, FailureMessage = null };
                EntriesChanged?.Invoke(this, EventArgs.Empty);
                await TryStartOneAsync(slot, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (slot.Snapshot.State == PackageState.Disabled) return;
                var unloadReference = await StopOneAsync(slot, PackageState.Disabled, cancellationToken).ConfigureAwait(false);
                CollectUnloadedContext(unloadReference);
            }
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var index = slots.Count - 1; index >= 0; index--)
            {
                var slot = slots[index];
                if (slot.Snapshot.State == PackageState.Disabled) continue;
                var unloadReference = await StopOneAsync(slot, PackageState.Stopped, cancellationToken).ConfigureAwait(false);
                CollectUnloadedContext(unloadReference);
            }
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        disposed = true;
        gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task StartOneAsync(RuntimeSlot slot, CancellationToken cancellationToken)
    {
        var package = slot.Snapshot.Package;
        var entryPoint = package.Manifest.EntryPoint ?? throw new InvalidOperationException("A plugin entry point is required.");
        if (!PackagePathResolver.TryResolve(package.RootPath, entryPoint.Assembly, out var assemblyPath) || !File.Exists(assemblyPath))
            throw new FileNotFoundException("The plugin assembly does not exist inside its package.", entryPoint.Assembly);

        slot.LoadContext = new(assemblyPath, SharedAssemblies);
        var assembly = slot.LoadContext.LoadFromAssemblyPath(assemblyPath);
        var type = assembly.GetType(entryPoint.Type, throwOnError: true, ignoreCase: false)
            ?? throw new TypeLoadException($"Entry point '{entryPoint.Type}' was not found.");
        if (type.IsAbstract || !typeof(IExusiAIPlugin).IsAssignableFrom(type))
            throw new InvalidOperationException("The entry point must be a concrete IExusiAIPlugin implementation.");

        var plugin = Activator.CreateInstance(type) as IExusiAIPlugin
            ?? throw new InvalidOperationException("The entry point requires a public parameterless constructor.");
        slot.Snapshot = slot.Snapshot with { State = PackageState.Loaded, Instance = plugin };
        EntriesChanged?.Invoke(this, EventArgs.Empty);
        await plugin.InitializeAsync(new ExtensionContext(package.Manifest.Id, new ExtensionLogger(logger, package.Manifest.Id)), cancellationToken).ConfigureAwait(false);
        slot.Snapshot = slot.Snapshot with { State = PackageState.Initialized };
        EntriesChanged?.Invoke(this, EventArgs.Empty);
        await plugin.StartAsync(cancellationToken).ConfigureAwait(false);
        slot.Snapshot = slot.Snapshot with { State = PackageState.Running };
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task TryStartOneAsync(RuntimeSlot slot, CancellationToken cancellationToken)
    {
        try
        {
            await StartOneAsync(slot, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Extension {PackageId} failed during startup.", slot.Snapshot.Package.Manifest.Id);
            await CleanupFailedSlotAsync(slot).ConfigureAwait(false);
            slot.Snapshot = slot.Snapshot with
            {
                State = PackageState.Failed,
                Instance = null,
                FailureCode = "plugin-start-failed",
                FailureMessage = exception.Message
            };
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<WeakReference?> StopOneAsync(RuntimeSlot slot, PackageState finalState, CancellationToken cancellationToken)
    {
        if (slot.Snapshot.Instance is null)
        {
            slot.Snapshot = slot.Snapshot with { State = finalState, FailureCode = null, FailureMessage = null };
            EntriesChanged?.Invoke(this, EventArgs.Empty);
            return UnloadContext(slot);
        }

        WeakReference? unloadReference = null;
        slot.Snapshot = slot.Snapshot with { State = PackageState.Stopping };
        EntriesChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await slot.Snapshot.Instance.StopAsync(cancellationToken).ConfigureAwait(false);
            await DisposeInstanceAsync(slot.Snapshot.Instance).ConfigureAwait(false);
            slot.Snapshot = slot.Snapshot with { State = finalState, Instance = null, FailureCode = null, FailureMessage = null };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "Extension {PackageId} failed during shutdown.", slot.Snapshot.Package.Manifest.Id);
            slot.Snapshot = slot.Snapshot with { State = PackageState.Failed, Instance = null, FailureCode = "plugin-stop-failed", FailureMessage = exception.Message };
        }
        finally
        {
            unloadReference = UnloadContext(slot);
            EntriesChanged?.Invoke(this, EventArgs.Empty);
        }
        return unloadReference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference? UnloadContext(RuntimeSlot slot)
    {
        var context = slot.LoadContext;
        slot.LoadContext = null;
        if (context is null) return null;
        context.Unload();
        return new WeakReference(context);
    }

    private static void CollectUnloadedContext(WeakReference? reference)
    {
        for (var attempt = 0; reference is { IsAlive: true } && attempt < 5; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static async Task CleanupFailedSlotAsync(RuntimeSlot slot)
    {
        if (slot.Snapshot.Instance is not null)
        {
            try { await slot.Snapshot.Instance.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await DisposeInstanceAsync(slot.Snapshot.Instance).ConfigureAwait(false); } catch { }
        }
        slot.LoadContext?.Unload();
        slot.LoadContext = null;
    }

    private static async ValueTask DisposeInstanceAsync(IExusiAIPlugin plugin)
    {
        if (plugin is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (plugin is IDisposable disposable) disposable.Dispose();
    }

    private sealed class RuntimeSlot(ExtensionRuntimeEntry snapshot)
    {
        public ExtensionRuntimeEntry Snapshot { get; set; } = snapshot;
        public PackageLoadContext? LoadContext { get; set; }
    }

    private sealed record ExtensionContext(string PackageId, IExtensionLogger Logger) : IExtensionContext;

    private sealed class ExtensionLogger(ILogger logger, string packageId) : IExtensionLogger
    {
        public void Information(string message) => logger.LogInformation("[{PackageId}] {Message}", packageId, message);
        public void Warning(string message) => logger.LogWarning("[{PackageId}] {Message}", packageId, message);
        public void Error(string message, Exception? exception = null) => logger.LogError(exception, "[{PackageId}] {Message}", packageId, message);
    }
}
