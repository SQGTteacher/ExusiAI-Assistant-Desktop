using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed record ClassIslandAutomationCatalogItem(string Id, string Name, string Kind);

internal static class ClassIslandAutomationCatalog
{
    public static IReadOnlyList<ClassIslandAutomationCatalogItem> Triggers { get; } =
    [
        new("classisland.lessons.onClass", "上课时", "Trigger"),
        new("classisland.lessons.onBreakingTime", "课间休息时", "Trigger"),
        new("classisland.lessons.onAfterSchool", "放学时", "Trigger"),
        new("classisland.lessons.currentTimeStateChanged", "当前时间状态变化时", "Trigger"),
        new("classisland.lessons.preTimePoint", "特定时间点前", "Trigger"),
        new("classisland.cron", "cron", "Trigger"),
        new("classisland.signal", "收到信号时", "Trigger"),
        new("classisland.uri", "调用 Uri 时", "Trigger"),
        new("classisland.trayMenu", "从托盘菜单运行时", "Trigger"),
        new("classisland.lifetime.startup", "应用启动时", "Trigger"),
        new("classisland.lifetime.stopping", "应用退出时", "Trigger"),
        new("classisland.ruleSet.rulesetChanged", "规则集更新时", "Trigger")
    ];

    public static IReadOnlyList<ClassIslandAutomationCatalogItem> Actions { get; } =
    [
        new("classisland.showNotification", "显示提醒", "Action"),
        new("classisland.notification.weather", "显示天气提醒", "Action"),
        new("classisland.action.sleep", "等待时长", "Action"),
        new("classisland.os.run", "运行", "Action"),
        new("classisland.settings", "应用设置", "Action"),
        new("classisland.app.restart", "重启 ClassIsland", "Action"),
        new("classisland.app.quit", "退出 ClassIsland", "Action")
    ];

    public static string Resolve(string id) =>
        Triggers.Concat(Actions).FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;
}

internal sealed class ClassIslandAutomationDocument
{
    private readonly JsonArray root;

    private ClassIslandAutomationDocument(string filePath, JsonArray root)
    {
        FilePath = Path.GetFullPath(filePath);
        this.root = root;
    }

    public string FilePath { get; }
    public JsonArray Root => root;

    public IReadOnlyList<ClassIslandWorkflowRow> Workflows =>
        root.OfType<JsonObject>()
            .Select((node, index) => new ClassIslandWorkflowRow(this, node, index))
            .ToArray();

    public static async Task<ClassIslandAutomationDocument> LoadAsync(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        if (!File.Exists(full))
            throw new FileNotFoundException("ClassIsland 自动化配置不存在。", full);

        var node = JsonNode.Parse(await File.ReadAllTextAsync(full))
            ?? throw new InvalidDataException("自动化 JSON 为空。");
        if (node is not JsonArray array)
            throw new InvalidDataException("ClassIsland 自动化配置根节点必须是数组。");

        return new(full, array);
    }

    public ClassIslandWorkflowRow AddWorkflow()
    {
        var actionSetId = Guid.NewGuid();
        var node = new JsonObject
        {
            ["Triggers"] = new JsonArray(),
            ["IsConditionEnabled"] = false,
            ["Ruleset"] = new JsonObject
            {
                ["Mode"] = 0,
                ["IsReversed"] = false,
                ["Groups"] = new JsonArray()
            },
            ["ActionSet"] = new JsonObject
            {
                ["Name"] = "新工作流",
                ["Actions"] = new JsonArray(),
                ["IsEnabled"] = true,
                ["IsRevertEnabled"] = false,
                ["Status"] = 0,
                ["Guid"] = actionSetId
            }
        };
        root.Add(node);
        return new(this, node, root.Count - 1);
    }

    public void RemoveWorkflow(ClassIslandWorkflowRow workflow) => root.Remove(workflow.Node);

    public void MoveWorkflow(ClassIslandWorkflowRow workflow, int delta) =>
        Move(root, workflow.Node, delta);

    public ClassIslandTriggerRow AddTrigger(ClassIslandWorkflowRow workflow, ClassIslandAutomationCatalogItem item)
    {
        var node = new JsonObject
        {
            ["Id"] = item.Id,
            ["Settings"] = null
        };
        workflow.TriggersArray.Add(node);
        return new(this, workflow, node, workflow.TriggersArray.Count - 1);
    }

    public void RemoveTrigger(ClassIslandWorkflowRow workflow, ClassIslandTriggerRow trigger) =>
        workflow.TriggersArray.Remove(trigger.Node);

    public void MoveTrigger(ClassIslandWorkflowRow workflow, ClassIslandTriggerRow trigger, int delta) =>
        Move(workflow.TriggersArray, trigger.Node, delta);

    public ClassIslandActionRow AddAction(ClassIslandWorkflowRow workflow, ClassIslandAutomationCatalogItem item)
    {
        var node = new JsonObject
        {
            ["Id"] = item.Id,
            ["Settings"] = null
        };
        workflow.ActionsArray.Add(node);
        return new(this, workflow, node, workflow.ActionsArray.Count - 1);
    }

    public void RemoveAction(ClassIslandWorkflowRow workflow, ClassIslandActionRow action) =>
        workflow.ActionsArray.Remove(action.Node);

