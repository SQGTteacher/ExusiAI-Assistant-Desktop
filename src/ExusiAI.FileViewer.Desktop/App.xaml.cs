using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ExusiAI.FileViewer.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = await ViewerSettings.LoadAsync();
        if (!settings.PreferDarkTheme) ApplyLightTheme();
        var viewer = new FileViewerPage(settings);
        var window = new ViewerWindow(viewer);
        if (settings.StartMaximized) window.WindowState = WindowState.Maximized;
        MainWindow = window;
        window.Show();

        var filePath = e.Args.FirstOrDefault(File.Exists);
        if (filePath is not null)
            await viewer.OpenFileAsync(filePath);
    }

    private void ApplyLightTheme()
    {
        Resources["AppBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(243, 245, 249));
        Resources["SurfaceBrush"] = Brushes.White;
        Resources["SurfaceAltBrush"] = new SolidColorBrush(Color.FromRgb(236, 239, 244));
        Resources["BorderBrush"] = new SolidColorBrush(Color.FromRgb(210, 215, 224));
        Resources["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(28, 31, 38));
    }
}
