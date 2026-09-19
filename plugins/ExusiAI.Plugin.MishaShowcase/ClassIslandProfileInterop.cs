using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.MishaShowcase;

/// <summary>
/// Direct editor for ClassIsland 2.2 profile JSON. The original JSON tree is retained so
/// fields unknown to this port survive round-trips unchanged.
/// </summary>
internal sealed class ClassIslandProfileDocument
{
    public const string DefaultClassPlanGroupId = "ACAF4EF0-E261-4262-B941-34EA93CB4369";

    private readonly JsonObject root;

    private ClassIslandProfileDocument(string filePath, JsonObject root)
    {
        FilePath = Path.GetFullPath(filePath);
        this.root = root;
        EnsureContainer("Subjects");
        EnsureContainer("TimeLayouts");
        EnsureContainer("ClassPlans");
        EnsureContainer("ClassPlanGroups");
        EnsureContainer("OrderedSchedules");
        EnsureContainer("ScheduleItems");
    }

    public string FilePath { get; private set; }
    public JsonObject Root => root;

    public string Name
    {
        get => ReadString(root, "Name");
        set => root["Name"] = value;
    }

    public int ScheduleType
    {
        get => ReadInt(root, "ScheduleType");
        set => root["ScheduleType"] = value;
    }

    public IReadOnlyList<ClassIslandSubjectRow> Subjects =>
        SubjectsObject.Select(pair => new ClassIslandSubjectRow(this, pair.Key, RequireObject(pair.Value, "Subject")))
            .ToArray();

    public IReadOnlyList<ClassIslandTimeLayoutRow> TimeLayouts =>
        TimeLayoutsObject.Select(pair => new ClassIslandTimeLayoutRow(this, pair.Key, RequireObject(pair.Value, "TimeLayout")))
            .ToArray();

    public IReadOnlyList<ClassIslandClassPlanRow> ClassPlans =>
        ClassPlansObject.Select(pair => new ClassIslandClassPlanRow(this, pair.Key, RequireObject(pair.Value, "ClassPlan")))
            .ToArray();

    public IReadOnlyList<ClassIslandClassPlanGroupRow> ClassPlanGroups =>
        ClassPlanGroupsObject.Select(pair => new ClassIslandClassPlanGroupRow(this, pair.Key, RequireObject(pair.Value, "ClassPlanGroup")))
            .ToArray();

    public IReadOnlyList<ClassIslandOrderedScheduleRow> OrderedSchedules =>
        OrderedSchedulesObject.Select(pair => new ClassIslandOrderedScheduleRow(this, pair.Key, RequireObject(pair.Value, "OrderedSchedule")))
            .ToArray();

    public IReadOnlyList<ClassIslandScheduleItemRow> ScheduleItems =>
        ScheduleItemsObject.Select(pair => new ClassIslandScheduleItemRow(this, pair.Key, RequireObject(pair.Value, "ScheduleItem")))
            .ToArray();

    public string SelectedClassPlanGroupId
    {
        get => ReadString(root, "SelectedClassPlanGroupId");
        set => root["SelectedClassPlanGroupId"] = value;
    }

    public bool IsTempClassPlanGroupEnabled
    {
        get => ReadBool(root, "IsTempClassPlanGroupEnabled");
        set => root["IsTempClassPlanGroupEnabled"] = value;
    }

