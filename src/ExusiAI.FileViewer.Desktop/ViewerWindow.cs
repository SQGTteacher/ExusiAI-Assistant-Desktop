using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ExusiAI.FileViewer.Desktop;

internal sealed class ViewerWindow : Window
{
    private readonly FileViewerPage viewer;

    public ViewerWindow(FileViewerPage viewer)
    {
        this.viewer = viewer;
        Title = "ExusiAI Viewer";
        Width = 1360;
        Height = 860;
        MinWidth = 960;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = BuildShell();
        Closed += (_, _) => viewer.Dispose();
    }

    private FrameworkElement BuildShell()
    {
        var shell = new Grid();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());

        var rail = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(10, 13, 19)),
            BorderBrush = FindResource("BorderBrush") as Brush,
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
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
        railContent.Children.Add(new TextBlock
        {
            Text = "VIEW",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 188)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            Opacity = 0.58
        });
        rail.Child = railContent;
        shell.Children.Add(rail);

        Grid.SetColumn(viewer, 1);
        shell.Children.Add(viewer);
        return shell;
    }
}
