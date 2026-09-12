using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ExusiAI.Desktop;

public enum WindowBackdropKind { Solid, Mica, Acrylic }

public sealed record BackdropOption(WindowBackdropKind Value, string Name, string Description);

public interface IWindowBackdropService
{
    WindowBackdropKind Selection { get; }
    IReadOnlyList<BackdropOption> Options { get; }
    event EventHandler? Changed;
    void Apply(WindowBackdropKind selection);
    void ApplyTo(Window window, bool isDark);
}

public sealed class WindowBackdropService : IWindowBackdropService
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;

    public WindowBackdropKind Selection { get; private set; } = WindowBackdropKind.Mica;
    public IReadOnlyList<BackdropOption> Options { get; } =
    [
        new(WindowBackdropKind.Mica, "云母", "Windows 11 原生桌面材质，性能与稳定性优先"),
        new(WindowBackdropKind.Acrylic, "亚克力", "更明显的半透明模糊，适合浮层感界面"),
        new(WindowBackdropKind.Solid, "纯色", "关闭透明材质，在旧设备上保持一致")
    ];
    public event EventHandler? Changed;

    public void Apply(WindowBackdropKind selection)
    {
        if (Selection == selection) return;
        Selection = selection;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyTo(Window window, bool isDark)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsWindows()) return;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var darkMode = isDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int));

        // DWM owns the real outline, so WPF content cannot leak beyond a separately
        // painted rounded border at the four corners.
        var corner = 2; // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref corner, sizeof(int));

        var backdropKind = Selection switch
        {
            WindowBackdropKind.Mica => 2,     // DWMSBT_MAINWINDOW
            WindowBackdropKind.Acrylic => 3, // DWMSBT_TRANSIENTWINDOW
            _ => 1                           // DWMSBT_NONE
        };
        _ = DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref backdropKind, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);
}
