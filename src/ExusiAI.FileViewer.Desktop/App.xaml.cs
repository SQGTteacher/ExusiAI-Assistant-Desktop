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
        var window = new ViewerWindow(settings);
        if (settings.StartMaximized) window.WindowState = WindowState.Maximized;
        MainWindow = window;
        window.Show();

        var filePath = e.Args.FirstOrDefault(File.Exists);
        await window.InitializeAsync(filePath);
    }

    private void ApplyLightTheme()
    {
        Resources["AppBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(243, 245, 249));
        Resources["SurfaceBrush"] = Brushes.White;
        Resources["SurfaceAltBrush"] = new SolidColorBrush(Color.FromRgb(236, 239, 244));
        Resources["BorderBrush"] = new SolidColorBrush(Color.FromRgb(210, 215, 224));
        Resources["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(28, 31, 38));
        Resources["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(92, 99, 112));
    }
}
