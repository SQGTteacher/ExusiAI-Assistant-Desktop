namespace ExusiAI.Theme;

public enum ThemeSelection { System, Light, Dark }

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

public interface IThemeService
{
    ThemeSelection Selection { get; }
    ThemePalette Current { get; }
    event EventHandler? Changed;
    void Apply(ThemeSelection selection);
}

public interface ISystemThemeProvider
{
    bool IsDark { get; }
    event EventHandler? Changed;
}

public sealed class ThemeService : IThemeService, IDisposable
{
    public static ThemePalette Light { get; } = new("#F5F6FA", "#FFFFFF", "#F0F2F7", "#171A23", "#697083", "#E1E4EB", "#5268E5", "#E8ECFF", "#159A6A", "#D88A14", "#D34A5A");
    public static ThemePalette Dark { get; } = new("#101119", "#181A25", "#212431", "#F3F4F8", "#A4A9B8", "#303443", "#8796FF", "#292F57", "#50C99A", "#F0AE4A", "#F17382");

    private readonly ISystemThemeProvider systemTheme;
    private bool disposed;

    public ThemeService(ISystemThemeProvider? systemTheme = null)
    {
        this.systemTheme = systemTheme ?? new LightSystemThemeProvider();
        Current = this.systemTheme.IsDark ? Dark : Light;
        this.systemTheme.Changed += SystemTheme_OnChanged;
    }

    public ThemeSelection Selection { get; private set; } = ThemeSelection.System;
    public ThemePalette Current { get; private set; }
    public event EventHandler? Changed;

    public void Apply(ThemeSelection selection)
    {
        if (Selection == selection && Current == Resolve(selection)) return;
        Selection = selection;
        Current = Resolve(selection);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        systemTheme.Changed -= SystemTheme_OnChanged;
        GC.SuppressFinalize(this);
    }

    private ThemePalette Resolve(ThemeSelection selection) => selection == ThemeSelection.Dark || (selection == ThemeSelection.System && systemTheme.IsDark) ? Dark : Light;

    private void SystemTheme_OnChanged(object? sender, EventArgs e)
    {
        if (Selection != ThemeSelection.System) return;
        Current = Resolve(Selection);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class LightSystemThemeProvider : ISystemThemeProvider
    {
        public bool IsDark => false;
        public event EventHandler? Changed { add { } remove { } }
    }
}
