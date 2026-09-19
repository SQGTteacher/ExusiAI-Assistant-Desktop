using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Forms;
using Drawing = System.Drawing;
using ExusiAI.Theme;

namespace ExusiAI.Desktop;

public partial class MainWindow : Window
{
    private readonly IThemeService theme;
    private readonly IWindowBackdropService backdrop;
    private readonly NotifyIcon trayIcon;
    private bool exitRequested;

    public MainWindow(IThemeService theme, IWindowBackdropService backdrop)
    {
        this.theme = theme;
        this.backdrop = backdrop;
        InitializeComponent();
        trayIcon = CreateTrayIcon();
        SizeChanged += (_, _) => UpdateContentClip();
        StateChanged += (_, _) => UpdateContentClip();
        SourceInitialized += (_, _) => ApplyWindowAppearance();
        theme.Changed += Appearance_OnChanged;
        backdrop.Changed += Appearance_OnChanged;
        Closed += (_, _) =>
        {
            theme.Changed -= Appearance_OnChanged;
            backdrop.Changed -= Appearance_OnChanged;
            trayIcon.Visible = false;
            trayIcon.Dispose();
        };
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else DragMove();
    }

    private void Minimize_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_OnClick(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_OnClick(object sender, RoutedEventArgs e) => HideToTray();
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!exitRequested)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnClosing(e);
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 ExusiAI", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        var icon = new NotifyIcon
        {
            Text = "ExusiAI Assistant",
            Icon = Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        return icon;
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        exitRequested = true;
        Close();
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Appearance_OnChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(ApplyWindowAppearance);
    private void ApplyWindowAppearance() => backdrop.ApplyTo(this, theme.IsDark);

    private void UpdateContentClip()
    {
        var radius = WindowState == WindowState.Maximized ? 0d : 12d;
        ChromeRoot.Clip = ActualWidth > 0 && ActualHeight > 0
            ? new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), radius, radius)
            : null;
    }
}
