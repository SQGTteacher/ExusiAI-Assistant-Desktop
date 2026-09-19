using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed record ClassIslandComponentCatalogItem(string Id, string Name, string Description);

internal static class ClassIslandComponentCatalog
{
    public static IReadOnlyList<ClassIslandComponentCatalogItem> BuiltIns { get; } =
    [
        new("df3f8295-21f6-482e-bada-fa0e5f14bb66", "日期", "显示今天的日期和星期"),
        new("1db2017d-e374-4bc6-9d57-0b4adf03a6b8", "课程表", "显示当前课程表信息"),
        new("9e1af71d-8f77-4b21-a342-448787104dd9", "时钟", "显示当前时间"),
        new("ca495086-e297-4beb-9603-c5c1c1a8551e", "天气简报", "天气概况和气象预警"),
        new("7c645d35-8151-48ba-b4ac-15017460d994", "倒计时", "距离目标日期的倒计时"),
        new("ee8f66bd-c423-4e7c-ab46-aa9976b00e08", "文本", "显示自定义文本"),
        new("ab0f26d5-9df6-4575-b844-73b04d0907c1", "分割线", "组件视觉分隔"),
        new("c911d762-107f-40c6-84cc-0146ab3c86b1", "分组容器", "组合多个组件"),
        new("2d849ece-9f21-4c78-9434-415cfc283294", "堆叠容器", "堆叠多个组件"),
        new("7e19a113-d281-4f33-970a-834a0b78b5ad", "轮播容器", "轮播多个组件"),
        new("70fcd5ea-3fae-4e06-aca2-4f4df47f9acd", "滚动容器", "滚动显示组件内容")
    ];

    public static string ResolveName(string id, string fallback)
    {
        var known = BuiltIns.FirstOrDefault(x => SameId(x.Id, id));
        return known?.Name ?? (!string.IsNullOrWhiteSpace(fallback) ? fallback : id);
    }

    private static bool SameId(string left, string right) =>
        Guid.TryParse(left, out var a) && Guid.TryParse(right, out var b)
            ? a == b
            : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

internal sealed class ClassIslandComponentLayoutDocument
{
    private readonly JsonObject root;

    private ClassIslandComponentLayoutDocument(string filePath, JsonObject root)
    {
        FilePath = Path.GetFullPath(filePath);
        this.root = root;
        EnsureLines();
    }

    public string FilePath { get; }
    public JsonObject Root => root;

    public IReadOnlyList<ClassIslandComponentLineRow> Lines =>
        EnsureLines().OfType<JsonObject>()
            .Select((node, index) => new ClassIslandComponentLineRow(this, node, index))
            .ToArray();

    public static async Task<ClassIslandComponentLayoutDocument> LoadAsync(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        if (!File.Exists(full))
            throw new FileNotFoundException("ClassIsland 组件配置不存在。", full);

        var node = JsonNode.Parse(await File.ReadAllTextAsync(full))
            ?? throw new InvalidDataException("组件配置 JSON 为空。");
        if (node is not JsonObject root)
            throw new InvalidDataException("ClassIsland ComponentProfile 根节点必须是对象。");
        if (root["Lines"] is not null && root["Lines"] is not JsonArray)
            throw new InvalidDataException("ComponentProfile.Lines 不是数组。");

        return new(full, root);
    }

    public ClassIslandComponentLineRow AddLine()
    {
        var node = new JsonObject
        {
            ["Children"] = new JsonArray(),
            ["IsMainLine"] = false,
            ["IsNotificationEnabled"] = true
        };
        var lines = EnsureLines();
        lines.Add(node);
        return new(this, node, lines.Count - 1);
    }

    public void RemoveLine(ClassIslandComponentLineRow line)
    {
        EnsureLines().Remove(line.Node);
    }

    public ClassIslandComponentRow AddComponent(ClassIslandComponentLineRow line, ClassIslandComponentCatalogItem component)
    {
        var node = new JsonObject
        {
            ["Id"] = component.Id,
            ["NameCache"] = component.Name,
            ["Settings"] = null
        };
        line.ChildrenArray.Add(node);
        return new(this, line, node, line.ChildrenArray.Count - 1);
    }

