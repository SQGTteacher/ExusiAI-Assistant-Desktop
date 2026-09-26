using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed record ClassIslandWeatherSnapshot(
    string Condition,
    string Temperature,
    string FeelsLike,
    string Humidity,
    string Pressure,
    string Wind,
    string Aqi,
    int AlertCount,
    DateTimeOffset? UpdatedAt,
    bool IsStale)
{
    public bool HasData =>
        !string.IsNullOrWhiteSpace(Temperature) ||
        !string.IsNullOrWhiteSpace(Condition) ||
        !string.IsNullOrWhiteSpace(Humidity);
}

internal static class ClassIslandWeatherCache
{
    private static readonly IReadOnlyDictionary<string, string> Conditions = new Dictionary<string, string>
    {
        ["0"] = "晴", ["1"] = "多云", ["2"] = "阴", ["3"] = "阵雨", ["4"] = "雷阵雨",
        ["5"] = "雷阵雨伴冰雹", ["6"] = "雨夹雪", ["7"] = "小雨", ["8"] = "中雨", ["9"] = "大雨",
        ["10"] = "暴雨", ["11"] = "大暴雨", ["12"] = "特大暴雨", ["13"] = "阵雪", ["14"] = "小雪",
        ["15"] = "中雪", ["16"] = "大雪", ["17"] = "暴雪", ["18"] = "雾", ["19"] = "冻雨",
        ["20"] = "沙尘暴", ["29"] = "浮尘", ["30"] = "扬沙", ["31"] = "强沙尘暴", ["35"] = "轻雾",
        ["53"] = "霾", ["301"] = "雨", ["302"] = "雪", ["99"] = "未知"
    };

    public static ClassIslandWeatherSnapshot Parse(JsonNode? node, DateTimeOffset now, TimeSpan staleAfter)
    {
        var current = Property(node, "current");
        var code = Text(Property(current, "weather"));
        var updateMilliseconds = Long(Property(node, "updateTime"));
        DateTimeOffset? updatedAt = null;
        if (updateMilliseconds > 0)
        {
            try { updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(updateMilliseconds); }
            catch (ArgumentOutOfRangeException) { }
        }

        return new(
            Conditions.GetValueOrDefault(code, string.IsNullOrWhiteSpace(code) ? "" : code),
            Pair(Property(current, "temperature")),
            Pair(Property(current, "feelsLike")),
            Pair(Property(current, "humidity")),
            Pair(Property(current, "pressure")),
            Wind(Property(current, "wind")),
            Text(Property(Property(node, "aqi"), "aqi")),
            Property(node, "alerts") is JsonArray alerts ? alerts.Count : 0,
            updatedAt,
            updatedAt is not null && now - updatedAt > staleAfter);
    }

    public static string MainText(ClassIslandWeatherSnapshot snapshot, int kind) => kind switch
    {
        1 => string.IsNullOrWhiteSpace(snapshot.Humidity) ? "湿度暂无" : $"湿度 {snapshot.Humidity}",
        2 => string.IsNullOrWhiteSpace(snapshot.Wind) ? "风力暂无" : snapshot.Wind,
        3 => string.IsNullOrWhiteSpace(snapshot.Aqi) ? "AQI 暂无" : $"AQI {snapshot.Aqi}",
        4 => string.IsNullOrWhiteSpace(snapshot.Pressure) ? "气压暂无" : $"气压 {snapshot.Pressure}",
        5 => string.IsNullOrWhiteSpace(snapshot.FeelsLike) ? "体感暂无" : $"体感 {snapshot.FeelsLike}",
        _ => string.Join(" ", new[] { snapshot.Condition, snapshot.Temperature }.Where(x => !string.IsNullOrWhiteSpace(x)))
    };

    private static JsonNode? Property(JsonNode? node, string name)
    {
        if (node is not JsonObject owner) return null;
        foreach (var pair in owner)
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        return null;
    }

    private static string Pair(JsonNode? node)
    {
        var value = Text(Property(node, "value"));
        var unit = Text(Property(node, "unit"));
        return value + unit;
    }

    private static string Wind(JsonNode? node)
    {
        var speed = Pair(Property(node, "speed"));
        var direction = Text(Property(node, "direction"));
        return string.Join(" ", new[] { direction, speed }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string Text(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text ?? "",
        JsonValue value when value.TryGetValue<double>(out var number) => number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        _ => ""
    };

    private static long Long(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<long>(out var number) ? number : 0;
}
