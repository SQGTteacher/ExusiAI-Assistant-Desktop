using System.IO;
using System.Text.Json;

namespace ExusiAI.Plugin.ClassIsland;

public enum ClassIslandDockPosition { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight }

public sealed class ClassIslandAppearanceSettings
{
    public ClassIslandDockPosition DockPosition { get; set; } = ClassIslandDockPosition.TopCenter;
    public double Width { get; set; } = 620;
    public double Height { get; set; } = 72;
    public double Scale { get; set; } = 1;
    public double Opacity { get; set; } = 0.96;
    public double CornerRadius { get; set; } = 28;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; } = 12;
    public bool Topmost { get; set; } = true;
    public bool ShowSeconds { get; set; }
}

public sealed class ClassIslandAppearanceService
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly string path;
    public ClassIslandAppearanceService(string dataDirectory) => path = Path.Combine(dataDirectory, "Config", "Appearance.json");
    public ClassIslandAppearanceSettings Settings { get; private set; } = new();
    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return;
        await using var stream = File.OpenRead(path);
        Settings = await JsonSerializer.DeserializeAsync<ClassIslandAppearanceSettings>(stream, Options, cancellationToken).ConfigureAwait(false) ?? new();
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
        Settings.Width = Math.Clamp(Settings.Width, 320, 1600);
        Settings.Height = Math.Clamp(Settings.Height, 48, 180);
        Settings.Scale = Math.Clamp(Settings.Scale, 0.6, 2);
        Settings.Opacity = Math.Clamp(Settings.Opacity, 0.25, 1);
        Settings.CornerRadius = Math.Clamp(Settings.CornerRadius, 0, Settings.Height / 2);
    }
}
