using ExusiAI.Theme;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace ExusiAI.Infrastructure;

public sealed class SystemThemeProvider : ISystemThemeProvider, IDisposable
{
    private readonly Timer timer;
    private bool isDark;
    private bool disposed;

    public SystemThemeProvider()
    {
        isDark = ReadIsDark();
        timer = new Timer(CheckTheme, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    public bool IsDark => isDark;
    public event EventHandler? Changed;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Dispose();
    }

    private void CheckTheme(object? state)
    {
        var next = ReadIsDark();
        if (next == isDark)
        {
            return;
        }

        isDark = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool ReadIsDark()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var value = key?.GetValue("AppsUseLightTheme");
        return value is int lightTheme && lightTheme == 0;
    }
}