    public void RemoveComponent(ClassIslandComponentLineRow line, ClassIslandComponentRow component)
    {
        line.ChildrenArray.Remove(component.Node);
    }

    public void MoveLine(ClassIslandComponentLineRow line, int delta)
    {
        var lines = EnsureLines();
        var index = IndexOf(lines, line.Node);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= lines.Count) return;
        var node = lines[index];
        lines.RemoveAt(index);
        lines.Insert(target, node);
    }

    public void MoveComponent(ClassIslandComponentLineRow line, ClassIslandComponentRow component, int delta)
    {
        var children = line.ChildrenArray;
        var index = IndexOf(children, component.Node);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= children.Count) return;
        var node = children[index];
        children.RemoveAt(index);
        children.Insert(target, node);
    }

    public async Task SaveAsync() =>
        await ClassIslandWorkspace.WriteJsonAtomicAsync(FilePath, root);

    internal static string ReadString(JsonObject node, string key)
    {
        if (node[key] is JsonValue value && value.TryGetValue<string>(out var text))
            return text ?? "";
        return "";
    }

    internal static bool ReadBool(JsonObject node, string key, bool fallback = false)
    {
        if (node[key] is JsonValue value && value.TryGetValue<bool>(out var result))
            return result;
        return fallback;
    }

    internal static double ReadDouble(JsonObject node, string key, double fallback = 0)
    {
        if (node[key] is JsonValue value)
        {
            if (value.TryGetValue<double>(out var result)) return result;
            if (value.TryGetValue<int>(out var integer)) return integer;
        }
        return fallback;
    }

    internal static int ReadInt(JsonObject node, string key, int fallback = 0)
    {
        if (node[key] is JsonValue value && value.TryGetValue<int>(out var result))
            return result;
        return fallback;
    }

    private JsonArray EnsureLines()
    {
        if (root["Lines"] is JsonArray lines) return lines;
        lines = new JsonArray();
        root["Lines"] = lines;
        return lines;
    }

    private static int IndexOf(JsonArray array, JsonNode target)
    {
        for (var i = 0; i < array.Count; i++)
            if (ReferenceEquals(array[i], target)) return i;
        return -1;
    }
}

internal sealed class ClassIslandComponentLineRow
{
    private readonly ClassIslandComponentLayoutDocument owner;
    internal JsonObject Node { get; }

    internal ClassIslandComponentLineRow(ClassIslandComponentLayoutDocument owner, JsonObject node, int index)
    {
        this.owner = owner;
        Node = node;
        Index = index + 1;
    }

    public int Index { get; }
    public bool IsMainLine { get => ClassIslandComponentLayoutDocument.ReadBool(Node, "IsMainLine"); set => Node["IsMainLine"] = value; }
    public bool IsNotificationEnabled { get => ClassIslandComponentLayoutDocument.ReadBool(Node, "IsNotificationEnabled", true); set => Node["IsNotificationEnabled"] = value; }
    public bool IsResourceOverridingEnabled { get => ClassIslandComponentLayoutDocument.ReadBool(Node, "IsResourceOverridingEnabled"); set => Node["IsResourceOverridingEnabled"] = value; }
    public double BackgroundOpacity { get => ClassIslandComponentLayoutDocument.ReadDouble(Node, "BackgroundOpacity", 0.5); set => Node["BackgroundOpacity"] = value; }
    public bool IsCustomBackgroundOpacityEnabled { get => ClassIslandComponentLayoutDocument.ReadBool(Node, "IsCustomBackgroundOpacityEnabled"); set => Node["IsCustomBackgroundOpacityEnabled"] = value; }
    public double Opacity { get => ClassIslandComponentLayoutDocument.ReadDouble(Node, "Opacity", 1); set => Node["Opacity"] = value; }
    public int BackgroundMaterialOverrideMode { get => ClassIslandComponentLayoutDocument.ReadInt(Node, "BackgroundMaterialOverrideMode"); set => Node["BackgroundMaterialOverrideMode"] = value; }
    public bool HideOnRule { get => ClassIslandComponentLayoutDocument.ReadBool(Node, "HideOnRule"); set => Node["HideOnRule"] = value; }
    public int ComponentCount => ChildrenArray.Count;

