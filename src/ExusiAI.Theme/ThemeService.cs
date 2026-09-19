namespace ExusiAI.Theme;

public sealed record ThemePalette(
    string Background,
    string Surface,
    string SurfaceAlternative,
    string TextPrimary,
    string TextSecondary,
    string Border,
    string Accent,
    string AccentSoft,
    string Success,
    string Warning,
    string Danger);

public sealed record ThemeDefinition(
    string Id,
    string Name,
    string Description,
    bool IsDark,
    ThemePalette Palette,
    ThemePalette? DarkPalette = null)
{
    public bool FollowsSystem => DarkPalette is not null;
}

public static class ThemeCatalog
{
    public static ThemePalette Light { get; } = new("#F4F7FA", "#FFFFFF", "#EAF0F6", "#17212E", "#65758A", "#CAD5E1", "#5CA9C9", "#DCEFF6", "#159A73", "#D9913D", "#D95A70");
    public static ThemePalette Dark { get; } = new("#10141C", "#171D28", "#202937", "#F1F5F9", "#9CAABD", "#344052", "#72C3E1", "#243F4D", "#58D6AE", "#F2B36A", "#EF7687");

    public static IReadOnlyList<ThemeDefinition> All { get; } =
    [
        new("system", "跟随系统", "自动匹配 Windows 深浅色，并使用 Exusiai 风格的蓝灰基调", false, Light, Dark),
        new("exusiai", "Exusiai", "以能天使为灵感的深色蓝灰、冷青与暖橙点缀", true, Dark),
        new("paper", "Paper", "克制清晰的暖白工作区", false, new("#EDF7F5F0", "#F9FFFDF8", "#E8EEE9E1", "#24231F", "#706E66", "#9ED8D3C8", "#3568D4", "#D8E4EDFF", "#2E8B68", "#B97818", "#C84B55")),
        new("graphite", "Graphite", "中性的深灰编辑器配色", true, new("#EA17191C", "#EA202327", "#DC292D32", "#F2F4F7", "#AAB0BA", "#70464B53", "#7DA2F8", "#87314360", "#55C59A", "#E8B15B", "#ED7784")),
        new("nord", "Nord", "冷静的极地蓝灰色调", true, new("#EA242933", "#EA2E3440", "#DC3B4252", "#ECEFF4", "#B7C0D0", "#705C667A", "#88C0D0", "#70455D68", "#A3BE8C", "#EBCB8B", "#BF616A")),
        new("tokyo-night", "Tokyo Night", "高对比靛蓝夜间主题", true, new("#EA15161E", "#EA1A1B26", "#DC24283B", "#C0CAF5", "#9AA5CE", "#70414868", "#7AA2F7", "#73304168", "#9ECE6A", "#E0AF68", "#F7768E")),
        new("dracula", "Dracula", "紫色强调的经典暗色方案", true, new("#EA21222C", "#EA282A36", "#DC343746", "#F8F8F2", "#B7B8C3", "#705A5D72", "#BD93F9", "#71463264", "#50FA7B", "#F1FA8C", "#FF5555")),
        new("catppuccin", "Catppuccin", "柔和低刺激的摩卡色板", true, new("#EA181825", "#EA1E1E2E", "#DC313244", "#CDD6F4", "#A6ADC8", "#70585B70", "#CBA6F7", "#704B3E63", "#A6E3A1", "#F9E2AF", "#F38BA8")),
        new("solarized", "Solarized Light", "适合长时间阅读的低对比浅色", false, new("#EDFDF6E3", "#F9FFFBED", "#E8EEE8D5", "#586E75", "#7C8B8E", "#9ECBC4B4", "#268BD2", "#D8DCEAF0", "#2AA198", "#B58900", "#DC322F"))
    ];

    public static ThemeDefinition Find(string? id) =>
        All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

public interface IThemeService
{
    string SelectedThemeId { get; }
    ThemeDefinition SelectedTheme { get; }
    ThemePalette Current { get; }
    bool IsDark { get; }
    IReadOnlyList<ThemeDefinition> AvailableThemes { get; }
    event EventHandler? Changed;
    void Apply(string themeId);
}

public interface ISystemThemeProvider
{
    bool IsDark { get; }
    event EventHandler? Changed;
}

public sealed class ThemeService : IThemeService, IDisposable
{
    private readonly ISystemThemeProvider systemTheme;
    private bool disposed;

    public ThemeService(ISystemThemeProvider? systemTheme = null)
    {
        this.systemTheme = systemTheme ?? new LightSystemThemeProvider();
        SelectedTheme = ThemeCatalog.All[0];
        Current = Resolve(SelectedTheme);
        this.systemTheme.Changed += SystemTheme_OnChanged;
    }

    public string SelectedThemeId => SelectedTheme.Id;
    public ThemeDefinition SelectedTheme { get; private set; }
    public ThemePalette Current { get; private set; }
    public bool IsDark => SelectedTheme.FollowsSystem ? systemTheme.IsDark : SelectedTheme.IsDark;
    public IReadOnlyList<ThemeDefinition> AvailableThemes => ThemeCatalog.All;
    public event EventHandler? Changed;

    public void Apply(string themeId)
    {
        var definition = ThemeCatalog.Find(themeId);
        var palette = Resolve(definition);
        if (ReferenceEquals(SelectedTheme, definition) && Current == palette) return;
        SelectedTheme = definition;
        Current = palette;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        systemTheme.Changed -= SystemTheme_OnChanged;
        GC.SuppressFinalize(this);
    }

    private ThemePalette Resolve(ThemeDefinition definition) =>
        definition.FollowsSystem && systemTheme.IsDark ? definition.DarkPalette! : definition.Palette;

    private void SystemTheme_OnChanged(object? sender, EventArgs e)
    {
        if (!SelectedTheme.FollowsSystem) return;
        Current = Resolve(SelectedTheme);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class LightSystemThemeProvider : ISystemThemeProvider
    {
        public bool IsDark => false;
        public event EventHandler? Changed { add { } remove { } }
    }
}
