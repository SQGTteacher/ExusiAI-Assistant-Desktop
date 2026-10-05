using System.Text.Json;
using System.Globalization;

namespace ExusiAI.Plugin.ClassIsland;

public static class ClassIslandComponentText
{
    public static IReadOnlyList<ClassIslandComponentSettings> Children(ClassIslandComponentSettings component)
    {
        if (component.Settings is not { ValueKind: JsonValueKind.Object } settings ||
            !ClassIslandWeatherService.TryProperty(settings, "Children", out var children) || children.ValueKind != JsonValueKind.Array) return [];
        var nested = new List<ClassIslandComponentSettings>();
        foreach (var child in children.EnumerateArray())
        {
            try
            {
                if (JsonSerializer.Deserialize<ClassIslandComponentSettings>(child.GetRawText()) is { IsVisible: true } parsed)
                    nested.Add(parsed);
            }
            catch (JsonException) { /* A damaged child must not hide other components. */ }
        }
        return nested;
    }

    public static string? Resolve(ClassIslandComponentSettings component, ClassIslandTimetableService timetable, DateTime now, JsonElement? weather = null, int depth = 0)
    {
        if (depth > 8) return null;
        if (!Guid.TryParse(component.Id, out var id)) return null;
        if (id == new Guid("DF3F8295-21F6-482E-BADA-FA0E5F14BB66"))
            return now.ToString("ddd MM/dd", CultureInfo.CurrentCulture);
        if (id == new Guid("9E1AF71D-8F77-4B21-A342-448787104DD9"))
        {
            var seconds = ReadBool(component.Settings, "ShowSeconds");
            var separator = !seconds && ReadBool(component.Settings, "FlashTimeSeparator", true) && now.Second % 2 == 0 ? " " : ":";
            return now.ToString("HH", CultureInfo.CurrentCulture) + separator + now.ToString(seconds ? "mm:ss" : "mm", CultureInfo.CurrentCulture);
        }
        if (id == new Guid("1DB2017D-E374-4BC6-9D57-0B4ADF03A6B8"))
        {
            var lessons = timetable.GetLessons(now);
            var current = lessons.FirstOrDefault(x => now.TimeOfDay >= x.Time.StartTime && now.TimeOfDay < x.Time.EndTime);
            if (current is not null) return current.Subject.Name;
            var next = lessons.FirstOrDefault(x => x.Time.StartTime > now.TimeOfDay);
            return next is null ? "今天没有课程。" : $"接下来 · {next.Subject.Name}";
        }
        if (id == new Guid("EE8F66BD-C423-4E7C-AB46-AA9976B00E08"))
            return ReadString(component.Settings, "TextContent") ?? "";
        if (id == new Guid("CA495086-E297-4BEB-9603-C5C1C1A8551E"))
            return ResolveWeather(component.Settings, weather);
        if (id == new Guid("AB0F26D5-9DF6-4575-B844-73B04D0907C1")) return "│";
        if (id == new Guid("C911D762-107F-40C6-84CC-0146AB3C86B1") ||
            id == new Guid("70FCD5EA-3FAE-4E06-ACA2-4F4DF47F9ACD") ||
            id == new Guid("2D849ECE-9F21-4C78-9434-415CFC283294") ||
            id == new Guid("7E19A113-D281-4F33-970A-834A0B78B5AD"))
        {
            var nested = Children(component);
            if (nested.Count == 0) return null;
            if (id == new Guid("7E19A113-D281-4F33-970A-834A0B78B5AD"))
            {
                var seconds = Math.Max(1, ReadInt(component.Settings, "SlideSeconds", 15));
                return Resolve(nested[(int)((now.Ticks / TimeSpan.TicksPerSecond / seconds) % nested.Count)], timetable, now, weather, depth + 1);
            }
            return string.Join(id == new Guid("2D849ECE-9F21-4C78-9434-415CFC283294") ? " / " : "  ",
                nested.Select(x => Resolve(x, timetable, now, weather, depth + 1)).Where(x => !string.IsNullOrEmpty(x)));
        }
        if (id == new Guid("7C645D35-8151-48BA-B4AC-15017460D994"))
        {
            if (!TryGetCountdownWindow(component.Settings, now, out var start, out var end)) return null;
            var remaining = end - now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            var total = end - start;
            var format = ReadString(component.Settings, "CustomStringFormat") ?? "%D天";
            var value = format.Replace("%D", Math.Ceiling(remaining.TotalDays).ToString(CultureInfo.InvariantCulture))
                .Replace("%H", Math.Ceiling(remaining.TotalHours).ToString(CultureInfo.InvariantCulture))
                .Replace("%M", Math.Ceiling(remaining.TotalMinutes).ToString(CultureInfo.InvariantCulture))
                .Replace("%S", Math.Ceiling(remaining.TotalSeconds).ToString(CultureInfo.InvariantCulture))
                .Replace("%X", Math.Ceiling(remaining.TotalMilliseconds).ToString(CultureInfo.InvariantCulture))
                .Replace("%P", (total <= TimeSpan.Zero ? 0 : Math.Round((total - remaining) / total, 2)).ToString("P0", CultureInfo.InvariantCulture))
                .Replace("%p", (total <= TimeSpan.Zero ? 0 : (total - remaining) / total).ToString("P2", CultureInfo.InvariantCulture))
                .Replace("%L", (total <= TimeSpan.Zero ? 0 : Math.Round(remaining / total, 2)).ToString("P0", CultureInfo.InvariantCulture))
                .Replace("%d", remaining.Days.ToString(CultureInfo.InvariantCulture))
                .Replace("%h", remaining.Hours.ToString(CultureInfo.InvariantCulture))
                .Replace("%m", remaining.Minutes.ToString("00", CultureInfo.InvariantCulture))
                .Replace("%s", remaining.Seconds.ToString("00", CultureInfo.InvariantCulture))
                .Replace("%x", remaining.Milliseconds.ToString("000", CultureInfo.InvariantCulture));
            var name = ReadString(component.Settings, "CountDownName") ?? "倒计时";
            return ReadBool(component.Settings, "IsCompactModeEnabled") ? $"{name} {value}" : $"距离 {name} {ReadString(component.Settings, "CountDownConnector") ?? "还有"} {value}";
        }
        return null;
    }

