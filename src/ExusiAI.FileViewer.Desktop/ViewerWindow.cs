using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ExusiAI.FileViewer.Desktop;

internal sealed class ViewerWindow : Window
{
    private readonly ViewerSettings settings;
    private readonly ColumnDefinition railColumn = new() { Width = new GridLength(72) };
    private readonly ContentControl viewerHost = new();
    private Border? rail;
    private FileViewerPage? viewer;
    private string? pendingFile;
    private bool initializing;
    private bool presentationMode;
    private WindowState previousWindowState;
    private WindowStyle previousWindowStyle;
    private ResizeMode previousResizeMode;

    public ViewerWindow(ViewerSettings settings)
    {
        this.settings = settings;
        Title = "ExusiAI Viewer";
        Width = 1360;
        Height = 860;
        MinWidth = 960;
        MinHeight = 640;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AllowDrop = true;
        viewerHost.Content = BuildStartupSurface();
        Content = BuildShell();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
        PreviewDragOver += OnPreviewDragOver;
        Drop += OnDrop;
        Closing += OnClosing;
        Closed += (_, _) => viewer?.Dispose();
    }

    public async Task InitializeAsync(string? initialFile = null)
    {
        if (!string.IsNullOrWhiteSpace(initialFile))
            pendingFile = initialFile;

        if (viewer is not null)
        {
            var existingPending = pendingFile;
            pendingFile = null;
            if (!string.IsNullOrWhiteSpace(existingPending) && File.Exists(existingPending))
                await viewer.OpenFileAsync(existingPending);
            return;
        }

        if (initializing)
            return;

        initializing = true;
        try
        {
            // Allow the lightweight window shell to render before constructing the full document workspace.
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);

            var page = new FileViewerPage(settings);
            page.DocumentOpened += (_, path) => Title = $"{Path.GetFileName(path)} — ExusiAI Viewer";
            viewer = page;
            viewerHost.Content = page;

            var file = pendingFile;
            pendingFile = null;
            if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
                await page.OpenFileAsync(file);
        }
        finally
        {
            initializing = false;
        }
    }

    private FrameworkElement BuildShell()
    {
        var shell = new Grid();
        shell.ColumnDefinitions.Add(railColumn);
        shell.ColumnDefinitions.Add(new ColumnDefinition());

        rail = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        rail.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        rail.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var railContent = new StackPanel { Margin = new Thickness(0, 18, 0, 12) };
        railContent.Children.Add(new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(11),
            Background = FindResource("AccentBrush") as Brush,
            Child = new TextBlock
            {
                Text = "E",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            }
        });
        var railLabel = new TextBlock
        {
            Text = "VIEW",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            Opacity = 0.72
        };
        railLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        railContent.Children.Add(railLabel);
        rail.Child = railContent;
        shell.Children.Add(rail);

        Grid.SetColumn(viewerHost, 1);
        shell.Children.Add(viewerHost);
        return shell;
    }

    private FrameworkElement BuildStartupSurface()
    {
        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "AppBackgroundBrush");

        var card = new Border
        {
            MaxWidth = 720,
            Padding = new Thickness(42, 36, 42, 38),
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1)
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "ExusiAI Viewer",
            FontSize = 30,
            FontWeight = FontWeights.SemiBold
        });
        var subtitle = new TextBlock
        {
            Text = "窗口已就绪，正在按需装载文档工作区。",
            FontSize = 14,
            Margin = new Thickness(0, 8, 0, 20)
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        content.Children.Add(subtitle);

        var progress = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 4,
            BorderThickness = new Thickness(0)
        };
        content.Children.Add(progress);

        var note = new TextBlock
        {
            Text = "启动阶段不会预读取 Office 文档，也不会提前构建搜索、缩略图或分页内容。",
            FontSize = 11,
            Margin = new Thickness(0, 16, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        content.Children.Add(note);
        card.Child = content;
        root.Children.Add(card);
        return root;
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (control && e.Key == Key.O)
        {
            e.Handled = true;
            if (viewer is null) await InitializeAsync();
            if (viewer is not null) await viewer.PickFileAsync();
        }
        else if (control && e.Key == Key.S && viewer is not null)
        {
            e.Handled = true;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                await viewer.SaveAsCurrentAsync();
            else
                await viewer.SaveCurrentAsync();
        }
        else if (control && e.Key == Key.F && viewer is not null)
        {
            e.Handled = true;
            viewer.FocusSearch();
        }
        else if (control && e.Key == Key.R && viewer is not null)
        {
            e.Handled = true;
            await viewer.ReloadCurrentAsync();
        }
        else if (control && e.Key == Key.P && viewer is not null)
        {
            e.Handled = true;
            viewer.PrintCurrent();
        }
        else if (control && e.Key == Key.G && viewer is not null)
        {
            e.Handled = true;
            viewer.FocusGoToLine();
        }
        else if (e.Key == Key.F3 && viewer is not null)
        {
            e.Handled = true;
            await viewer.NavigateSearchAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        else if (control && (e.Key == Key.Add || e.Key == Key.OemPlus) && viewer is not null)
        {
            e.Handled = true;
            viewer.ZoomBy(1);
        }
        else if (control && (e.Key == Key.Subtract || e.Key == Key.OemMinus) && viewer is not null)
        {
            e.Handled = true;
            viewer.ZoomBy(-1);
        }
        else if (control && (e.Key == Key.D0 || e.Key == Key.NumPad0) && viewer is not null)
        {
            e.Handled = true;
            viewer.ResetZoom();
        }
        else if (e.Key == Key.F5 && viewer?.HasDocument == true)
        {
            e.Handled = true;
            TogglePresentationMode();
        }
        else if (e.Key == Key.Escape && presentationMode)
        {
            e.Handled = true;
            TogglePresentationMode();
        }
        else if (viewer?.CanNavigateSlides == true &&
                 Keyboard.FocusedElement is not TextBox &&
                 e.Key is Key.PageDown or Key.Right or Key.Down or Key.Space)
        {
            e.Handled = true;
            await viewer.NextPageAsync();
        }
        else if (viewer?.CanNavigateSlides == true &&
                 Keyboard.FocusedElement is not TextBox &&
                 e.Key is Key.PageUp or Key.Left or Key.Up)
        {
            e.Handled = true;
            await viewer.PreviousPageAsync();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (viewer is not null && !viewer.ConfirmCanClose())
            e.Cancel = true;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (viewer is null || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        viewer.ZoomBy(e.Delta > 0 ? 1 : -1);
    }

    private static bool TryGetDroppedFile(DragEventArgs e, out string filePath)
    {
        filePath = string.Empty;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files) return false;
        filePath = files[0];
        return File.Exists(filePath);
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedFile(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!TryGetDroppedFile(e, out var filePath)) return;
        e.Handled = true;
        await InitializeAsync(filePath);
    }

    private void TogglePresentationMode()
    {
        if (viewer is null)
            return;

        presentationMode = !presentationMode;
        if (presentationMode)
        {
            previousWindowState = WindowState;
            previousWindowStyle = WindowStyle;
            previousResizeMode = ResizeMode;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            railColumn.Width = new GridLength(0);
            if (rail is not null) rail.Visibility = Visibility.Collapsed;
        }
        else
        {
            WindowStyle = previousWindowStyle;
            ResizeMode = previousResizeMode;
            WindowState = previousWindowState;
            railColumn.Width = new GridLength(72);
            if (rail is not null) rail.Visibility = Visibility.Visible;
        }
        viewer.SetPresentationMode(presentationMode);
    }
}
