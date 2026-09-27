using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.ClassIsland;

public sealed class ClassIslandComponentService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, JsonNode?> components = new(StringComparer.OrdinalIgnoreCase);

    public ClassIslandComponentService(string configurationDirectory) =>
        ConfigurationDirectory = Path.GetFullPath(configurationDirectory);

    public string ConfigurationDirectory { get; }
    public IReadOnlyDictionary<string, JsonNode?> Components => components;
    public event EventHandler? ComponentsChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ConfigurationDirectory);
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            components.Clear();
            foreach (var path in Directory.EnumerateFiles(ConfigurationDirectory, "*.json", SearchOption.AllDirectories))
            {
                await using var stream = File.OpenRead(path);
                components[Path.GetRelativePath(ConfigurationDirectory, path)] = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally { gate.Release(); }
        ComponentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveAsync(string relativePath, JsonNode? value, CancellationToken cancellationToken = default)
    {
        var target = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllTextAsync(temporary, value?.ToJsonString(new() { WriteIndented = true }) ?? "null", cancellationToken)
                    .ConfigureAwait(false);
                File.Move(temporary, target, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            components[Path.GetRelativePath(ConfigurationDirectory, target)] = value?.DeepClone();
        }
        finally { gate.Release(); }
        ComponentsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string Resolve(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new ArgumentException("组件配置路径必须是相对路径。", nameof(relativePath));
        var root = ConfigurationDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("组件配置路径越界。");
        return target;
    }
}
