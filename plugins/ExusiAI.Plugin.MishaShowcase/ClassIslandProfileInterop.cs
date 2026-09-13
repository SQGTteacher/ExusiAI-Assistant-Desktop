using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.MishaShowcase;

internal static class ClassIslandProfileInterop
{
    public static JsonObject Parse(string json, MishaPlatformState target)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("ClassIsland 档案根节点无效。");
        if (root["Subjects"] is not JsonObject subjects || root["TimeLayouts"] is not JsonObject timeLayouts || root["ClassPlans"] is not JsonObject classPlans)
            throw new InvalidDataException("文件不是有效的 ClassIsland 档案：缺少 Subjects、TimeLayouts 或 ClassPlans。");

        target.ProfileName = root["Name"]?.GetValue<string>() ?? target.ProfileName;
        var subjectMap = subjects.Where(x => x.Value is JsonObject).ToDictionary(
            x => x.Key,
            x => ((JsonObject)x.Value!)["Name"]?.GetValue<string>() ?? "未命名科目",
            StringComparer.OrdinalIgnoreCase);
        var teacherMap = subjects.Where(x => x.Value is JsonObject).ToDictionary(
            x => x.Key,
            x => ((JsonObject)x.Value!)["TeacherName"]?.GetValue<string>() ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);

        var selectedPlan = classPlans.Select(x => x.Value as JsonObject)
            .FirstOrDefault(x => x is not null && (x["IsEnabled"]?.GetValue<bool>() ?? true))
            ?? classPlans.Select(x => x.Value as JsonObject).FirstOrDefault(x => x is not null);
        if (selectedPlan is null) return root;
        var layoutId = selectedPlan["TimeLayoutId"]?.GetValue<string>();
        var layout = layoutId is not null ? timeLayouts[layoutId] as JsonObject : null;
        layout ??= timeLayouts.Select(x => x.Value as JsonObject).FirstOrDefault(x => x is not null);
        if (layout?["Layouts"] is not JsonArray points || selectedPlan["Classes"] is not JsonArray classes) return root;

        var classPoints = points.OfType<JsonObject>().Where(x => (x["TimeType"]?.GetValue<int>() ?? 0) == 0).ToArray();
        var imported = new ObservableCollection<ScheduleEntry>();
        var weekValue = selectedPlan["TimeRule"]?["WeekCountDiv"]?.GetValue<int>() ?? 0;
        for (var index = 0; index < Math.Min(classPoints.Length, classes.Count); index++)
        {
            if (classes[index] is not JsonObject lesson) continue;
            var subjectId = lesson["SubjectId"]?.GetValue<string>() ?? string.Empty;
            imported.Add(new(index + 1,
                subjectMap.GetValueOrDefault(subjectId, "未命名科目"),
                teacherMap.GetValueOrDefault(subjectId, string.Empty),
                ReadTime(classPoints[index], "StartTime"), ReadTime(classPoints[index], "EndTime"),
                weekValue > 0 ? weekValue : 1,
                lesson["IsEnabled"]?.GetValue<bool>() ?? true));
        }
        if (imported.Count > 0) target.Schedule = imported;
        return root;
    }

    public static string Write(MishaPlatformState state, JsonObject? importedRoot)
    {
        var root = importedRoot?.DeepClone().AsObject() ?? new JsonObject();
        root["Name"] = state.ProfileName;
        root["Id"] ??= Guid.NewGuid().ToString();
        root["IsOverlayClassPlanEnabled"] ??= false;
        root["ClassPlanGroups"] ??= new JsonObject();
        root["OrderedSchedules"] ??= new JsonObject();
        root["ScheduleItems"] ??= new JsonObject();
        root["ScheduleType"] ??= 0;

        var subjects = root["Subjects"] as JsonObject;
        if (subjects is null) { subjects = new JsonObject(); root["Subjects"] = subjects; }
        var idsByName = subjects.Where(x => x.Value is JsonObject)
            .GroupBy(x => ((JsonObject)x.Value!)["Name"]?.GetValue<string>() ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Key, StringComparer.CurrentCultureIgnoreCase);
        foreach (var lesson in state.Schedule)
        {
            if (idsByName.ContainsKey(lesson.Subject)) continue;
            var id = Guid.NewGuid().ToString();
            idsByName[lesson.Subject] = id;
            subjects[id] = new JsonObject { ["Name"] = lesson.Subject, ["Initial"] = lesson.Subject.FirstOrDefault().ToString(), ["TeacherName"] = lesson.Teacher, ["IsOutDoor"] = false };
        }
        var timeLayouts = root["TimeLayouts"] as JsonObject;
        if (timeLayouts is null) { timeLayouts = new JsonObject(); root["TimeLayouts"] = timeLayouts; }
        var layoutPair = timeLayouts.FirstOrDefault(x => x.Value is JsonObject);
        var layoutId = string.IsNullOrEmpty(layoutPair.Key) ? Guid.NewGuid().ToString() : layoutPair.Key;
        var layout = layoutPair.Value as JsonObject ?? new JsonObject();
        layout["Name"] = layout["Name"]?.GetValue<string>() ?? "ExusiAI 导出时间表";
        layout["IsOverlay"] ??= false;
        var points = new JsonArray();
        foreach (var lesson in state.Schedule.OrderBy(x => x.Index))
            points.Add(new JsonObject { ["StartTime"] = NormalizeTime(lesson.Start), ["EndTime"] = NormalizeTime(lesson.End), ["TimeType"] = 0, ["IsHideDefault"] = false, ["DefaultClassId"] = Guid.Empty.ToString(), ["BreakName"] = string.Empty });
        layout["Layouts"] = points;
        if (layoutPair.Value is null) timeLayouts[layoutId] = layout;

        var classPlans = root["ClassPlans"] as JsonObject;
        if (classPlans is null) { classPlans = new JsonObject(); root["ClassPlans"] = classPlans; }
        var planPair = classPlans.FirstOrDefault(x => x.Value is JsonObject);
        var planId = string.IsNullOrEmpty(planPair.Key) ? Guid.NewGuid().ToString() : planPair.Key;
        var plan = planPair.Value as JsonObject ?? new JsonObject();
        plan["Name"] = plan["Name"]?.GetValue<string>() ?? "ExusiAI 导出课表";
        plan["TimeLayoutId"] = layoutId;
        plan["IsEnabled"] = true;
        plan["IsOverlay"] ??= false;
        plan["TimeRule"] = new JsonObject { ["Type"] = 0, ["WeekDay"] = (int)DateTime.Today.DayOfWeek, ["WeekCountDiv"] = state.CycleWeek, ["WeekCountDivTotal"] = Math.Max(2, state.CycleWeek) };
        var classes = new JsonArray();
        foreach (var lesson in state.Schedule.OrderBy(x => x.Index))
            classes.Add(new JsonObject { ["SubjectId"] = idsByName[lesson.Subject], ["IsChangedClass"] = false, ["IsEnabled"] = lesson.Enabled });
        plan["Classes"] = classes;
        if (planPair.Value is null) classPlans[planId] = plan;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ReadTime(JsonObject point, string property) =>
        TimeSpan.TryParse(point[property]?.GetValue<string>(), CultureInfo.InvariantCulture, out var value) ? value.ToString(@"hh\:mm", CultureInfo.InvariantCulture) : "00:00";

    private static string NormalizeTime(string value) =>
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : "00:00:00";
}
