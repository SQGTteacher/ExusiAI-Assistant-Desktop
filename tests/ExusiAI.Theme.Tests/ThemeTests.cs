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

    private sealed class TestSystemThemeProvider : ISystemThemeProvider
    {
        public bool IsDark { get; set; }
        public event EventHandler? Changed;
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
