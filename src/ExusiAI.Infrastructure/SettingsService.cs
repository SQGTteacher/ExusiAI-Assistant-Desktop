using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Infrastructure;

public sealed record ApplicationSettings(
    int SchemaVersion = 1,
    string Theme = "system",
    string Backdrop = "mica",
    string[]? DisabledPackages = null,
    string[]? RemovedBundledPackages = null);

public interface ISettingsService
{
    ApplicationSettings Current { get; }
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
    string GetExtensionSettingsPath(string packageId);
}

public sealed class SettingsService(IAppPaths paths, ILogger<SettingsService> logger) : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private string SettingsPath => Path.Combine(paths.SettingsDirectory, "settings.json");
    public ApplicationSettings Current { get; private set; } = new();

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(SettingsPath)) return Current = new();
            await using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var settings = await JsonSerializer.DeserializeAsync<ApplicationSettings>(stream, Options, cancellationToken).ConfigureAwait(false);
            return Current = settings is { SchemaVersion: 1 } ? settings : new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(exception, "Settings could not be loaded; defaults are active.");
            return Current = new();
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(paths.SettingsDirectory);
            var temporaryPath = SettingsPath + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, SettingsPath, true);
            Current = settings;
        }
        finally { gate.Release(); }
    }

    public string GetExtensionSettingsPath(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId) || packageId.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '.' or '-')))
            throw new ArgumentException("Package id is not path-safe.", nameof(packageId));
        return Path.Combine(paths.SettingsDirectory, "extensions", packageId + ".json");
    }
}
