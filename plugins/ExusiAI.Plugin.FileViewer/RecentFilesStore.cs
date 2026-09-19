using System.IO;
using System.Text.Json;

namespace ExusiAI.Plugin.FileViewer;

internal sealed record RecentFileEntry(string Path, string DisplayName, DateTimeOffset LastOpenedUtc);

internal sealed class RecentFilesStore
{
    private const int MaximumEntries = 12;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExusiAI",
        "FileViewer",
        "recent.json");

    public async Task<IReadOnlyList<RecentFileEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await LoadCoreAsync(cancellationToken);
            var filtered = entries
                .Where(entry => File.Exists(entry.Path))
                .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(entry => entry.LastOpenedUtc)
                .Take(MaximumEntries)
                .ToArray();

            if (filtered.Length != entries.Count)
                await SaveCoreAsync(filtered, cancellationToken);

            return filtered;
        }
        finally { gate.Release(); }
    }

    public async Task AddAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await LoadCoreAsync(cancellationToken);
            var updated = entries
                .Where(entry => !string.Equals(entry.Path, fullPath, StringComparison.OrdinalIgnoreCase) && File.Exists(entry.Path))
                .Prepend(new RecentFileEntry(fullPath, Path.GetFileName(fullPath), DateTimeOffset.UtcNow))
                .Take(MaximumEntries)
                .ToArray();
            await SaveCoreAsync(updated, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task RemoveAsync(string path, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await LoadCoreAsync(cancellationToken);
            await SaveCoreAsync(
                entries.Where(entry => !string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)).ToArray(),
                cancellationToken);
        }
        finally { gate.Release(); }
    }

    private async Task<List<RecentFileEntry>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(filePath)) return [];
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<List<RecentFileEntry>>(stream, cancellationToken: cancellationToken) ?? [];
        }
        catch (JsonException) { return []; }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private async Task SaveCoreAsync(IEnumerable<RecentFileEntry> entries, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = filePath + ".tmp";

            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, entries, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, filePath, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
