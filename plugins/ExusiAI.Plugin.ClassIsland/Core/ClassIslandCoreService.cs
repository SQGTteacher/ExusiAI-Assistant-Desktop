using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Plugin.ClassIsland.Core;

/// <summary>
/// Native ClassIsland capability boundary inside ExusiAI.
///
/// This service is intentionally separated from the legacy runtime launcher so
/// migrated ClassIsland features can run in the ExusiAI extension lifecycle.
/// </summary>
internal sealed class ClassIslandCoreService
{
    private readonly IExtensionLogger logger;

    public ClassIslandCoreService(IExtensionLogger logger)
    {
        this.logger = logger;
    }

    public bool IsInitialized { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsInitialized = true;
        logger.Information("ClassIsland native core service initialized.");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsInitialized = false;
        logger.Information("ClassIsland native core service stopped.");
        return Task.CompletedTask;
    }
}
