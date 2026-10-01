using System.IO;
using System.Text.Json;

namespace ExusiAI.Plugin.ClassIsland;

internal sealed class ClassIslandSelectionSettings
{
    public string? SelectedProfile { get; private init; }
    public string? CurrentComponentConfig { get; private init; }

    public static async Task<ClassIslandSelectionSettings> ReadAsync(string dataDirectory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(dataDirectory, "Settings.json");
        if (!File.Exists(path)) return new();
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("ClassIsland Settings.json 根节点无效。");
        return new()
        {
            SelectedProfile = ReadString(document.RootElement, "SelectedProfile"),
            CurrentComponentConfig = ReadString(document.RootElement, "CurrentComponentConfig")
        };
    }

    public string? ResolveProfile(IReadOnlyList<string> paths)
    {
        var selected = Path.GetFileName(SelectedProfile);
        return paths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(selected) &&
            Path.GetFileName(path).Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? paths.FirstOrDefault();
    }

    private static string? ReadString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