    public string TempClassPlanGroupId
    {
        get => ReadString(root, "TempClassPlanGroupId");
        set => root["TempClassPlanGroupId"] = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public DateTime TempClassPlanGroupExpireTime
    {
        get => DateTime.TryParse(ReadString(root, "TempClassPlanGroupExpireTime"), out var value) ? value : DateTime.Now;
        set => root["TempClassPlanGroupExpireTime"] = value;
    }

    public int TempClassPlanGroupType
    {
        get => ReadInt(root, "TempClassPlanGroupType");
        set => root["TempClassPlanGroupType"] = value;
    }

    public bool IsOverlayClassPlanEnabled
    {
        get => ReadBool(root, "IsOverlayClassPlanEnabled");
        set => root["IsOverlayClassPlanEnabled"] = value;
    }

    public string OverlayClassPlanId
    {
        get => ReadString(root, "OverlayClassPlanId");
        set => root["OverlayClassPlanId"] = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public string TempClassPlanId
    {
        get => ReadString(root, "TempClassPlanId");
        set => root["TempClassPlanId"] = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static async Task<ClassIslandProfileDocument> LoadAsync(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("ClassIsland 档案不存在。", fullPath);

        var text = await File.ReadAllTextAsync(fullPath);
        var root = JsonNode.Parse(text)?.AsObject()
            ?? throw new InvalidDataException("ClassIsland 档案根节点不是 JSON 对象。");

        // These three objects are the stable core of ClassIsland profiles. Older profiles may
        // omit newer containers, which are created in memory but are never written until Save.
        if (root["Subjects"] is not null && root["Subjects"] is not JsonObject)
            throw new InvalidDataException("Subjects 节点格式无效。");
        if (root["TimeLayouts"] is not null && root["TimeLayouts"] is not JsonObject)
            throw new InvalidDataException("TimeLayouts 节点格式无效。");
        if (root["ClassPlans"] is not null && root["ClassPlans"] is not JsonObject)
            throw new InvalidDataException("ClassPlans 节点格式无效。");

        return new(fullPath, root);
    }

    public async Task SaveAsync()
    {
        await WriteAtomicAsync(FilePath);
    }

    public async Task SaveAsAsync(string destination)
    {
        var fullPath = Path.GetFullPath(destination);
        await WriteAtomicAsync(fullPath);
        FilePath = fullPath;
    }

    public ClassIslandSubjectRow AddSubject(string name = "新科目")
    {
        var id = Guid.NewGuid().ToString();
        var node = new JsonObject
        {
            ["Name"] = name,
            ["Initial"] = string.IsNullOrWhiteSpace(name) ? "" : name[..1],
            ["TeacherName"] = "",
            ["IsOutDoor"] = false
        };
        SubjectsObject[id] = node;
        return new(this, id, node);
    }

    public void RemoveSubject(string id)
    {
        if (IsSubjectReferenced(id))
            throw new InvalidOperationException("该科目仍被课表、时间表默认科目或日程引用，不能删除。");
        SubjectsObject.Remove(id);
    }

    public ClassIslandTimeLayoutRow AddTimeLayout(string name = "新时间表")
    {
        var id = Guid.NewGuid().ToString();
        var node = new JsonObject
        {
            ["Name"] = name,
            ["IsOverlay"] = false,
            ["OverlaySourceId"] = null,
            ["Layouts"] = new JsonArray()
        };
        TimeLayoutsObject[id] = node;
        return new(this, id, node);
    }

    public void RemoveTimeLayout(string id)
    {
        var referenced = ClassPlansObject
            .Select(x => x.Value as JsonObject)
            .Any(x => x is not null && SameId(ReadString(x, "TimeLayoutId"), id));
        if (referenced)
            throw new InvalidOperationException("该时间表仍被课表引用，不能删除。");
        TimeLayoutsObject.Remove(id);
    }

    public ClassIslandTimeLayoutItemRow AddTimeLayoutItem(string layoutId, int timeType)
    {
        var layout = GetObject(TimeLayoutsObject, layoutId, "时间表");
        var layouts = EnsureArray(layout, "Layouts");
        var node = new JsonObject
        {
            ["StartTime"] = "00:00:00",
            ["EndTime"] = "00:00:00",
            ["TimeType"] = timeType,
            ["IsHideDefault"] = false,
            ["DefaultClassId"] = Guid.Empty.ToString(),
            ["BreakName"] = ""
        };
        layouts.Add(node);
        SyncClassPlanLengths(layoutId);
        return new(this, layoutId, node);
    }

    public void RemoveTimeLayoutItem(string layoutId, JsonObject item)
    {
        var layout = GetObject(TimeLayoutsObject, layoutId, "时间表");
        var layouts = EnsureArray(layout, "Layouts");
        var classIndex = GetClassIndex(layouts, item);
        var wasClass = ReadInt(item, "TimeType") == 0;
        if (!layouts.Remove(item)) return;

        if (wasClass)
            RemoveClassAt(layoutId, classIndex);
        else
            SyncClassPlanLengths(layoutId);
    }

    internal void ChangeTimeLayoutItemType(string layoutId, JsonObject item, int newType)
    {
        var layout = GetObject(TimeLayoutsObject, layoutId, "时间表");
        var layouts = EnsureArray(layout, "Layouts");
        var oldType = ReadInt(item, "TimeType");
        if (oldType == newType) return;

        var classIndex = GetClassIndex(layouts, item);
        item["TimeType"] = newType;

        if (oldType == 0 && newType != 0)
            RemoveClassAt(layoutId, classIndex);
        else if (oldType != 0 && newType == 0)
            InsertClassAt(layoutId, classIndex);
        else
            SyncClassPlanLengths(layoutId);
    }

    public ClassIslandClassPlanRow AddClassPlan(string timeLayoutId, string name = "新课表")
    {
        _ = GetObject(TimeLayoutsObject, timeLayoutId, "时间表");
        var id = Guid.NewGuid().ToString();
        var node = new JsonObject
        {
            ["TimeLayoutId"] = timeLayoutId,
            ["Name"] = name,
            ["TimeRule"] = new JsonObject
            {
                ["Type"] = 0,
                ["RestrictsEnableRange"] = false,
                ["WeekDay"] = (int)DateTime.Today.DayOfWeek,
                ["WeekCountDiv"] = 0,
                ["WeekCountDivTotal"] = 2,
                ["EnableDates"] = new JsonArray(),
                ["LoopCycleDays"] = 3,
                ["LoopOffsetDays"] = 0
            },
            ["Classes"] = new JsonArray(),
            ["IsOverlay"] = false,
            ["OverlaySourceId"] = null,
            ["IsEnabled"] = true,
            ["AssociatedGroup"] = DefaultClassPlanGroupId
        };
        ClassPlansObject[id] = node;
        EnsureClassPlanLength(id);
        return new(this, id, node);
    }

    public ClassIslandClassPlanGroupRow AddClassPlanGroup(string name = "新课表群")
    {
        var id = Guid.NewGuid().ToString();
        var node = new JsonObject { ["Name"] = name, ["IsGlobal"] = false };
        ClassPlanGroupsObject[id] = node;
        return new(this, id, node);
    }

    public void RemoveClassPlanGroup(string id)
    {
        if (SameId(id, DefaultClassPlanGroupId) || SameId(id, Guid.Empty.ToString()))
            throw new InvalidOperationException("默认课表群和全局课表群不能删除。");

        foreach (var plan in ClassPlans)
        {
            if (SameId(plan.AssociatedGroup, id))
                plan.AssociatedGroup = DefaultClassPlanGroupId;
        }

        var key = FindKey(ClassPlanGroupsObject, id);
        if (key is not null) ClassPlanGroupsObject.Remove(key);

        if (SameId(SelectedClassPlanGroupId, id))
            SelectedClassPlanGroupId = DefaultClassPlanGroupId;
        if (SameId(TempClassPlanGroupId, id))
        {
            TempClassPlanGroupId = "";
            IsTempClassPlanGroupEnabled = false;
        }
    }

    public ClassIslandOrderedScheduleRow AddOrderedSchedule(DateTime date, string classPlanId)
    {
        _ = GetObject(ClassPlansObject, classPlanId, "课表");
        var key = date.ToString("O", CultureInfo.InvariantCulture);
        var node = new JsonObject { ["ClassPlanId"] = classPlanId };
        OrderedSchedulesObject[key] = node;
        return new(this, key, node);
    }

    public void RemoveOrderedSchedule(string dateKey)
    {
        OrderedSchedulesObject.Remove(dateKey);
    }

    public ClassIslandScheduleItemRow AddScheduleItem()
    {
        var id = Guid.NewGuid().ToString();
        var node = new JsonObject
        {
            ["SubjectId"] = Guid.Empty.ToString(),
            ["StartTime"] = "00:00:00",
            ["EndTime"] = "00:00:00",
            ["EnableRule"] = new JsonObject
            {
                ["Type"] = 0,
                ["RestrictsEnableRange"] = false,
                ["WeekDay"] = (int)DateTime.Today.DayOfWeek,
                ["WeekCountDiv"] = 0,
                ["WeekCountDivTotal"] = 2,
                ["EnableDates"] = new JsonArray(),
                ["LoopCycleDays"] = 3,
                ["LoopOffsetDays"] = 0
            }
        };
        ScheduleItemsObject[id] = node;
        return new(this, id, node);
    }

    public void RemoveScheduleItem(string id)
    {
        var key = FindKey(ScheduleItemsObject, id);
        if (key is not null) ScheduleItemsObject.Remove(key);
    }

    public void RemoveClassPlan(string id)
    {
        var planKey = FindKey(ClassPlansObject, id);
        if (planKey is not null) ClassPlansObject.Remove(planKey);
        var ordered = OrderedSchedulesObject;
        foreach (var key in ordered.Where(pair =>
                     pair.Value is JsonObject o && SameId(ReadString(o, "ClassPlanId"), id))
                     .Select(pair => pair.Key).ToArray())
            ordered.Remove(key);

        if (SameId(ReadString(root, "OverlayClassPlanId"), id)) root["OverlayClassPlanId"] = null;
        if (SameId(ReadString(root, "TempClassPlanId"), id)) root["TempClassPlanId"] = null;
    }

    public IReadOnlyList<ClassIslandTimeLayoutItemRow> GetTimeLayoutItems(string layoutId)
    {
        var layout = GetObject(TimeLayoutsObject, layoutId, "时间表");
        return EnsureArray(layout, "Layouts").OfType<JsonObject>()
            .Select(node => new ClassIslandTimeLayoutItemRow(this, layoutId, node))
            .ToArray();
    }

    public IReadOnlyList<ClassIslandLessonRow> GetLessons(string classPlanId)
    {
        EnsureClassPlanLength(classPlanId);
        var plan = GetObject(ClassPlansObject, classPlanId, "课表");
        var layoutId = ReadString(plan, "TimeLayoutId");
        var layout = GetObject(TimeLayoutsObject, layoutId, "时间表");
        var timePoints = EnsureArray(layout, "Layouts").OfType<JsonObject>()
            .Where(point => ReadInt(point, "TimeType") == 0)
            .ToArray();
        var classes = EnsureArray(plan, "Classes").OfType<JsonObject>().ToArray();

        return Enumerable.Range(0, Math.Min(timePoints.Length, classes.Length))
            .Select(index => new ClassIslandLessonRow(this, classPlanId, index, timePoints[index], classes[index]))
            .ToArray();
    }

    public IReadOnlyList<ClassIslandLessonSnapshot> GetLessonsForDate(DateTime date, int rotationWeek)
    {
        var candidates = ClassPlans
            .Where(plan => plan.IsEnabled && RuleMatches(plan.Node, date, rotationWeek))
            .ToArray();

        var result = new List<ClassIslandLessonSnapshot>();
        foreach (var plan in candidates)
        {
            foreach (var lesson in GetLessons(plan.Id).Where(x => x.Enabled))
            {
                result.Add(new(
                    plan.Id,
                    plan.Name,
                    lesson.Index,
                    lesson.SubjectName,
                    lesson.TeacherName,
                    ParseTime(lesson.StartTime),
                    ParseTime(lesson.EndTime)));
            }
        }
        return result.OrderBy(x => x.Start).ThenBy(x => x.Index).ToArray();
    }

    public string ResolveSubjectName(string id) =>
        TryGetObject(SubjectsObject, id, out var subject) ? ReadString(subject, "Name") : "";

    public string ResolveTeacherName(string id) =>
        TryGetObject(SubjectsObject, id, out var subject) ? ReadString(subject, "TeacherName") : "";

    public string ResolveSubjectIdByName(string name)
    {
        foreach (var pair in SubjectsObject)
        {
            if (pair.Value is JsonObject subject &&
                string.Equals(ReadString(subject, "Name"), name, StringComparison.CurrentCultureIgnoreCase))
                return pair.Key;
        }
        return "";
    }

    public ClassIslandSubjectRow GetOrCreateSubject(string name)
    {
        var id = ResolveSubjectIdByName(name);
        if (!string.IsNullOrWhiteSpace(id) && TryGetObject(SubjectsObject, id, out var existing))
            return new(this, FindKey(SubjectsObject, id) ?? id, existing);
        return AddSubject(name);
    }

    internal JsonObject GetClassPlanNode(string id) => GetObject(ClassPlansObject, id, "课表");
    internal JsonObject GetTimeLayoutNode(string id) => GetObject(TimeLayoutsObject, id, "时间表");

    internal static string ReadString(JsonObject node, string property)
    {
        if (node[property] is JsonValue value && value.TryGetValue<string>(out var text))
            return text ?? "";
        return node[property]?.ToJsonString().Trim('"') ?? "";
    }

    internal static int ReadInt(JsonObject node, string property, int fallback = 0)
    {
        if (node[property] is JsonValue value)
        {
            if (value.TryGetValue<int>(out var number)) return number;
            if (value.TryGetValue<string>(out var text) && int.TryParse(text, out number)) return number;
        }
        return fallback;
    }

    internal static bool ReadBool(JsonObject node, string property, bool fallback = false)
    {
        if (node[property] is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var result)) return result;
            if (value.TryGetValue<string>(out var text) && bool.TryParse(text, out result)) return result;
        }
        return fallback;
    }

    internal static string NormalizeTime(string input)
    {
        if (!TimeSpan.TryParse(input, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("时间必须是 HH:mm 或 HH:mm:ss。");
        if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
            throw new FormatException("时间必须位于同一天内。");
        return value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private JsonObject SubjectsObject => EnsureObject(root, "Subjects");
    private JsonObject TimeLayoutsObject => EnsureObject(root, "TimeLayouts");
    private JsonObject ClassPlansObject => EnsureObject(root, "ClassPlans");
    private JsonObject ClassPlanGroupsObject => EnsureObject(root, "ClassPlanGroups");
    private JsonObject OrderedSchedulesObject => EnsureObject(root, "OrderedSchedules");
    private JsonObject ScheduleItemsObject => EnsureObject(root, "ScheduleItems");

    private void EnsureContainer(string name) => _ = EnsureObject(root, name);

    private static JsonObject EnsureObject(JsonObject owner, string name)
    {
        if (owner[name] is JsonObject result) return result;
        result = new JsonObject();
        owner[name] = result;
        return result;
    }

    private static JsonArray EnsureArray(JsonObject owner, string name)
    {
        if (owner[name] is JsonArray result) return result;
        result = new JsonArray();
        owner[name] = result;
        return result;
    }

    private static JsonObject RequireObject(JsonNode? node, string label) =>
        node as JsonObject ?? throw new InvalidDataException($"{label} 节点不是对象。");

    private static JsonObject GetObject(JsonObject owner, string id, string label) =>
        TryGetObject(owner, id, out var value)
            ? value
            : throw new KeyNotFoundException($"{label} {id} 不存在。");

    private static bool TryGetObject(JsonObject owner, string id, out JsonObject value)
    {
        var key = FindKey(owner, id);
        if (key is not null && owner[key] is JsonObject found)
        {
            value = found;
            return true;
        }

        value = null!;
        return false;
    }

    private static string? FindKey(JsonObject owner, string id)
    {
        if (owner.ContainsKey(id)) return id;
        return owner.Select(x => x.Key).FirstOrDefault(key => SameId(key, id));
    }

    private static bool SameId(string left, string right) =>
        Guid.TryParse(left, out var a) && Guid.TryParse(right, out var b)
            ? a == b
            : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private bool IsSubjectReferenced(string id)
    {
        foreach (var plan in ClassPlansObject.Select(x => x.Value as JsonObject).Where(x => x is not null))
        {
            if (EnsureArray(plan!, "Classes").OfType<JsonObject>()
                .Any(x => SameId(ReadString(x, "SubjectId"), id)))
                return true;
        }

        foreach (var layout in TimeLayoutsObject.Select(x => x.Value as JsonObject).Where(x => x is not null))
        {
            if (EnsureArray(layout!, "Layouts").OfType<JsonObject>()
                .Any(x => SameId(ReadString(x, "DefaultClassId"), id)))
                return true;
        }

        var scheduleItems = EnsureObject(root, "ScheduleItems");
        return scheduleItems.Select(x => x.Value as JsonObject).Where(x => x is not null)
            .Any(x => SameId(ReadString(x!, "SubjectId"), id));
    }

    private void SyncClassPlanLengths(string layoutId)
    {
        foreach (var pair in ClassPlansObject)
        {
            if (pair.Value is JsonObject plan && SameId(ReadString(plan, "TimeLayoutId"), layoutId))
                EnsureClassPlanLength(pair.Key);
        }
    }

    private static int GetClassIndex(JsonArray layouts, JsonObject target)
    {
        var index = 0;
        foreach (var item in layouts.OfType<JsonObject>())
        {
            if (ReferenceEquals(item, target)) return index;
            if (ReadInt(item, "TimeType") == 0) index++;
        }
        return index;
    }

    private void RemoveClassAt(string layoutId, int classIndex)
    {
        foreach (var pair in ClassPlansObject)
        {
            if (pair.Value is not JsonObject plan || !SameId(ReadString(plan, "TimeLayoutId"), layoutId))
                continue;

            var classes = EnsureArray(plan, "Classes");
            if (classIndex >= 0 && classIndex < classes.Count)
                classes.RemoveAt(classIndex);
            EnsureClassPlanLength(pair.Key);
        }
    }

    private void InsertClassAt(string layoutId, int classIndex)
    {
        foreach (var pair in ClassPlansObject)
        {
            if (pair.Value is not JsonObject plan || !SameId(ReadString(plan, "TimeLayoutId"), layoutId))
                continue;

            var classes = EnsureArray(plan, "Classes");
            var node = new JsonObject
            {
                ["SubjectId"] = Guid.Empty.ToString(),
                ["IsChangedClass"] = false,
                ["IsEnabled"] = true
            };
            classes.Insert(Math.Clamp(classIndex, 0, classes.Count), node);
            EnsureClassPlanLength(pair.Key);
        }
    }

    private void EnsureClassPlanLength(string classPlanId)
    {
        var plan = GetObject(ClassPlansObject, classPlanId, "课表");
        var layoutId = ReadString(plan, "TimeLayoutId");
        if (TimeLayoutsObject[layoutId] is not JsonObject layout) return;

        var expected = EnsureArray(layout, "Layouts").OfType<JsonObject>()
            .Count(x => ReadInt(x, "TimeType") == 0);
        var classes = EnsureArray(plan, "Classes");

        while (classes.Count < expected)
        {
            classes.Add(new JsonObject
            {
                ["SubjectId"] = Guid.Empty.ToString(),
                ["IsChangedClass"] = false,
                ["IsEnabled"] = true
            });
        }
        while (classes.Count > expected)
            classes.RemoveAt(classes.Count - 1);
    }

    private static bool RuleMatches(JsonObject plan, DateTime date, int rotationWeek)
    {
        var rule = plan["TimeRule"] as JsonObject;
        if (rule is null) return true;

        if (ReadBool(rule, "RestrictsEnableRange"))
        {
            if (DateOnly.TryParse(ReadString(rule, "RangeStart"), out var start) &&
                DateOnly.FromDateTime(date) < start) return false;
            if (DateOnly.TryParse(ReadString(rule, "RangeEnd"), out var end) &&
                DateOnly.FromDateTime(date) > end) return false;
        }

        var type = ReadInt(rule, "Type");
        if (type == 0)
        {
            if (ReadInt(rule, "WeekDay") != (int)date.DayOfWeek) return false;
            var week = ReadInt(rule, "WeekCountDiv");
            return week <= 0 || week == rotationWeek;
        }

        if (type == 1)
        {
            if (rule["EnableDates"] is not JsonArray dates) return false;
            return dates.Any(item =>
                DateOnly.TryParse(item?.ToString().Trim('"'), out var target) &&
                target == DateOnly.FromDateTime(date));
        }

        if (type == 2)
        {
            var cycle = Math.Max(1, ReadInt(rule, "LoopCycleDays", 3));
            var offset = ReadInt(rule, "LoopOffsetDays");
            DateOnly anchor;
            if (!DateOnly.TryParse(ReadString(rule, "RangeStart"), out anchor))
                anchor = new DateOnly(date.Year, 1, 1);
            var days = DateOnly.FromDateTime(date).DayNumber - anchor.DayNumber;
            return ((days - offset) % cycle + cycle) % cycle == 0;
        }

        return true;
    }

    private static TimeSpan ParseTime(string value) =>
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : TimeSpan.Zero;

    private async Task WriteAtomicAsync(string destination)
    {
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("档案目标路径无效。");
        Directory.CreateDirectory(directory);

        var temporary = destination + ".tmp";
        var backup = destination + ".bak";
        await File.WriteAllTextAsync(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        if (File.Exists(destination))
            File.Copy(destination, backup, true);
        File.Move(temporary, destination, true);
    }
}

internal sealed class ClassIslandSubjectRow
{
    internal readonly JsonObject Node;

    internal ClassIslandSubjectRow(ClassIslandProfileDocument owner, string id, JsonObject node)
    {
        Id = id;
        Node = node;
    }

    public string Id { get; }
    public string Name { get => ClassIslandProfileDocument.ReadString(Node, "Name"); set => Node["Name"] = value; }
    public string Initial { get => ClassIslandProfileDocument.ReadString(Node, "Initial"); set => Node["Initial"] = value; }
    public string TeacherName { get => ClassIslandProfileDocument.ReadString(Node, "TeacherName"); set => Node["TeacherName"] = value; }
    public bool IsOutDoor { get => ClassIslandProfileDocument.ReadBool(Node, "IsOutDoor"); set => Node["IsOutDoor"] = value; }
}

internal sealed class ClassIslandTimeLayoutRow
{
    private readonly ClassIslandProfileDocument owner;
    internal readonly JsonObject Node;

    internal ClassIslandTimeLayoutRow(ClassIslandProfileDocument owner, string id, JsonObject node)
    {
        this.owner = owner;
        Id = id;
        Node = node;
    }

    public string Id { get; }
    public string Name { get => ClassIslandProfileDocument.ReadString(Node, "Name"); set => Node["Name"] = value; }
    public bool IsOverlay { get => ClassIslandProfileDocument.ReadBool(Node, "IsOverlay"); set => Node["IsOverlay"] = value; }
    public IReadOnlyList<ClassIslandTimeLayoutItemRow> Items => owner.GetTimeLayoutItems(Id);
}

internal sealed class ClassIslandTimeLayoutItemRow
{
    private readonly ClassIslandProfileDocument owner;
    private readonly string layoutId;
    internal readonly JsonObject Node;

    internal ClassIslandTimeLayoutItemRow(ClassIslandProfileDocument owner, string layoutId, JsonObject node)
    {
        this.owner = owner;
        this.layoutId = layoutId;
        Node = node;
    }

    public string StartTime
    {
        get => ClassIslandProfileDocument.ReadString(Node, "StartTime");
        set => Node["StartTime"] = ClassIslandProfileDocument.NormalizeTime(value);
    }

    public string EndTime
    {
        get => ClassIslandProfileDocument.ReadString(Node, "EndTime");
        set => Node["EndTime"] = ClassIslandProfileDocument.NormalizeTime(value);
    }

    public int TimeType
    {
        get => ClassIslandProfileDocument.ReadInt(Node, "TimeType");
        set => owner.ChangeTimeLayoutItemType(layoutId, Node, value);
    }

    public bool IsHideDefault
    {
        get => ClassIslandProfileDocument.ReadBool(Node, "IsHideDefault");
        set => Node["IsHideDefault"] = value;
    }

    public string BreakName
    {
        get => ClassIslandProfileDocument.ReadString(Node, "BreakName");
        set => Node["BreakName"] = value;
    }

    public string DefaultSubject
    {
        get => owner.ResolveSubjectName(ClassIslandProfileDocument.ReadString(Node, "DefaultClassId"));
        set
        {
            var subject = owner.GetOrCreateSubject(value);
            Node["DefaultClassId"] = subject.Id;
        }
    }
}

internal sealed class ClassIslandClassPlanRow
{
    private readonly ClassIslandProfileDocument owner;
    internal readonly JsonObject Node;

    internal ClassIslandClassPlanRow(ClassIslandProfileDocument owner, string id, JsonObject node)
    {
        this.owner = owner;
        Id = id;
        Node = node;
    }

    public string Id { get; }
    public string Name { get => ClassIslandProfileDocument.ReadString(Node, "Name"); set => Node["Name"] = value; }
    public string TimeLayoutId { get => ClassIslandProfileDocument.ReadString(Node, "TimeLayoutId"); set => Node["TimeLayoutId"] = value; }
    public bool IsEnabled { get => ClassIslandProfileDocument.ReadBool(Node, "IsEnabled", true); set => Node["IsEnabled"] = value; }
    public string AssociatedGroup { get => ClassIslandProfileDocument.ReadString(Node, "AssociatedGroup"); set => Node["AssociatedGroup"] = value; }

    private JsonObject Rule
    {
        get
        {
            if (Node["TimeRule"] is JsonObject rule) return rule;
            rule = new JsonObject();
            Node["TimeRule"] = rule;
            return rule;
        }
    }

    public int RuleType { get => ClassIslandProfileDocument.ReadInt(Rule, "Type"); set => Rule["Type"] = value; }
    public int WeekDay { get => ClassIslandProfileDocument.ReadInt(Rule, "WeekDay"); set => Rule["WeekDay"] = value; }
    public int WeekCountDiv { get => ClassIslandProfileDocument.ReadInt(Rule, "WeekCountDiv"); set => Rule["WeekCountDiv"] = value; }
    public int WeekCountDivTotal { get => ClassIslandProfileDocument.ReadInt(Rule, "WeekCountDivTotal", 2); set => Rule["WeekCountDivTotal"] = value; }
    public IReadOnlyList<ClassIslandLessonRow> Lessons => owner.GetLessons(Id);
}

internal sealed class ClassIslandLessonRow
{
    private readonly ClassIslandProfileDocument owner;
    private readonly JsonObject timePoint;
    private readonly JsonObject classNode;

    internal ClassIslandLessonRow(ClassIslandProfileDocument owner, string planId, int index, JsonObject timePoint, JsonObject classNode)
    {
        this.owner = owner;
        PlanId = planId;
        Index = index + 1;
        this.timePoint = timePoint;
        this.classNode = classNode;
    }

    public string PlanId { get; }
    public int Index { get; }

    public string SubjectName
    {
        get => owner.ResolveSubjectName(ClassIslandProfileDocument.ReadString(classNode, "SubjectId"));
        set
        {
            var subject = owner.GetOrCreateSubject(value);
            classNode["SubjectId"] = subject.Id;
        }
    }

    public string TeacherName =>
        owner.ResolveTeacherName(ClassIslandProfileDocument.ReadString(classNode, "SubjectId"));

    public string StartTime => ClassIslandProfileDocument.ReadString(timePoint, "StartTime");
    public string EndTime => ClassIslandProfileDocument.ReadString(timePoint, "EndTime");

    public bool Enabled
    {
        get => ClassIslandProfileDocument.ReadBool(classNode, "IsEnabled", true);
        set => classNode["IsEnabled"] = value;
    }
}

internal sealed record ClassIslandLessonSnapshot(
    string PlanId,
    string PlanName,
    int Index,
    string Subject,
    string Teacher,
    TimeSpan Start,
    TimeSpan End);


internal sealed class ClassIslandClassPlanGroupRow
{
    internal readonly JsonObject Node;

    internal ClassIslandClassPlanGroupRow(ClassIslandProfileDocument owner, string id, JsonObject node)
    {
        Id = id;
        Node = node;
    }

    public string Id { get; }
    public string Name
    {
        get => ClassIslandProfileDocument.ReadString(Node, "Name");
        set => Node["Name"] = value;
    }

    public bool IsGlobal
    {
        get => ClassIslandProfileDocument.ReadBool(Node, "IsGlobal");
        set => Node["IsGlobal"] = value;
    }
}

internal sealed class ClassIslandOrderedScheduleRow
{
    private readonly ClassIslandProfileDocument owner;
    internal readonly JsonObject Node;

    internal ClassIslandOrderedScheduleRow(ClassIslandProfileDocument owner, string dateKey, JsonObject node)
    {
        this.owner = owner;
        DateKey = dateKey;
        Node = node;
    }

    public string DateKey { get; }
    public DateTime Date => DateTime.TryParse(DateKey, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
        ? value
        : DateTime.MinValue;

    public string ClassPlanId
    {
        get => ClassIslandProfileDocument.ReadString(Node, "ClassPlanId");
        set => Node["ClassPlanId"] = value;
    }

    public string ClassPlanName =>
        owner.ClassPlans.FirstOrDefault(x => string.Equals(x.Id, ClassPlanId, StringComparison.OrdinalIgnoreCase))?.Name
        ?? owner.ClassPlans.FirstOrDefault(x => Guid.TryParse(x.Id, out var a) && Guid.TryParse(ClassPlanId, out var b) && a == b)?.Name
        ?? "";
}

internal sealed class ClassIslandScheduleItemRow
{
    private readonly ClassIslandProfileDocument owner;
    internal readonly JsonObject Node;

    internal ClassIslandScheduleItemRow(ClassIslandProfileDocument owner, string id, JsonObject node)
    {
        this.owner = owner;
        Id = id;
        Node = node;
    }

    public string Id { get; }

    public string SubjectName
    {
        get => owner.ResolveSubjectName(ClassIslandProfileDocument.ReadString(Node, "SubjectId"));
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Node["SubjectId"] = Guid.Empty.ToString();
                return;
            }

            Node["SubjectId"] = owner.GetOrCreateSubject(value).Id;
        }
    }

    public string StartTime
    {
        get => ClassIslandProfileDocument.ReadString(Node, "StartTime");
        set
        {
            var normalized = ClassIslandProfileDocument.NormalizeTime(value);
            Node["StartTime"] = normalized;
            if (TimeSpan.TryParse(EndTime, CultureInfo.InvariantCulture, out var end) &&
                TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out var start) &&
                end < start)
                Node["EndTime"] = normalized;
        }
    }

    public string EndTime
    {
        get => ClassIslandProfileDocument.ReadString(Node, "EndTime");
        set
        {
            var normalized = ClassIslandProfileDocument.NormalizeTime(value);
            Node["EndTime"] = normalized;
            if (TimeSpan.TryParse(StartTime, CultureInfo.InvariantCulture, out var start) &&
                TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out var end) &&
                start > end)
                Node["StartTime"] = normalized;
        }
    }

    private JsonObject Rule
    {
        get
        {
            if (Node["EnableRule"] is JsonObject rule) return rule;
            rule = new JsonObject();
            Node["EnableRule"] = rule;
            return rule;
        }
    }

    public int RuleType { get => ClassIslandProfileDocument.ReadInt(Rule, "Type"); set => Rule["Type"] = value; }
    public int WeekDay { get => ClassIslandProfileDocument.ReadInt(Rule, "WeekDay"); set => Rule["WeekDay"] = value; }
    public int WeekCountDiv { get => ClassIslandProfileDocument.ReadInt(Rule, "WeekCountDiv"); set => Rule["WeekCountDiv"] = value; }
    public int WeekCountDivTotal { get => ClassIslandProfileDocument.ReadInt(Rule, "WeekCountDivTotal", 2); set => Rule["WeekCountDivTotal"] = value; }
}
