using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.MishaShowcase;

/// <summary>
/// A direct attachment to an existing ClassIsland data directory. Nothing is generated when the
/// store is constructed; users explicitly select an existing Settings.json or Profile JSON.
/// </summary>
internal sealed class MishaPlatformStore
{
    private const string StateFileName = "workspace-state.json";
    private readonly string storageRoot;

    public MishaPlatformStore(string? storageRoot = null)
    {
        this.storageRoot = Path.GetFullPath(storageRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExusiAI",
            "misha"));
    }

    public ClassIslandWorkspace? Workspace { get; private set; }
    public ClassIslandProfileDocument? Profile { get; private set; }
    public ClassIslandThemeSnapshot ThemeSnapshot { get; private set; } = ClassIslandThemeSnapshot.Empty;
    public string? SourceRootDirectory { get; private set; }
    public string StorageRoot => storageRoot;

    public event EventHandler? Changed;

    public async Task<bool> RestoreLastWorkspaceAsync()
    {
        var statePath = Path.Combine(storageRoot, StateFileName);
        if (!File.Exists(statePath)) return false;

        try
        {
            var state = JsonNode.Parse(await File.ReadAllTextAsync(statePath)) as JsonObject;
            var settingsPath = state?["SettingsPath"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
                return false;

            await LoadImportedWorkspaceAsync(settingsPath, state?["SourceRootDirectory"]?.GetValue<string>());
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // A stale/corrupt binding must never prevent the plugin or host from starting.
            return false;
        }
    }

    public async Task AttachWorkspaceAsync(string settingsPath)
    {
        var imported = await ClassIslandWorkspaceImporter.ImportAsync(settingsPath, Path.Combine(storageRoot, "workspaces"));
        await LoadImportedWorkspaceAsync(imported.SettingsPath, imported.SourceRootDirectory);
        await PersistWorkspaceBindingAsync();
    }

    public async Task<ClassIslandBackupSummary> SyncBackupAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        var imported = await ClassIslandBackupImporter.ImportAsync(
            archivePath,
            Path.Combine(storageRoot, "workspaces"),
            cancellationToken);
        await LoadImportedWorkspaceAsync(imported.SettingsPath, imported.SourceArchivePath);
        await PersistWorkspaceBindingAsync();
        return imported.Summary;
    }

    public async Task OpenProfileAsync(string profilePath)
    {
        var imported = await ClassIslandWorkspaceImporter.ImportStandaloneProfileAsync(
            profilePath,
            Path.Combine(storageRoot, "profiles"));
        SourceRootDirectory ??= Path.GetDirectoryName(Path.GetFullPath(profilePath));
        Profile = await ClassIslandProfileDocument.LoadAsync(imported);
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
        await ClassIslandWorkspace.WriteJsonAtomicAsync(destination, Profile.Root);
    }

    public async Task SaveWorkspaceSettingsAsync()
    {
        if (Workspace is null) throw new InvalidOperationException("尚未连接 ClassIsland 工作区。");
        await Workspace.SaveSettingsAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task ReloadThemesAsync()
    {
        if (Workspace is null)
            throw new InvalidOperationException("尚未连接 ClassIsland 工作区。");
        ThemeSnapshot = ClassIslandThemeCompatibilityLayer.Load(Workspace);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async Task UpdateEnabledThemesAsync(IReadOnlyList<string> themeIds)
    {
        if (Workspace is null)
            throw new InvalidOperationException("尚未连接 ClassIsland 工作区。");

        var normalized = themeIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var array = new JsonArray(normalized.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        await ClassIslandWorkspace.WriteJsonAtomicAsync(Workspace.EnabledThemesPath, array);
        ThemeSnapshot = ClassIslandThemeCompatibilityLayer.Load(Workspace);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public int ResolveRotationWeek(DateTime date) => Workspace?.ResolveRotationWeek(date) ?? 1;

    private async Task LoadImportedWorkspaceAsync(string settingsPath, string? sourceRootDirectory)
    {
        var workspace = await ClassIslandWorkspace.LoadAsync(settingsPath);
        ClassIslandProfileDocument? profile = null;
        var profilePath = workspace.SelectedProfilePath;
        if (profilePath is not null && File.Exists(profilePath))
            profile = await ClassIslandProfileDocument.LoadAsync(profilePath);

        SourceRootDirectory = sourceRootDirectory;
        Workspace = workspace;
        Profile = profile;
        ThemeSnapshot = ClassIslandThemeCompatibilityLayer.Load(workspace);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task PersistWorkspaceBindingAsync()
    {
        if (Workspace is null) return;
        Directory.CreateDirectory(storageRoot);
        var state = new JsonObject
        {
            ["SettingsPath"] = Workspace.SettingsPath,
            ["SourceRootDirectory"] = SourceRootDirectory,
            ["SchemaVersion"] = 1
        };
        await ClassIslandWorkspace.WriteJsonAtomicAsync(Path.Combine(storageRoot, StateFileName), state);
    }
}

internal sealed record ImportedClassIslandWorkspace(string SettingsPath, string SourceRootDirectory);

internal static class ClassIslandWorkspaceImporter
{
    public static async Task<ImportedClassIslandWorkspace> ImportAsync(string settingsPath, string destinationRoot)
    {
        var sourceSettings = Path.GetFullPath(settingsPath);
        if (!File.Exists(sourceSettings))
            throw new FileNotFoundException("未找到 ClassIsland Settings.json。", sourceSettings);

        var sourceRoot = Path.GetDirectoryName(sourceSettings)
            ?? throw new InvalidOperationException("Settings.json 路径无效。");

        Directory.CreateDirectory(destinationRoot);
        if (IsUnderRoot(sourceSettings, destinationRoot))
            return new(sourceSettings, ReadSourceRoot(Path.GetDirectoryName(sourceSettings)!) ?? sourceRoot);

        var workspaceName = SanitizeName(Path.GetFileName(sourceRoot));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceRoot.ToUpperInvariant())))[..12];
        var destination = Path.Combine(destinationRoot, $"{workspaceName}-{hash}-{DateTime.UtcNow:yyyyMMddHHmmssfff}");
        Directory.CreateDirectory(destination);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, sourceFile);
            if (relative.StartsWith("..", StringComparison.Ordinal))
                continue;

            var normalized = relative.Replace(Path.DirectorySeparatorChar, '/');
            var preserve =
                normalized.Equals("Settings.json", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Profiles/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Config/", StringComparison.OrdinalIgnoreCase);
            if (!preserve)
                continue;

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(sourceFile, target, false);
        }

        var settingsRelative = Path.GetRelativePath(sourceRoot, sourceSettings);
        var importedSettings = Path.Combine(destination, settingsRelative);
        if (!File.Exists(importedSettings))
            throw new InvalidOperationException("导入后未找到 Settings.json；ClassIsland JSON 工作区复制不完整。");

        await File.WriteAllTextAsync(
            Path.Combine(destination, ".exusiai-source.txt"),
            $"Source={sourceRoot}{Environment.NewLine}Imported={DateTimeOffset.Now:O}{Environment.NewLine}",
            Encoding.UTF8);

        return new(importedSettings, sourceRoot);
    }

    public static Task<string> ImportStandaloneProfileAsync(string profilePath, string destinationRoot)
    {
        var source = Path.GetFullPath(profilePath);
        if (!File.Exists(source))
            throw new FileNotFoundException("未找到 ClassIsland Profile JSON。", source);

        Directory.CreateDirectory(destinationRoot);
        if (IsUnderRoot(source, destinationRoot))
            return Task.FromResult(source);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToUpperInvariant())))[..12];
        var fileName = Path.GetFileNameWithoutExtension(source);
        var destination = Path.Combine(destinationRoot, $"{SanitizeName(fileName)}-{hash}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
        File.Copy(source, destination, false);
        return Task.FromResult(destination);
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadSourceRoot(string workspaceDirectory)
    {
        var sourceMarker = Path.Combine(workspaceDirectory, ".exusiai-source.txt");
        if (!File.Exists(sourceMarker)) return null;
        var line = File.ReadLines(sourceMarker).FirstOrDefault(x => x.StartsWith("Source=", StringComparison.Ordinal));
        return line is null ? null : line["Source=".Length..];
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var value = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(value) ? "ClassIsland" : value;
    }
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
    public string ConfigDirectory => Path.Combine(RootDirectory, "Config");
    public string ComponentLayoutsDirectory => Path.Combine(ConfigDirectory, "ComponentLayouts");
    public string AutomationsDirectory => Path.Combine(ConfigDirectory, "Automations");
    public string ThemesDirectory => Path.Combine(ConfigDirectory, "Themes");
    public string EnabledThemesPath => Path.Combine(ConfigDirectory, "EnabledThemes.json");

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
