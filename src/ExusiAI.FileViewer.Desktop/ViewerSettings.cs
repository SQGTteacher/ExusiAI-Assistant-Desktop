using System.IO;
using System.Text.Json;

namespace ExusiAI.FileViewer.Desktop;

internal sealed record ViewerSettings(bool StartMaximized = false, bool RememberRecentFiles = true, bool PreferDarkTheme = true, int DefaultZoomPercent = 100)
{
    public static async Task<ViewerSettings> LoadAsync()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExusiAI", "FileViewer", "settings.json");
        try
        {
            if (!File.Exists(path)) return new();
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ViewerSettings>(stream) ?? new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
}