    public static bool TryGetCountdownWindow(JsonElement? settings, DateTime now, out DateTime start, out DateTime end)
    {
        start = default;
        end = default;
        switch (ReadInt(settings, "CountdownSource"))
        {
            case 0:
                if (!TryReadDate(settings, "OverTime", out end)) return false;
                TryReadDate(settings, "StartTime", out start);
                break;
            case 1:
                if (!TryReadDate(settings, "CycleStartTime", out var cycleStart)) return false;
                var duration = ReadDuration(settings, "CycleDuration", TimeSpan.FromDays(1));
                var before = ReadBool(settings, "IsAdvancedCycleTimingEnabled") ? ReadDuration(settings, "CycleBeforeDuration", TimeSpan.Zero) : TimeSpan.Zero;
                var after = ReadBool(settings, "IsAdvancedCycleTimingEnabled") ? ReadDuration(settings, "CycleAfterDuration", TimeSpan.Zero) : TimeSpan.Zero;
                var cycle = before + duration + after;
                if (cycle <= TimeSpan.Zero) { start = cycleStart; end = cycleStart; break; }
                var cycles = Math.Floor((now - cycleStart).Ticks / (double)cycle.Ticks);
                if (ReadBool(settings, "IsCycleCountLimited")) cycles = Math.Min(cycles, ReadInt(settings, "CycleCountLimit", 2));
                // Keep the calculation bounded for malformed or very old imported profiles.
                cycles = Math.Clamp(cycles, -100000, 100000);
                start = cycleStart + TimeSpan.FromTicks((long)(cycles * cycle.Ticks)) + before;
                end = start + duration;
                break;
            case 2:
                start = now.Date; end = start.AddDays(1); break;
            case 3:
                var weekStart = ReadInt(settings, "WeekCountdownStartDay", 1);
                if (weekStart is < 0 or > 6) weekStart = 1;
                start = now.Date.AddDays(-(((int)now.DayOfWeek - weekStart + 7) % 7));
                end = now.Date.AddDays(8); break;
            default: return false;
        }
        return true;
    }

