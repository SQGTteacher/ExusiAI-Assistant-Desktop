using ExusiAI.Theme;

namespace ExusiAI.Theme.Tests;

public sealed class ThemeTests
{
    [Fact]
    public void ExplicitThemeOverridesSystemTheme()
    {
        var system = new TestSystemThemeProvider { IsDark = true };
        using var service = new ThemeService(system);
        service.Apply(ThemeSelection.Light);
        Assert.Same(ThemeService.Light, service.Current);
    }

    [Fact]
    public void SystemThemeTracksProviderChanges()
    {
        var system = new TestSystemThemeProvider();
        using var service = new ThemeService(system);
        system.IsDark = true;
        system.RaiseChanged();
        Assert.Same(ThemeService.Dark, service.Current);
    }

    private sealed class TestSystemThemeProvider : ISystemThemeProvider
    {
        public bool IsDark { get; set; }
        public event EventHandler? Changed;
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
