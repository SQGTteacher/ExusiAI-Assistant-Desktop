using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ExusiAI.Desktop;

public partial class PluginWorkspacePage : UserControl
{
    private PluginPageOption? previousPage;

    public PluginWorkspacePage()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is PluginWorkspaceViewModel oldViewModel) oldViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            if (args.NewValue is PluginWorkspaceViewModel newViewModel) newViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        };
    }

    private void MoveEarlier_OnClick(object sender, RoutedEventArgs e) => Move(sender, -1);
    private void MoveLater_OnClick(object sender, RoutedEventArgs e) => Move(sender, 1);

    private void Move(object sender, int offset)
    {
        if (DataContext is not PluginWorkspaceViewModel viewModel || sender is not FrameworkElement { DataContext: PluginPageOption page }) return;
        viewModel.MovePageCommand.Execute(new PluginPageMoveRequest(page, offset));
    }

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PluginWorkspaceViewModel.SelectedPage) || sender is not PluginWorkspaceViewModel viewModel) return;
        var next = viewModel.SelectedPage;
        var oldIndex = previousPage is null ? -1 : viewModel.Pages.IndexOf(previousPage);
        var newIndex = next is null ? -1 : viewModel.Pages.IndexOf(next);
        previousPage = next;
        AnimateContent(newIndex >= oldIndex ? 18 : -18);
    }

    private void AnimateContent(double offset)
    {
        var transform = new TranslateTransform(0, offset);
        ContentFrame.RenderTransform = transform;
        ContentFrame.Opacity = 0;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
        ContentFrame.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
    }
}