    public void MoveAction(ClassIslandWorkflowRow workflow, ClassIslandActionRow action, int delta) =>
        Move(workflow.ActionsArray, action.Node, delta);

    public async Task SaveAsync() =>
        await ClassIslandWorkspace.WriteJsonAtomicAsync(FilePath, root);

    internal static string ReadString(JsonObject node, string key)
    {
        if (node[key] is JsonValue value && value.TryGetValue<string>(out var result))
            return result ?? "";
        return "";
    }

    internal static bool ReadBool(JsonObject node, string key, bool fallback = false)
    {
        if (node[key] is JsonValue value && value.TryGetValue<bool>(out var result))
            return result;
        return fallback;
    }

    internal static int ReadInt(JsonObject node, string key, int fallback = 0)
    {
        if (node[key] is JsonValue value && value.TryGetValue<int>(out var result))
            return result;
        return fallback;
    }

    internal static JsonArray EnsureArray(JsonObject owner, string key)
    {
        if (owner[key] is JsonArray result) return result;
        result = new JsonArray();
        owner[key] = result;
        return result;
    }

    internal static JsonObject EnsureObject(JsonObject owner, string key)
    {
        if (owner[key] is JsonObject result) return result;
        result = new JsonObject();
        owner[key] = result;
        return result;
    }

    private static void Move(JsonArray array, JsonNode node, int delta)
    {
        var index = IndexOf(array, node);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= array.Count) return;
        var moving = array[index];
        array.RemoveAt(index);
        array.Insert(target, moving);
    }

    private static int IndexOf(JsonArray array, JsonNode node)
    {
        for (var i = 0; i < array.Count; i++)
            if (ReferenceEquals(array[i], node)) return i;
        return -1;
    }
}

internal sealed class ClassIslandWorkflowRow
{
    private readonly ClassIslandAutomationDocument owner;
    internal JsonObject Node { get; }

    internal ClassIslandWorkflowRow(ClassIslandAutomationDocument owner, JsonObject node, int index)
    {
        this.owner = owner;
        Node = node;
        Index = index + 1;
    }

    public int Index { get; }

    internal JsonObject ActionSet => ClassIslandAutomationDocument.EnsureObject(Node, "ActionSet");
    internal JsonArray TriggersArray => ClassIslandAutomationDocument.EnsureArray(Node, "Triggers");
    internal JsonArray ActionsArray => ClassIslandAutomationDocument.EnsureArray(ActionSet, "Actions");
    internal JsonObject Ruleset => ClassIslandAutomationDocument.EnsureObject(Node, "Ruleset");

    public string Name
    {
        get => ClassIslandAutomationDocument.ReadString(ActionSet, "Name");
        set => ActionSet["Name"] = value;
    }

    public bool IsEnabled
    {
        get => ClassIslandAutomationDocument.ReadBool(ActionSet, "IsEnabled", true);
        set => ActionSet["IsEnabled"] = value;
    }

    public bool IsRevertEnabled
    {
        get => ClassIslandAutomationDocument.ReadBool(ActionSet, "IsRevertEnabled");
        set => ActionSet["IsRevertEnabled"] = value;
    }

    public bool IsConditionEnabled
    {
        get => ClassIslandAutomationDocument.ReadBool(Node, "IsConditionEnabled");
        set => Node["IsConditionEnabled"] = value;
    }

    public int TriggerCount => TriggersArray.Count;
    public int ActionCount => ActionsArray.Count;

    public IReadOnlyList<ClassIslandTriggerRow> Triggers =>
        TriggersArray.OfType<JsonObject>()
            .Select((node, index) => new ClassIslandTriggerRow(owner, this, node, index))
            .ToArray();

    public IReadOnlyList<ClassIslandActionRow> Actions =>
        ActionsArray.OfType<JsonObject>()
            .Select((node, index) => new ClassIslandActionRow(owner, this, node, index))
            .ToArray();

    public string RulesetJson
    {
        get => Ruleset.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        set => Node["Ruleset"] = ParseObject(value, "Ruleset");
    }

    private static JsonObject ParseObject(string text, string label) =>
        JsonNode.Parse(text) as JsonObject
        ?? throw new JsonException($"{label} 必须是 JSON 对象。");
}

internal abstract class ClassIslandAutomationItemRow
{
    protected ClassIslandAutomationItemRow(JsonObject node, int index)
    {
        Node = node;
        Index = index + 1;
    }

    internal JsonObject Node { get; }
    public int Index { get; }
    public string Id
    {
        get => ClassIslandAutomationDocument.ReadString(Node, "Id");
        set => Node["Id"] = value;
    }
    public string DisplayName => ClassIslandAutomationCatalog.Resolve(Id);

    public string SettingsJson
    {
        get => Node["Settings"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        set => Node["Settings"] = JsonNode.Parse(value);
    }
}

internal sealed class ClassIslandTriggerRow : ClassIslandAutomationItemRow
{
    internal ClassIslandTriggerRow(
        ClassIslandAutomationDocument owner,
        ClassIslandWorkflowRow workflow,
        JsonObject node,
        int index) : base(node, index)
    {
    }
}

internal sealed class ClassIslandActionRow : ClassIslandAutomationItemRow
{
    internal ClassIslandActionRow(
        ClassIslandAutomationDocument owner,
        ClassIslandWorkflowRow workflow,
        JsonObject node,
        int index) : base(node, index)
    {
    }
}
