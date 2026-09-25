using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ExusiAI.FileViewer.Desktop;

internal sealed class ViewerWindow : Window
{
    private readonly FileViewerPage viewer;
    private readonly ColumnDefinition railColumn = new() { Width = new GridLength(72) };
    private Border? rail;
    private bool presentationMode;
    private WindowState previousWindowState;
    private WindowStyle previousWindowStyle;
    private ResizeMode previousResizeMode;

    public ViewerWindow(FileViewerPage viewer)
    {
        this.viewer = viewer;
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
        Content = BuildShell();
        viewer.DocumentOpened += (_, path) => Title = $"{Path.GetFileName(path)} — ExusiAI Viewer";
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
        PreviewDragOver += OnPreviewDragOver;
        Drop += OnDrop;
        Closing += OnClosing;
        Closed += (_, _) => viewer.Dispose();
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

        Grid.SetColumn(viewer, 1);
        shell.Children.Add(viewer);
        return shell;
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (control && e.Key == Key.O)
        {
            e.Handled = true;
            await viewer.PickFileAsync();
        }
        else if (control && e.Key == Key.S)
        {
            e.Handled = true;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                await viewer.SaveAsCurrentAsync();
            else
                await viewer.SaveCurrentAsync();
        }
        else if (control && e.Key == Key.F)
        {
            e.Handled = true;
            viewer.FocusSearch();
        }
        else if (control && e.Key == Key.R)
        {
            e.Handled = true;
            await viewer.ReloadCurrentAsync();
        }
        else if (e.Key == Key.F3)
        {
            e.Handled = true;
            await viewer.NavigateSearchAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        else if (control && (e.Key == Key.Add || e.Key == Key.OemPlus))
        {
            e.Handled = true;
            viewer.ZoomBy(1);
        }
        else if (control && (e.Key == Key.Subtract || e.Key == Key.OemMinus))
        {
            e.Handled = true;
            viewer.ZoomBy(-1);
        }
        else if (control && (e.Key == Key.D0 || e.Key == Key.NumPad0))
        {
            e.Handled = true;
            viewer.ResetZoom();
        }
        else if (e.Key == Key.F5 && viewer.HasDocument)
        {
            e.Handled = true;
            TogglePresentationMode();
        }
        else if (e.Key == Key.Escape && presentationMode)
        {
            e.Handled = true;
            TogglePresentationMode();
        }
        else if (viewer.CanNavigateSlides &&
                 Keyboard.FocusedElement is not TextBox &&
                 e.Key is Key.PageDown or Key.Right or Key.Down or Key.Space)
        {
            e.Handled = true;
            await viewer.NextPageAsync();
        }
        else if (viewer.CanNavigateSlides &&
                 Keyboard.FocusedElement is not TextBox &&
                 e.Key is Key.PageUp or Key.Left or Key.Up)
        {
            e.Handled = true;
            await viewer.PreviousPageAsync();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!viewer.ConfirmCanClose())
            e.Cancel = true;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
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
        await viewer.OpenFileAsync(filePath);
    }

    private void TogglePresentationMode()
    {
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
