using System.Windows;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using ExusiAI.Theme;
using ExusiAI.Extension.Wpf;
using Microsoft.Extensions.Logging;

namespace ExusiAI.Desktop;

public partial class MainWindow : Window
{
    private readonly IThemeService theme;
    private readonly IWindowBackdropService backdrop;
    private readonly Forms.NotifyIcon trayIcon;
    private readonly WpfTrayRegistry trayRegistry;
    private readonly ILogger<MainWindow> logger;
    private readonly CancellationTokenSource trayCancellation = new();
    private readonly List<Forms.ToolStripItem> trayExtensionItems = [];
    private bool exitRequested;
    private NavigationItem? previousNavigationItem;

    public MainWindow(IThemeService theme, IWindowBackdropService backdrop, WpfTrayRegistry trayRegistry, ILogger<MainWindow> logger)
    {
        this.theme = theme;
        this.backdrop = backdrop;
        this.trayRegistry = trayRegistry;
        this.logger = logger;
        InitializeComponent();
        DataContextChanged += MainWindow_OnDataContextChanged;
        trayIcon = CreateTrayIcon();
        trayRegistry.Changed += TrayRegistry_OnChanged;
        RefreshTrayMenu();
        SourceInitialized += (_, _) =>
        {
            ApplyWindowAppearance();
            ApplyNativeWindowFrame();
        };
        theme.Changed += Appearance_OnChanged;
        backdrop.Changed += Appearance_OnChanged;
        Closed += (_, _) =>
        {
            theme.Changed -= Appearance_OnChanged;
            backdrop.Changed -= Appearance_OnChanged;
            trayRegistry.Changed -= TrayRegistry_OnChanged;
            trayCancellation.Cancel();
            trayCancellation.Dispose();
            trayIcon.ContextMenuStrip?.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
        };
    }

    internal void SetTitleBarHeight(double height) => TitleBarRow.Height = new GridLength(height);

    private void MainWindow_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ShellViewModel oldViewModel) oldViewModel.PropertyChanged -= ShellViewModel_OnPropertyChanged;
        if (e.NewValue is ShellViewModel newViewModel)
        {
            previousNavigationItem = newViewModel.SelectedItem;
            newViewModel.PropertyChanged += ShellViewModel_OnPropertyChanged;
        }
    }

    private void ShellViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.SelectedItem) || sender is not ShellViewModel viewModel) return;
        var oldIndex = previousNavigationItem is null ? -1 : viewModel.NavigationItems.IndexOf(previousNavigationItem);
        var newIndex = viewModel.SelectedItem is null ? -1 : viewModel.NavigationItems.IndexOf(viewModel.SelectedItem);
        previousNavigationItem = viewModel.SelectedItem;
        Dispatcher.BeginInvoke(() => AnimateNavigation(newIndex >= oldIndex ? 22 : -22));
    }

    private void AnimateNavigation(double offset)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        AnimateElement(PageHost, offset, 230, easing);
        AnimateElement(SectionTitle, offset > 0 ? 8 : -8, 180, easing);
    }

    private static void AnimateElement(UIElement element, double offset, int milliseconds, IEasingFunction easing)
    {
        var transform = new TranslateTransform(0, offset);
        element.RenderTransform = transform;
        element.Opacity = 0;
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing });
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(milliseconds - 35)));
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

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 ExusiAI", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        var icon = new Forms.NotifyIcon
        {
            Text = "ExusiAI Assistant",
            Icon = Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        return icon;
    }

    private void TrayRegistry_OnChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(RefreshTrayMenu);

    private void RefreshTrayMenu()
    {
        var menu = trayIcon.ContextMenuStrip;
        if (exitRequested || menu is null || menu.IsDisposed) return;
        foreach (var item in trayExtensionItems)
        {
            menu.Items.Remove(item);
            item.Dispose();
        }
        trayExtensionItems.Clear();
        foreach (var contribution in trayRegistry.Menus.Where(menu => menu.Commands.Count > 0))
        {
            var group = new Forms.ToolStripMenuItem(contribution.Title);
            foreach (var command in contribution.Commands)
            {
                var item = new Forms.ToolStripMenuItem(command.Title);
                item.Click += async (_, _) => await ExecuteTrayCommandAsync(contribution.PackageId, command, item);
                group.DropDownItems.Add(item);
            }
            menu.Items.Insert(menu.Items.Count - 1, group);
            trayExtensionItems.Add(group);
        }
        if (trayExtensionItems.Count > 0)
        {
            var separator = new Forms.ToolStripSeparator();
            menu.Items.Insert(menu.Items.Count - 1, separator);
            trayExtensionItems.Add(separator);
        }
    }

    private async Task ExecuteTrayCommandAsync(string packageId, WpfTrayCommand command, Forms.ToolStripMenuItem item)
    {
        if (exitRequested || !trayRegistry.IsRegistered(packageId, command)) return;
        item.Enabled = false;
        var token = trayCancellation.Token;
        try
        {
            await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                if (trayRegistry.IsRegistered(packageId, command)) await command.ExecuteAsync(token);
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Tray command {CommandId} from {PackageId} failed.", command.Id, packageId);
            if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.InvokeAsync(() =>
            {
                if (!exitRequested) MessageBox.Show(exception.GetBaseException().Message,
                    command.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }
        finally
        {
            if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.InvokeAsync(() =>
            {
                if (!exitRequested && !item.IsDisposed) item.Enabled = true;
            });
        }
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

    private void ApplyNativeWindowFrame()
    {
        // WPF WindowChrome and a manual content clip must not both own the outer radius.
        // Windows 11 can provide a native rounded frame; Windows 10 keeps a clean square edge.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        const int DwmWindowCornerPreference = 33;
        const int DwmBorderColor = 34;
        const int DwmWindowCornerRound = 2;
        var cornerPreference = DwmWindowCornerRound;
        _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref cornerPreference, sizeof(int));

        // DWMWA_COLOR_NONE removes the extra DWM outline around custom chrome.
        var borderColorNone = unchecked((int)0xFFFFFFFE);
        _ = DwmSetWindowAttribute(handle, DwmBorderColor, ref borderColorNone, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
