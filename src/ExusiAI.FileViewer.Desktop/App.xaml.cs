using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ExusiAI.FileViewer.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.FirstOrDefault() == "--verify-package")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                if (e.Args.Length != 3) throw new ArgumentException("Usage: --verify-package <fixture-directory> <report-path>");
                await ViewerPackageProbe.RunAsync(e.Args[1], e.Args[2]);
                Shutdown(0);
            }
            catch (Exception exception)
            {
                try
                {
                    if (e.Args.Length == 3) await File.WriteAllTextAsync(e.Args[2], exception.ToString());
                }
                catch (IOException) { /* Exit status still reports failure when the report cannot be written. */ }
                catch (UnauthorizedAccessException) { }
                Shutdown(1);
            }
            return;
        }
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