    internal JsonArray ChildrenArray
    {
        get
        {
            if (Node["Children"] is JsonArray array) return array;
            array = new JsonArray();
            Node["Children"] = array;
            return array;
        }
    }

    public IReadOnlyList<ClassIslandComponentRow> Components =>
        ChildrenArray.OfType<JsonObject>()
            .Select((node, index) => new ClassIslandComponentRow(owner, this, node, index))
            .ToArray();
}

internal sealed class ClassIslandComponentRow
{
    internal JsonObject Node { get; }

    internal ClassIslandComponentRow(
        ClassIslandComponentLayoutDocument owner,
        ClassIslandComponentLineRow line,
        JsonObject node,
        int index)
    {
        Node = node;
        Index = index + 1;
    }

    public int Index { get; }
    public string Id { get => ClassIslandComponentLayoutDocument.ReadString(Node, "Id"); set => Node["Id"] = value.ToLowerInvariant(); }
    public string NameCache { get => ClassIslandComponentLayoutDocument.ReadString(Node, "NameCache"); set => Node["NameCache"] = value; }
    public string DisplayName => ClassIslandComponentCatalog.ResolveName(Id, NameCache);
    public bool HideOnRule { get => ClassIslandComponentLayoutDocument.ReadBool(Node, "HideOnRule"); set => Node["HideOnRule"] = value; }
    public int RelativeLineNumber { get => ClassIslandComponentLayoutDocument.ReadInt(Node, "RelativeLineNumber"); set => Node["RelativeLineNumber"] = value; }
    public bool IsMinWidthEnabled { get => ClassIslandComponentLayoutDocument.ReadBool(Node, "IsMinWidthEnabled"); set => Node["IsMinWidthEnabled"] = value; }
    public double MinWidth { get => ClassIslandComponentLayoutDocument.ReadDouble(Node, "MinWidth", 100); set => Node["MinWidth"] = value; }
    public bool IsMaxWidthEnabled { get => ClassIslandComponentLayoutDocument.ReadBool(Node, "IsMaxWidthEnabled"); set => Node["IsMaxWidthEnabled"] = value; }
    public double MaxWidth { get => ClassIslandComponentLayoutDocument.ReadDouble(Node, "MaxWidth", 300); set => Node["MaxWidth"] = value; }
    public bool IsFixedWidthEnabled { get => ClassIslandComponentLayoutDocument.ReadBool(Node, "IsFixedWidthEnabled"); set => Node["IsFixedWidthEnabled"] = value; }
    public double FixedWidth { get => ClassIslandComponentLayoutDocument.ReadDouble(Node, "FixedWidth", 200); set => Node["FixedWidth"] = value; }
    public double Opacity { get => ClassIslandComponentLayoutDocument.ReadDouble(Node, "Opacity", 1); set => Node["Opacity"] = value; }
    public bool IsCustomMarginEnabled { get => ClassIslandComponentLayoutDocument.ReadBool(Node, "IsCustomMarginEnabled"); set => Node["IsCustomMarginEnabled"] = value; }
    public double MarginLeft { get => ClassIslandComponentLayoutDocument.ReadDouble(Node, "MarginLeft"); set => Node["MarginLeft"] = value; }
    public double MarginTop { get => ClassIslandComponentLayoutDocument.ReadDouble(Node, "MarginTop"); set => Node["MarginTop"] = value; }
    public double MarginRight { get => ClassIslandComponentLayoutDocument.ReadDouble(Node, "MarginRight"); set => Node["MarginRight"] = value; }
    public double MarginBottom { get => ClassIslandComponentLayoutDocument.ReadDouble(Node, "MarginBottom"); set => Node["MarginBottom"] = value; }

    public string SettingsJson
    {
        get => Node["Settings"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        set
        {
            var parsed = JsonNode.Parse(value);
            Node["Settings"] = parsed;
        }
    }
}
