using System.IO;
using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Plugin.ClassIsland;

public sealed class ClassIslandCoreService : IAsyncDisposable
{
    private readonly IExtensionLogger logger;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private CancellationTokenSource? lifetime;

    public ClassIslandCoreService(IExtensionLogger logger, string? dataDirectory = null)
    {
        this.logger = logger;
        DataDirectory = Path.GetFullPath(dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExusiAI", "ClassIsland"));
        Profiles = new(Path.Combine(DataDirectory, "Profiles"));
        Timetable = new(Profiles);
        Components = new(Path.Combine(DataDirectory, "Config", "Components"));
        Notifications = new();
    }

    public string DataDirectory { get; }
    public ClassIslandProfileService Profiles { get; }
    public ClassIslandTimetableService Timetable { get; }
    public ClassIslandComponentService Components { get; }
    public ClassIslandNotificationService Notifications { get; }
    public bool IsRunning => lifetime is { IsCancellationRequested: false };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DataDirectory);
            await Profiles.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await Components.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var profiles = await Profiles.ListAsync(cancellationToken).ConfigureAwait(false);
            if (profiles.Count > 0) await Profiles.LoadAsync(profiles[0], cancellationToken).ConfigureAwait(false);
            logger.Information($"ClassIsland core initialized at '{DataDirectory}' with {profiles.Count} profile(s).");
        }
        finally { lifecycleGate.Release(); }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning) return;
            lifetime?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            lifetime = new CancellationTokenSource();
            logger.Information("ClassIsland core services started under the ExusiAI plugin lifecycle.");
        }
        finally { lifecycleGate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (lifetime is null) return;
            await lifetime.CancelAsync().ConfigureAwait(false);
            lifetime.Dispose();
            lifetime = null;
            logger.Information("ClassIsland core services stopped.");
        }
        finally { lifecycleGate.Release(); }
    }

    public async Task<ClassIslandBackupSummary> ImportBackupAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        var result = await ClassIslandBackupImporter.ImportIntoDataDirectoryAsync(archivePath, DataDirectory, cancellationToken)
            .ConfigureAwait(false);
        await Components.ReloadAsync(cancellationToken).ConfigureAwait(false);
        var profilePaths = await Profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        if (profilePaths.Count > 0) await Profiles.LoadAsync(profilePaths[0], cancellationToken).ConfigureAwait(false);
        Notifications.Publish(ClassIslandNotificationKind.Information, "配置同步完成", $"已导入 {result.TotalFileCount} 个 ClassIsland 文件。");
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lifecycleGate.Dispose();
    }
}
