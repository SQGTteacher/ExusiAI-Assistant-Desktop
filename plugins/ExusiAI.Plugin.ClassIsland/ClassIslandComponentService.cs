using System.IO;
using System.Text.Json;

namespace ExusiAI.Plugin.ClassIsland;

public sealed class ClassIslandComponentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = null, WriteIndented = true
    };
    private readonly SemaphoreSlim gate = new(1, 1);

    public ClassIslandComponentService(string configurationDirectory) =>
        ConfigurationDirectory = Path.GetFullPath(configurationDirectory);

    public string ConfigurationDirectory { get; }
    public string CurrentConfigName { get; private set; } = "Default";
    public ClassIslandComponentProfile CurrentComponents { get; private set; } = CreateDefault();
    public IReadOnlyList<string> ComponentConfigs { get; private set; } = [];
    public event EventHandler? ComponentsChanged;

    public async Task InitializeAsync(string? selectedConfig = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ConfigurationDirectory);
        await RefreshConfigsAsync(cancellationToken).ConfigureAwait(false);
        var selected = ComponentConfigs.FirstOrDefault(x => x.Equals(selectedConfig, StringComparison.OrdinalIgnoreCase));
        await LoadAsync(selected ?? (ComponentConfigs.Contains("Default", StringComparer.OrdinalIgnoreCase) ? "Default" : ComponentConfigs.FirstOrDefault()), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RefreshConfigsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ComponentConfigs = Directory.EnumerateFiles(ConfigurationDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension).Where(x => x is not null).Cast<string>()
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        await Task.CompletedTask;
    }

    public async Task LoadAsync(string? name, CancellationToken cancellationToken = default)
    {
        var configName = SanitizeName(name ?? "Default");
        var path = Path.Combine(ConfigurationDirectory, configName + ".json");
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                CurrentComponents = CreateDefault();
                CurrentConfigName = configName;
                await SaveCoreAsync(path, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await using var stream = File.OpenRead(path);
                CurrentComponents = await JsonSerializer.DeserializeAsync<ClassIslandComponentProfile>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false) ?? new();
                CurrentComponents.Lines ??= [];
                CurrentConfigName = configName;
            }
        }
        catch (JsonException exception) { throw new InvalidDataException("ClassIsland 组件配置 JSON 无法解析。", exception); }
        finally { gate.Release(); }
        await RefreshConfigsAsync(cancellationToken).ConfigureAwait(false);
        ComponentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await SaveCoreAsync(Path.Combine(ConfigurationDirectory, CurrentConfigName + ".json"), cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
        await RefreshConfigsAsync(cancellationToken).ConfigureAwait(false);
        ComponentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default) => await LoadAsync(CurrentConfigName, cancellationToken).ConfigureAwait(false);

    private async Task SaveCoreAsync(string path, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, CurrentComponents, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string SanitizeName(string name)
    {
        var value = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("组件配置名称无效。", nameof(name));
        return value;
    }

    private static ClassIslandComponentProfile CreateDefault() => new()
    {
        Lines =
        {
            new()
            {
                Children =
                {
                    new() { Id = "df3f8295-21f6-482e-bada-fa0e5f14bb66" },
                    new() { Id = "1db2017d-e374-4bc6-9d57-0b4adf03a6b8" }
                }
            }
        }
    };
}
