using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.ArkPets;

public sealed record ArkPetModel(
    string Key,
    string Type,
    string Style,
    string Name,
    string Appellation,
    string SkinGroupId,
    string SkinGroupName,
    IReadOnlyList<string> SortTags,
    string AssetDirectory,
    IReadOnlyDictionary<string, IReadOnlyList<string>> AssetFiles)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(SkinGroupName) || string.Equals(SkinGroupName, "默认服装", StringComparison.OrdinalIgnoreCase)
            ? Name
            : $"{Name} · {SkinGroupName}";

    public string Subtitle =>
        string.Join(" · ", new[] { Appellation, Type, Style }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public bool IsAvailable =>
        Directory.Exists(AssetDirectory) &&
        AssetFiles.Values.SelectMany(x => x).All(file => File.Exists(Path.Combine(AssetDirectory, file)));

    public string? PreviewImagePath =>
        AssetFiles.TryGetValue(".png", out var images)
            ? images.Select(file => Path.Combine(AssetDirectory, file)).FirstOrDefault(File.Exists)
            : null;
}

public sealed record ArkModelsCatalog(
    string RootDirectory,
    string GameDataVersionDescription,
    string GameDataServerRegion,
    string Compatibility,
    IReadOnlyDictionary<string, string> SortTags,
    IReadOnlyList<ArkPetModel> Models)
{
    public static ArkModelsCatalog Empty(string root = "") =>
        new(root, "", "", "", new Dictionary<string, string>(), Array.Empty<ArkPetModel>());
}

public static class ArkModelsDataset
{
    public static async Task<ArkModelsCatalog> LoadAsync(string rootDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = DiscoverRoot(rootDirectory);
        var datasetPath = Path.Combine(root, "models_data.json");
        if (!File.Exists(datasetPath))
            throw new FileNotFoundException("所选目录中没有 Ark-Models 的 models_data.json。", datasetPath);

        await using var stream = File.OpenRead(datasetPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var json = document.RootElement;
        if (json.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Ark-Models models_data.json 根节点无效。");

        var storage = ReadStringMap(json, "storageDirectory");
        if (storage.Count == 0)
            throw new InvalidDataException("Ark-Models 数据集缺少 storageDirectory。");

        var sortTags = ReadStringMap(json, "sortTags");
        var models = new List<ArkPetModel>();
        if (!json.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Ark-Models 数据集缺少 data。");

        foreach (var item in data.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Value.ValueKind != JsonValueKind.Object) continue;
            var node = item.Value;
            var type = ReadString(node, "type");
            if (string.IsNullOrWhiteSpace(type) || !storage.TryGetValue(type, out var storageRelative))
                continue;

            var assetDirectory = ResolveUnderRoot(root, Path.Combine(storageRelative, item.Name));
            var assetFiles = ReadAssetFiles(node);
            if (assetFiles.Count == 0)
            {
                var assetId = ReadString(node, "assetId");
                if (!string.IsNullOrWhiteSpace(assetId))
                {
                    assetFiles = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        [".atlas"] = [$"{assetId}.atlas"],
                        [".png"] = [$"{assetId}.png"],
                        [".skel"] = [$"{assetId}.skel"]
                    };
                }
            }

            models.Add(new(
                item.Name,
                type,
                ReadString(node, "style"),
                ReadString(node, "name", item.Name),
                ReadString(node, "appellation"),
                ReadString(node, "skinGroupId"),
                ReadString(node, "skinGroupName"),
                ReadStringArray(node, "sortTags"),
                assetDirectory,
                assetFiles));
        }

        var compatibility = json.TryGetProperty("arkPetsCompatibility", out var compatibilityNode) &&
                            compatibilityNode.ValueKind == JsonValueKind.Array
            ? string.Join(".", compatibilityNode.EnumerateArray().Select(x => x.GetInt32()))
            : "";

        return new(
            root,
            ReadString(json, "gameDataVersionDescription"),
            ReadString(json, "gameDataServerRegion"),
            compatibility,
            sortTags,
            models.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.SkinGroupName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    internal static string DiscoverRoot(string selectedPath)
    {
        var selected = Path.GetFullPath(selectedPath);
        if (File.Exists(selected))
        {
            if (string.Equals(Path.GetFileName(selected), "models_data.json", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(selected)!;
            throw new FileNotFoundException("所选文件不是 Ark-Models 的 models_data.json。", selected);
        }

        if (!Directory.Exists(selected))
            throw new DirectoryNotFoundException($"模型库目录不存在：{selected}");

        if (File.Exists(Path.Combine(selected, "models_data.json")))
            return selected;

        var conventional = Path.Combine(selected, "ArkModels");
        if (File.Exists(Path.Combine(conventional, "models_data.json")))
            return conventional;

        var candidates = Directory.EnumerateDirectories(selected)
            .Where(directory => File.Exists(Path.Combine(directory, "models_data.json")))
            .Take(2)
            .ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            > 1 => throw new InvalidDataException("所选目录中包含多个 Ark-Models 模型库，请选择具体模型库目录。"),
            _ => throw new FileNotFoundException("所选目录及其直接子目录中没有 Ark-Models 的 models_data.json。", Path.Combine(selected, "models_data.json"))
        };
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement parent, string property)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!parent.TryGetProperty(property, out var node) || node.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var item in node.EnumerateObject())
            if (item.Value.ValueKind == JsonValueKind.String)
                result[item.Name] = item.Value.GetString() ?? "";
        return result;
    }

    private static Dictionary<string, IReadOnlyList<string>> ReadAssetFiles(JsonElement node)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (!node.TryGetProperty("assetList", out var list) || list.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var item in list.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String)
            {
                var value = item.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value) && IsSafeRelativeFile(value))
                    result[item.Name] = [value];
            }
            else if (item.Value.ValueKind == JsonValueKind.Array)
            {
                var values = item.Value.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x) && IsSafeRelativeFile(x!))
                    .Cast<string>()
                    .ToArray();
                if (values.Length > 0) result[item.Name] = values;
            }
        }
        return result;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.Array
            ? node.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToArray()
            : Array.Empty<string>();

    private static string ReadString(JsonElement parent, string property, string fallback = "") =>
        parent.TryGetProperty(property, out var node) && node.ValueKind == JsonValueKind.String
            ? node.GetString() ?? fallback
            : fallback;

    private static bool IsSafeRelativeFile(string path) =>
        !Path.IsPathRooted(path) &&
        !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part == "..");

    private static string ResolveUnderRoot(string root, string relative)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException("Ark-Models 数据集包含绝对资源路径。");

        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relative));
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ark-Models 数据集包含越界资源路径。");
        return candidate;
    }
}

