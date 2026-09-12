using System.Windows;

namespace ExusiAI.Extension.Wpf;

public sealed record WpfNavigationPage(
    string Route,
    string Title,
    string IconGlyph,
    Func<FrameworkElement> CreateView);

public interface IWpfNavigationExtension
{
    IReadOnlyCollection<WpfNavigationPage> GetNavigationPages();
}