    public static bool TryParseUpstreamColor(string? hex, out byte alpha, out byte red, out byte green, out byte blue)
    {
        alpha = 255;
        red = green = blue = 0;
        if (hex is null || (hex.Length != 9 && hex.Length != 7) || hex[0] != '#') return false;
        var style = NumberStyles.HexNumber;
        var culture = CultureInfo.InvariantCulture;
        if (!byte.TryParse(hex.AsSpan(1, 2), style, culture, out red) ||
            !byte.TryParse(hex.AsSpan(3, 2), style, culture, out green) ||
            !byte.TryParse(hex.AsSpan(5, 2), style, culture, out blue)) return false;
        return hex.Length == 7 || byte.TryParse(hex.AsSpan(7, 2), style, culture, out alpha);
    }

    private static string? ResolveWeather(JsonElement? settings, JsonElement? weather)
    {
        if (weather is not { ValueKind: JsonValueKind.Object } info ||
            !ClassIslandWeatherService.TryProperty(info, "current", out var current)) return null;
        var kind = ReadInt(settings, "MainWeatherInfoKind");
        var field = kind switch { 1 => "humidity", 2 => "wind", 4 => "pressure", 5 => "feelsLike", _ => "temperature" };
        JsonElement pair;
        if (kind == 3)
        {
            if (!ClassIslandWeatherService.TryProperty(info, "aqi", out var aqi) ||
                !ClassIslandWeatherService.TryProperty(aqi, "aqi", out pair)) return null;
            return $"AQI {pair}";
        }
        if (!ClassIslandWeatherService.TryProperty(current, field, out pair)) return null;
        if (kind == 2 && !ClassIslandWeatherService.TryProperty(pair, "speed", out pair)) return null;
        if (!ClassIslandWeatherService.TryProperty(pair, "value", out var value)) return null;
        if (!ReadBool(settings, "ShowMainWeatherInfo", true) || string.IsNullOrWhiteSpace(value.ToString())) return null;
        ClassIslandWeatherService.TryProperty(pair, "unit", out var unit);
        var result = value.ToString() + (unit.ValueKind == JsonValueKind.String ? unit.GetString() : "");
        if (ReadBool(settings, "ShowAlerts", true) && ClassIslandWeatherService.TryProperty(info, "alerts", out var alerts) &&
            alerts.ValueKind == JsonValueKind.Array && alerts.GetArrayLength() > 0 &&
            ClassIslandWeatherService.TryProperty(alerts[0], "title", out var title)) result += "  " + title.ToString();
        return result;
    }

    private static bool ReadBool(JsonElement? settings, string key, bool defaultValue = false) =>
        settings is { ValueKind: JsonValueKind.Object } && settings.Value.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : defaultValue;

    private static int ReadInt(JsonElement? settings, string key, int fallback = 0) =>
        settings is { ValueKind: JsonValueKind.Object } && settings.Value.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : fallback;

    private static TimeSpan ReadDuration(JsonElement? settings, string key, TimeSpan fallback) =>
        TimeSpan.TryParse(ReadString(settings, key), CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string? ReadString(JsonElement? settings, string key) =>
        settings is { ValueKind: JsonValueKind.Object } && settings.Value.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool TryReadDate(JsonElement? settings, string key, out DateTime date)
    {
        date = default;
        return DateTime.TryParse(ReadString(settings, key), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out date);
    }
}
