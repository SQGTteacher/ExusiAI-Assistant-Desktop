using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ExusiAI.Theme;
using Microsoft.Extensions.DependencyInjection;

namespace ExusiAI.Desktop;

/// <summary>Windows-only rendering check for the actual shell, without starting plugins.</summary>
internal static class UiVerification
{
    internal static async Task RunAsync(IServiceProvider services, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var bindings = new StringWriter();
        using var listener = new TextWriterTraceListener(bindings);
        var source = PresentationTraceSources.DataBindingSource;
        var previousLevel = source.Switch.Level;
        source.Switch.Level = SourceLevels.Error;
        source.Listeners.Add(listener);
        try
        {
            var theme = services.GetRequiredService<IThemeService>();
            var shell = services.GetRequiredService<ShellViewModel>();
            var window = services.GetRequiredService<MainWindow>();
            Application.Current.MainWindow = window;
            window.DataContext = shell;
            window.Show();
            var expected = new Dictionary<string, Type>
            {
                ["home"] = typeof(HomePage), ["workspace"] = typeof(PluginWorkspacePage),
                ["marketplace"] = typeof(MarketplacePage), ["extensions"] = typeof(PluginManagerPage),
                ["theme"] = typeof(ThemePage), ["settings"] = typeof(SoftwareSettingsPage)
            };

            foreach (var (name, palette) in new[] { ("light", ThemeCatalog.Light), ("dark", ThemeCatalog.Dark) })
            foreach (var width in new[] { 1200, 900 })
            {
                App.ApplyTheme(palette);
                window.Width = width;
                window.Height = width == 900 ? 620 : 800;
                foreach (var item in shell.NavigationItems)
                {
                    shell.NavigateCommand.Execute(item.Route);
                    if (shell.CurrentPage?.GetType() != expected[item.Route])
                        throw new InvalidOperationException($"Page creation failed: {item.Route}");
                    await Task.Delay(300); // Let the production page transition finish.
                    await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
                    // Hosted runners can have a 1024px desktop. Arrange the real content
                    // at the requested DIP size so both layout cases remain deterministic.
                    var content = (FrameworkElement)window.Content;
                    content.Measure(new System.Windows.Size(width, window.Height));
                    content.Arrange(new Rect(0, 0, width, window.Height));
                    content.UpdateLayout();
                    Capture(window, Path.Combine(outputDirectory, $"{name}-{width}-{item.Route}.png"));
                }
            }

            // Exercise the native ComboBox popup and every installed palette as well.
            shell.NavigateCommand.Execute("theme");
            foreach (var option in theme.AvailableThemes)
            {
                App.ApplyTheme(option.Palette);
                await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            }
            var comboBox = Descendants(shell.CurrentPage!).OfType<ComboBox>().First();
            comboBox.IsDropDownOpen = true;
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
            comboBox.IsDropDownOpen = false;
            listener.Flush();
            var errors = bindings.ToString();
            File.WriteAllText(Path.Combine(outputDirectory, "bindings.log"), errors);
            if (errors.Length > 0) throw new InvalidOperationException("WPF binding errors; see bindings.log.");
            File.WriteAllText(Path.Combine(outputDirectory, "result.txt"),
                "PASS: six actual pages, light/dark, 1200x800 and 900x620; all palette resources and material popup loaded. Plugins were not started.");
        }
        finally
        {
            source.Listeners.Remove(listener);
            source.Switch.Level = previousLevel;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Capture(Window window, string path)
    {
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),
            (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
