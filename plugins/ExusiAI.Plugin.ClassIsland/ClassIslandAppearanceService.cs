using System.IO;
using System.Text.Json;

namespace ExusiAI.Plugin.ClassIsland;

public enum ClassIslandDockPosition { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight }
public enum ClassIslandIslandTheme { DarkGlass, LightGlass }

public sealed class ClassIslandAppearanceSettings
{
    public ClassIslandDockPosition DockPosition { get; set; } = ClassIslandDockPosition.TopCenter;
    public double Width { get; set; } = 440;
    public double Height { get; set; } = 52;
    public double Scale { get; set; } = 1;
    public double Opacity { get; set; } = 1;
    public double CornerRadius { get; set; } = 26;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; } = 12;
    public bool Topmost { get; set; } = true;
    public bool ShowSeconds { get; set; }
    public bool FadeOnPointerEnter { get; set; } = true;
    public double HoverOpacity { get; set; } = 0.25;
    public ClassIslandIslandTheme IslandTheme { get; set; } = ClassIslandIslandTheme.DarkGlass;
    public string? MonitorDeviceName { get; set; }
}

public sealed class ClassIslandAppearanceService
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly string path;
    private readonly string legacyPath;
    public ClassIslandAppearanceService(string dataDirectory)
    {
        path = Path.Combine(dataDirectory, "IslandAppearance.json");
        legacyPath = Path.Combine(dataDirectory, "Config", "Appearance.json");
    }
    public ClassIslandAppearanceSettings Settings { get; private set; } = new();
    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var source = File.Exists(path) ? path : legacyPath;
        if (!File.Exists(source)) return;
        var json = await File.ReadAllTextAsync(source, cancellationToken).ConfigureAwait(false);
        Settings = JsonSerializer.Deserialize<ClassIslandAppearanceSettings>(json, Options) ?? new();
        using var document = JsonDocument.Parse(json);
        // The previous preset used a 620×72 island; bring untouched presets to
        // the compact default while leaving explicitly customized dimensions alone.
        if (!document.RootElement.TryGetProperty(nameof(ClassIslandAppearanceSettings.IslandTheme), out _) &&
            Settings.Width == 620 && Settings.Height == 72)
        {
            Settings.Width = 440;
            Settings.Height = 52;
            Settings.CornerRadius = 26;
        }
        Normalize();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, Settings, Options, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyChanged() { Normalize(); Changed?.Invoke(this, EventArgs.Empty); }

    private void Normalize()
    {
        Settings.Width = Math.Clamp(Settings.Width, 240, 1600);
        Settings.Height = Math.Clamp(Settings.Height, 40, 180);
        Settings.Scale = Math.Clamp(Settings.Scale, 0.6, 2);
        Settings.Opacity = Math.Clamp(Settings.Opacity, 0.25, 1);
        Settings.HoverOpacity = Math.Clamp(Settings.HoverOpacity, 0.05, Settings.Opacity);
        Settings.CornerRadius = Math.Clamp(Settings.CornerRadius, 0, Settings.Height / 2);
    }
}