public sealed record ArkPetsSettings
{
    public int SchemaVersion { get; init; } = 3;
    public string RuntimePath { get; init; } = "";
    public string ModelRoot { get; init; } = "";
    public string SelectedModelKey { get; init; } = "";
    public string[] FavoriteModelKeys { get; init; } = Array.Empty<string>();
    public string RuntimeVersion { get; init; } = "";
    public string NetworkProxy { get; init; } = "";
    public bool AutoStartPetWithExusiAI { get; init; }
    public bool StartExusiAIWithWindows { get; init; }

    public int BehaviorAiActivation { get; init; } = 4;
    public bool BehaviorAllowInteract { get; init; } = true;
    public bool BehaviorAllowSit { get; init; } = true;
    public bool BehaviorAllowSleep { get; init; }
    public bool BehaviorAllowSpecial { get; init; } = true;
    public bool BehaviorAllowWalk { get; init; } = true;
    public int BehaviorDirectionSwitching { get; init; } = 1;
    public bool BehaviorDoPeerRepulsion { get; init; } = true;
    public double BehaviorWalkSpeed { get; init; } = 30;

    public double PhysicGravityAcc { get; init; } = 800;
    public double PhysicAirFrictionAcc { get; init; } = 100;
    public double PhysicStaticFrictionAcc { get; init; } = 500;
    public double PhysicSpeedLimitX { get; init; } = 1000;
    public double PhysicSpeedLimitY { get; init; } = 1000;

    public int DisplayFps { get; init; } = 60;
    public int DisplayMarginBottom { get; init; }
    public bool DisplayMultiMonitors { get; init; } = true;
    public double DisplayScale { get; init; } = 1;
    public double InitialPositionX { get; init; } = 0.2;
    public double InitialPositionY { get; init; } = 0.2;
    public double OpacityDim { get; init; } = 0.75;
    public double OpacityNormal { get; init; } = 1;
    public string CanvasColor { get; init; } = "#00000000";
    public double CanvasCoverage { get; init; } = 0.8;
    public int CanvasSamplingInterval { get; init; } = 4;

    public double RenderAnimationMixture { get; init; } = 0.3;
    public bool RenderEnableMipmap { get; init; } = true;
    public int RenderOutline { get; init; } = 1;
    public int RenderOutlineEmphasis { get; init; } = 3;
    public double RenderOutlineWidth { get; init; } = 2;
    public bool RenderShaderHighQuality { get; init; } = true;
    public string RenderOutlineColor { get; init; } = "#FFFF00FF";
    public string RenderOutlineEmphasisColor { get; init; } = "#FFBB00FF";
    public string RenderShadowColor { get; init; } = "#000000BB";
    public double TransitionDuration { get; init; } = 0.3;
    public string TransitionType { get; init; } = "EASE_OUT_CUBIC";

    public bool EcoMode { get; init; }
    public bool LauncherSolidExit { get; init; } = true;
    public string LoggingLevel { get; init; } = "INFO";
    public bool WindowStyleToolwindow { get; init; } = true;
    public bool WindowStyleTopmost { get; init; } = true;

    public bool ClassIslandRemindersEnabled { get; init; }
    public bool OrganizeDesktopDuringBreaks { get; init; }
}

internal sealed class ArkPetsSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string path;

    public ArkPetsSettingsStore(string? path = null)
    {
        this.path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExusiAI", "arkpets", "settings.json");
    }

    public async Task<ArkPetsSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new();
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ArkPetsSettings>(stream, Options, cancellationToken) ?? new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public async Task SaveAsync(ArkPetsSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
        File.Move(temporary, path, true);
    }
}
