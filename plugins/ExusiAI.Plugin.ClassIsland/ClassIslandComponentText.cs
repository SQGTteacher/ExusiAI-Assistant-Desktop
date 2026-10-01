using System.Text.Json;
using System.Globalization;

namespace ExusiAI.Plugin.ClassIsland;

public static class ClassIslandComponentText
{
    public static string? Resolve(ClassIslandComponentSettings component, ClassIslandTimetableService timetable, DateTime now)
    {
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
        if (id == new Guid("7C645D35-8151-48BA-B4AC-15017460D994"))
        {
            if (ReadInt(component.Settings, "CountdownSource") != 0 || !TryReadDate(component.Settings, "OverTime", out var end)) return null;
            TryReadDate(component.Settings, "StartTime", out var start);
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

    private static bool ReadBool(JsonElement? settings, string key, bool defaultValue = false) =>
        settings is { ValueKind: JsonValueKind.Object } && settings.Value.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : defaultValue;

    private static int ReadInt(JsonElement? settings, string key) =>
        settings is { ValueKind: JsonValueKind.Object } && settings.Value.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

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
