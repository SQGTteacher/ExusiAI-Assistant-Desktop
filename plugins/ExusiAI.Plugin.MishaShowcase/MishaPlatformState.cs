using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.MishaShowcase;

/// <summary>
/// A direct attachment to an existing ClassIsland data directory. Nothing is generated when the
/// store is constructed; users explicitly select an existing Settings.json or Profile JSON.
/// </summary>
internal sealed class MishaPlatformStore
{
    public ClassIslandWorkspace? Workspace { get; private set; }
    public ClassIslandProfileDocument? Profile { get; private set; }

    public event EventHandler? Changed;

    public async Task AttachWorkspaceAsync(string settingsPath)
    {
        Workspace = await ClassIslandWorkspace.LoadAsync(settingsPath);
        Profile = null;

        var profilePath = Workspace.SelectedProfilePath;
        if (!string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath))
            Profile = await ClassIslandProfileDocument.LoadAsync(profilePath);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task OpenProfileAsync(string profilePath)
    {
        Profile = await ClassIslandProfileDocument.LoadAsync(profilePath);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveProfileAsync()
    {
        if (Profile is null) throw new InvalidOperationException("尚未打开 ClassIsland 档案。");
        await Profile.SaveAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveProfileAsAsync(string destination)
    {
        if (Profile is null) throw new InvalidOperationException("尚未打开 ClassIsland 档案。");
        await Profile.SaveAsAsync(destination);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveWorkspaceSettingsAsync()
    {
        if (Workspace is null) throw new InvalidOperationException("尚未连接 ClassIsland 工作区。");
        await Workspace.SaveSettingsAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public int ResolveRotationWeek(DateTime date) => Workspace?.ResolveRotationWeek(date) ?? 1;
}

internal sealed class ClassIslandWorkspace
{
    private ClassIslandWorkspace(string settingsPath, JsonObject settings)
    {
        SettingsPath = Path.GetFullPath(settingsPath);
        RootDirectory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("Settings.json 路径无效。");
        Settings = settings;
    }

    public string RootDirectory { get; }
    public string SettingsPath { get; }
    public JsonObject Settings { get; private set; }

    public string ProfilesDirectory => Path.Combine(RootDirectory, "Profiles");
    public string ComponentLayoutsDirectory => Path.Combine(RootDirectory, "Config", "ComponentLayouts");
    public string AutomationsDirectory => Path.Combine(RootDirectory, "Config", "Automations");

    public string SelectedProfile => GetString("SelectedProfile");
    public string CurrentComponentConfig => GetString("CurrentComponentConfig");
    public string CurrentAutomationConfig => GetString("CurrentAutomationConfig");

    public string? SelectedProfilePath =>
        ResolveFile(ProfilesDirectory, SelectedProfile, addJsonWhenMissing: false);

    public string? CurrentComponentLayoutPath =>
        ResolveFile(ComponentLayoutsDirectory, CurrentComponentConfig, addJsonWhenMissing: true);

    public string? CurrentAutomationPath =>
        ResolveFile(AutomationsDirectory, CurrentAutomationConfig, addJsonWhenMissing: true);

    public static async Task<ClassIslandWorkspace> LoadAsync(string settingsPath)
    {
        var fullPath = Path.GetFullPath(settingsPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("未找到 ClassIsland Settings.json。", fullPath);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(fullPath))?.AsObject()
            ?? throw new InvalidDataException("ClassIsland Settings.json 根节点无效。");
        return new(fullPath, root);
    }

    public IReadOnlyList<string> EnumerateProfiles() =>
        Directory.Exists(ProfilesDirectory)
            ? Directory.GetFiles(ProfilesDirectory, "*.json").Select(Path.GetFileName).Where(x => x is not null).Cast<string>().Order().ToArray()
            : [];

    public IReadOnlyList<string> EnumerateComponentLayouts() =>
        EnumerateConfigNames(ComponentLayoutsDirectory);

    public IReadOnlyList<string> EnumerateAutomations() =>
        EnumerateConfigNames(AutomationsDirectory);

    public string GetString(string key)
    {
        if (Settings[key] is JsonValue value && value.TryGetValue<string>(out var text))
            return text ?? "";
        return "";
    }

    public bool GetBool(string key, bool fallback = false)
    {
        if (Settings[key] is JsonValue value && value.TryGetValue<bool>(out var result))
            return result;
        return fallback;
    }

    public int GetInt(string key, int fallback = 0)
    {
        if (Settings[key] is JsonValue value)
        {
            if (value.TryGetValue<int>(out var result)) return result;
            if (value.TryGetValue<double>(out var number)) return (int)number;
        }
        return fallback;
    }

    public double GetDouble(string key, double fallback = 0)
    {
        if (Settings[key] is JsonValue value)
        {
            if (value.TryGetValue<double>(out var result)) return result;
            if (value.TryGetValue<int>(out var number)) return number;
        }
        return fallback;
    }

    public void Set(string key, string value) => Settings[key] = value;
    public void Set(string key, bool value) => Settings[key] = value;
    public void Set(string key, int value) => Settings[key] = value;
    public void Set(string key, double value) => Settings[key] = value;

    public int ResolveRotationWeek(DateTime date)
    {
        var maxCycle = Math.Max(1, GetInt("MultiWeekRotationMaxCycle", 1));
        if (maxCycle == 1) return 1;

        DateTime start;
        if (!DateTime.TryParse(GetString("SingleWeekStartTime"), out start))
            start = date.Date.AddDays(-(int)date.DayOfWeek);

        var weeks = (int)Math.Floor((date.Date - start.Date).TotalDays / 7d);
        return ((weeks % maxCycle) + maxCycle) % maxCycle + 1;
    }

    public async Task SaveSettingsAsync()
    {
        await WriteJsonAtomicAsync(SettingsPath, Settings);
    }

    public async Task<JsonNode?> LoadCurrentComponentLayoutAsync()
    {
        var path = CurrentComponentLayoutPath;
        return path is not null && File.Exists(path)
            ? JsonNode.Parse(await File.ReadAllTextAsync(path))
            : null;
    }

    public async Task SaveCurrentComponentLayoutAsync(JsonNode root)
    {
        var path = CurrentComponentLayoutPath
            ?? throw new InvalidOperationException("Settings.json 未指定 CurrentComponentConfig。");
        await WriteJsonAtomicAsync(path, root);
    }

    public async Task<JsonNode?> LoadCurrentAutomationAsync()
    {
        var path = CurrentAutomationPath;
        return path is not null && File.Exists(path)
            ? JsonNode.Parse(await File.ReadAllTextAsync(path))
            : null;
    }

    public async Task SaveCurrentAutomationAsync(JsonNode root)
    {
        var path = CurrentAutomationPath
            ?? throw new InvalidOperationException("Settings.json 未指定 CurrentAutomationConfig。");
        await WriteJsonAtomicAsync(path, root);
    }

    internal static async Task WriteJsonAtomicAsync(string destination, JsonNode root)
    {
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("目标路径无效。");
        Directory.CreateDirectory(directory);

        var temporary = destination + ".tmp";
        var backup = destination + ".bak";
        await File.WriteAllTextAsync(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        if (File.Exists(destination))
            File.Copy(destination, backup, true);
        File.Move(temporary, destination, true);
    }

    private static IReadOnlyList<string> EnumerateConfigNames(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.json").Select(Path.GetFileNameWithoutExtension).Where(x => x is not null).Cast<string>().Order().ToArray()
            : [];

    private static string? ResolveFile(string directory, string name, bool addJsonWhenMissing)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var fileName = addJsonWhenMissing && !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? name + ".json"
            : name;

        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(directory, fileName));
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }
}
