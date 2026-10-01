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
        Settings = new(DataDirectory);
        Profiles = new(Path.Combine(DataDirectory, "Profiles"));
        Timetable = new(Profiles);
        Components = new(Path.Combine(DataDirectory, "Config", "ComponentLayouts"));
        Notifications = new();
        Appearance = new(DataDirectory);
        Weather = new(DataDirectory);
        Presentation = new(Timetable, Components, Appearance, Notifications, Weather, Settings);
    }

    public string DataDirectory { get; }
    public ClassIslandSettingsService Settings { get; }
    public ClassIslandProfileService Profiles { get; }
    public ClassIslandTimetableService Timetable { get; }
    public ClassIslandComponentService Components { get; }
    public ClassIslandNotificationService Notifications { get; }
    public ClassIslandAppearanceService Appearance { get; }
    public ClassIslandWeatherService Weather { get; }
    public ClassIslandPresentationService Presentation { get; }
    public bool IsRunning => lifetime is { IsCancellationRequested: false };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DataDirectory);
            await Settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            Timetable.RotationAnchor = Settings.SingleWeekStartTime;
            var selection = await ClassIslandSelectionSettings.ReadAsync(DataDirectory, cancellationToken).ConfigureAwait(false);
            await Profiles.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await Components.InitializeAsync(selection.CurrentComponentConfig, cancellationToken).ConfigureAwait(false);
            await Appearance.LoadAsync(cancellationToken).ConfigureAwait(false);
            Weather.LoadCached();
            var profiles = await Profiles.ListAsync(cancellationToken).ConfigureAwait(false);
            if (selection.ResolveProfile(profiles) is { } selectedProfile)
                await Profiles.LoadAsync(selectedProfile, cancellationToken).ConfigureAwait(false);
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
            Notifications.Start(cancellationToken);
            Weather.Start();
            Presentation.Start();
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
            await Notifications.StopAsync().ConfigureAwait(false);
            await Weather.StopAsync().ConfigureAwait(false);
            Presentation.Stop();
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
        var selection = await ClassIslandSelectionSettings.ReadAsync(DataDirectory, cancellationToken).ConfigureAwait(false);
        await Settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Timetable.RotationAnchor = Settings.SingleWeekStartTime;
        await Components.InitializeAsync(selection.CurrentComponentConfig, cancellationToken).ConfigureAwait(false);
        Weather.LoadCached();
        var profilePaths = await Profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        if (selection.ResolveProfile(profilePaths) is { } selectedProfile)
            await Profiles.LoadAsync(selectedProfile, cancellationToken).ConfigureAwait(false);
        Notifications.Publish(ClassIslandNotificationKind.Information, "配置同步完成", $"已导入 {result.TotalFileCount} 个 ClassIsland 文件。");
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        Weather.Dispose();
        lifecycleGate.Dispose();
    }
}
