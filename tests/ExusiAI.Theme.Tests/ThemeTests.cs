using ExusiAI.Theme;

namespace ExusiAI.Theme.Tests;

public sealed class ThemeTests
{
    [Fact]
    public void ExplicitThemeOverridesSystemTheme()
    {
        var system = new TestSystemThemeProvider { IsDark = true };
        using var service = new ThemeService(system);
        service.Apply("paper");
        Assert.Same(ThemeCatalog.Find("paper").Palette, service.Current);
        Assert.False(service.IsDark);
    }

    [Fact]
    public void SystemThemeTracksProviderChanges()
    {
        var system = new TestSystemThemeProvider();
        using var service = new ThemeService(system);
        system.IsDark = true;
        system.RaiseChanged();
        Assert.Same(ThemeCatalog.Dark, service.Current);
        Assert.True(service.IsDark);
    }

    [Fact]
    public void UnknownThemeFallsBackToSystem()
    {
        using var service = new ThemeService(new TestSystemThemeProvider());
        service.Apply("does-not-exist");
        Assert.Equal("system", service.SelectedThemeId);
        Assert.True(service.AvailableThemes.Count >= 8);
    }

    [Fact]
    public void ThemeCatalogSeparatesAutomaticLightAndDarkModes()
    {
        Assert.Equal("自动", ThemeCatalog.Find("system").ModeGroup);
        Assert.Contains(ThemeCatalog.All, theme => theme.ModeGroup == "浅色主题");
        Assert.Contains(ThemeCatalog.All, theme => theme.ModeGroup == "深色主题");
    }

    [Fact]
    public void ThemeCatalogKeepsPrimaryTextReadable()
    {
        foreach (var theme in ThemeCatalog.All)
        {
            foreach (var palette in theme.DarkPalette is null
                         ? new[] { theme.Palette }
                         : new[] { theme.Palette, theme.DarkPalette! })
            {
                Assert.True(
                    ContrastRatio(palette.TextPrimary, palette.Background) >= 4.5,
                    $"{theme.Id} primary text/background contrast is too low.");
                Assert.True(
                    ContrastRatio(palette.TextPrimary, palette.Surface) >= 4.5,
                    $"{theme.Id} primary text/surface contrast is too low.");
            }
        }
    }

    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string color)
    {
        var value = color.TrimStart('#');
        if (value.Length == 8) value = value[2..];
        var red = Convert.ToInt32(value[..2], 16) / 255d;
        var green = Convert.ToInt32(value.Substring(2, 2), 16) / 255d;
        var blue = Convert.ToInt32(value.Substring(4, 2), 16) / 255d;
        return 0.2126 * Linearize(red) + 0.7152 * Linearize(green) + 0.0722 * Linearize(blue);
    }

    private static double Linearize(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private sealed class TestSystemThemeProvider : ISystemThemeProvider
    {
        public bool IsDark { get; set; }
        public event EventHandler? Changed;
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
