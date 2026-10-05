using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.ClassIsland;

public sealed class ClassIslandComponentProfile : ClassIslandJsonModel
{
    public List<ClassIslandMainWindowLineSettings> Lines { get; set; } = [];
}

public sealed class ClassIslandMainWindowLineSettings : ClassIslandVisualSettings
{
    public List<ClassIslandComponentSettings> Children { get; set; } = [];
    public bool IsMainLine { get; set; }
    public bool IsNotificationEnabled { get; set; } = true;
    public int IslandSeparationMode { get; set; }
}

public sealed class ClassIslandComponentSettings : ClassIslandVisualSettings
{
    private string id = "";
    public string Id { get => id; set => id = value?.ToLowerInvariant() ?? ""; }
    public string NameCache { get; set; } = "";
    public JsonElement? Settings { get; set; }
    public void UpdateSettings(Action<JsonObject> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var values = Settings is { ValueKind: JsonValueKind.Object } existing
            ? JsonNode.Parse(existing.GetRawText())!.AsObject() : new JsonObject();
        update(values);
        Settings = JsonSerializer.SerializeToElement(values);
    }
    public int RelativeLineNumber { get; set; }
    public bool IsMinWidthEnabled { get; set; }
    public double MinWidth { get; set; } = 100;
    public bool IsMaxWidthEnabled { get; set; }
    public double MaxWidth { get; set; } = 300;
    public bool IsFixedWidthEnabled { get; set; }
    public double FixedWidth { get; set; } = 200;
    public int HorizontalAlignment { get; set; } = 3;
    public bool IsCustomMarginEnabled { get; set; }
    public double MarginLeft { get; set; }
    public double MarginTop { get; set; }
    public double MarginRight { get; set; }
    public double MarginBottom { get; set; }
    public double LastWidthCache { get; set; } = 100;
}

public abstract class ClassIslandVisualSettings : ClassIslandJsonModel
{
    public bool IsResourceOverridingEnabled { get; set; }
    public double MainWindowSecondaryFontSize { get; set; } = 14;
    public double MainWindowBodyFontSize { get; set; } = 16;
    public double MainWindowEmphasizedFontSize { get; set; } = 18;
    public double MainWindowLargeFontSize { get; set; } = 20;
    public bool IsCustomForegroundColorEnabled { get; set; }
    public string ForegroundColor { get; set; } = "#1E90FF";
    public double BackgroundOpacity { get; set; } = 0.5;
    public bool IsCustomBackgroundOpacityEnabled { get; set; }
    public string BackgroundColor { get; set; } = "#000000";
    public bool IsCustomBackgroundColorEnabled { get; set; }
    public double CustomCornerRadius { get; set; } = 8;
    public bool IsCustomCornerRadiusEnabled { get; set; }
    public double Opacity { get; set; } = 1;
    public int BackgroundMaterialOverrideMode { get; set; }
    public int BackgroundMaterialType { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool HideOnRule { get; set; }
    public JsonElement HidingRules { get; set; } = JsonSerializer.Deserialize<JsonElement>("{}");
}
