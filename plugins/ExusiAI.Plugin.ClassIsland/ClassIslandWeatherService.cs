using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.ClassIsland;

/// <summary>Weather data is independent of the island window and survives component layout changes.</summary>
public sealed class ClassIslandWeatherService : IDisposable
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly string settingsPath;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private CancellationTokenSource? lifetime;
    private PeriodicTimer? timer;
    private Task? refreshLoop;

    public ClassIslandWeatherService(string dataDirectory) => settingsPath = Path.Combine(dataDirectory, "Settings.json");
    public JsonElement? Current { get; private set; }
    public string CityId { get; private set; } = "";
    public string Summary
    {
        get
        {
            if (Current is not { } info || !TryProperty(info, "current", out var current) ||
                !TryProperty(current, "temperature", out var temperature) ||
                !TryProperty(temperature, "value", out var value)) return "尚无天气数据。请设置城市编号后刷新。";
            TryProperty(temperature, "unit", out var unit);
            var summary = $"城市编号：{CityId}　气温：{value}{unit}";
            if (TryProperty(info, "alerts", out var alerts) && alerts.ValueKind == JsonValueKind.Array)
                summary += $"　气象预警：{alerts.GetArrayLength()} 条";
            return summary;
        }
    }
    public event EventHandler? Changed;

    public void LoadCached()
    {
        if (!File.Exists(settingsPath)) return;
        try
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (TryProperty(settings.RootElement, "CityId", out var city) && city.ValueKind == JsonValueKind.String) CityId = city.GetString() ?? "";
            if (TryProperty(settings.RootElement, "LastWeatherInfo", out var weather))
                Current = weather.Clone();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is JsonException or IOException)
        {
            // An imported settings file can be corrupt; the rest of the plugin must still start.
        }
    }

    public async Task SetCityAsync(string cityId, CancellationToken cancellationToken = default)
    {
        var root = File.Exists(settingsPath)
            ? JsonNode.Parse(await File.ReadAllTextAsync(settingsPath, cancellationToken).ConfigureAwait(false)) as JsonObject
            : new JsonObject();
        if (root is null) throw new InvalidDataException("ClassIsland Settings.json 根节点无效。");
        root["CityId"] = cityId.Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporary = settingsPath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, settingsPath, true);
            CityId = cityId.Trim();
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Start()
    {
        if (refreshLoop is not null) return;
        lifetime = new CancellationTokenSource();
        timer = new PeriodicTimer(TimeSpan.FromMinutes(20));
        refreshLoop = RefreshLoopAsync(lifetime.Token);
    }

    private async Task RefreshLoopAsync(CancellationToken token)
    {
        try
        {
            await RefreshAsync(token).ConfigureAwait(false);
            while (timer is not null && await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                await RefreshAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public async Task StopAsync()
    {
        if (lifetime is null) return;
        await lifetime.CancelAsync().ConfigureAwait(false);
        timer?.Dispose();
        if (refreshLoop is not null) await refreshLoop.ConfigureAwait(false);
        lifetime.Dispose(); lifetime = null; timer = null; refreshLoop = null;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath)) return;
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath, cancellationToken).ConfigureAwait(false));
            if (!TryProperty(settings.RootElement, "CityId", out var city) || city.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(city.GetString())) return;
            var cityId = city.GetString()!;
            // Retain ClassIsland's city identifier lookup, then use a public forecast
            // endpoint without distributing the upstream provider's embedded signature.
            using var locationResponse = await Client.GetAsync($"https://weatherapi.market.xiaomi.com/wtr-v3/location/city/info?locationKey={Uri.EscapeDataString(cityId)}&locale=zh_cn", cancellationToken).ConfigureAwait(false);
            locationResponse.EnsureSuccessStatusCode();
            using var locations = JsonDocument.Parse(await locationResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (locations.RootElement.ValueKind != JsonValueKind.Array || locations.RootElement.GetArrayLength() == 0) return;
            var location = locations.RootElement[0];
            if (!TryProperty(location, "latitude", out var latitude) || !TryProperty(location, "longitude", out var longitude)) return;
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={Uri.EscapeDataString(latitude.ToString())}&longitude={Uri.EscapeDataString(longitude.ToString())}&current=temperature_2m,relative_humidity_2m,apparent_temperature,pressure_msl,wind_speed_10m&timezone=auto";
            using var response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var weather = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (!TryProperty(weather.RootElement, "current", out var reading)) return;
            var converted = new
            {
                current = new
                {
                    temperature = Pair(reading, "temperature_2m", "°C"),
                    humidity = Pair(reading, "relative_humidity_2m", "%"),
                    feelsLike = Pair(reading, "apparent_temperature", "°C"),
                    pressure = Pair(reading, "pressure_msl", "hPa"),
                    wind = new { speed = Pair(reading, "wind_speed_10m", "km/h") }
                },
                alerts = Array.Empty<object>()
            };
            Current = JsonSerializer.SerializeToElement(converted);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            // Keep the last valid forecast if the provider or network is unavailable.
        }
        finally { refreshGate.Release(); }
    }

    private static object Pair(JsonElement reading, string field, string unit) => new
    {
        value = TryProperty(reading, field, out var value) ? value.ToString() : "",
        unit
    };

    public static bool TryProperty(JsonElement value, string name, out JsonElement result)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { result = property.Value; return true; }
        result = default;
        return false;
    }

    public void Dispose()
    {
        lifetime?.Cancel(); timer?.Dispose(); lifetime?.Dispose();
    }
}
