using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.ClassIsland;

/// <summary>Small native adapter for the upstream Settings.json data contract.</summary>
public sealed class ClassIslandSettingsService
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    public ClassIslandSettingsService(string dataDirectory) => path = Path.Combine(dataDirectory, "Settings.json");
    public DateOnly? SingleWeekStartTime { get; private set; }
    public double TimeOffsetSeconds { get; private set; }
    public string ExactTimeServer { get; private set; } = "ntp.aliyun.com";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return;
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        if (ClassIslandWeatherService.TryProperty(root, "SingleWeekStartTime", out var start) &&
            start.ValueKind == JsonValueKind.String && DateTime.TryParse(start.GetString(), out var date))
            SingleWeekStartTime = DateOnly.FromDateTime(date);
        if (ClassIslandWeatherService.TryProperty(root, "TimeOffsetSeconds", out var offset) && offset.ValueKind == JsonValueKind.Number && offset.TryGetDouble(out var seconds))
            TimeOffsetSeconds = seconds;
        if (ClassIslandWeatherService.TryProperty(root, "ExactTimeServer", out var server) && server.ValueKind == JsonValueKind.String)
            ExactTimeServer = server.GetString() ?? ExactTimeServer;
    }

    public async Task SaveGeneralAsync(DateOnly anchor, double offsetSeconds, string timeServer, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(offsetSeconds) || Math.Abs(offsetSeconds) > 86400)
            throw new ArgumentOutOfRangeException(nameof(offsetSeconds));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = File.Exists(path)
                ? JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)) as JsonObject
                : new JsonObject();
            if (root is null) throw new InvalidDataException("ClassIsland Settings.json 根节点无效。");
            root["SingleWeekStartTime"] = anchor.ToDateTime(TimeOnly.MinValue).ToString("O");
            root["TimeOffsetSeconds"] = offsetSeconds;
            root["ExactTimeServer"] = timeServer.Trim();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            SingleWeekStartTime = anchor;
            TimeOffsetSeconds = offsetSeconds;
            ExactTimeServer = timeServer.Trim();
        }
        finally { gate.Release(); }
    }
}
