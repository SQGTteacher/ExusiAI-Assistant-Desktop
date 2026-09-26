using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;

namespace ExusiAI.Desktop;

public partial class PluginWorkspacePage : UserControl
{
    private PluginPageOption? previousPage;
    private Point dragStart;
    private PluginPageOption? dragCandidate;

    public PluginWorkspacePage()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is PluginWorkspaceViewModel oldViewModel) oldViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            if (args.NewValue is PluginWorkspaceViewModel newViewModel) newViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        };
    }

    private void PluginList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(PluginList);
        dragCandidate = FindItem(e.OriginalSource as DependencyObject)?.DataContext as PluginPageOption;
    }

    private void PluginList_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragCandidate is null) return;
        var point = e.GetPosition(PluginList);
        if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(PluginList, dragCandidate, DragDropEffects.Move);
        dragCandidate = null;
    }

    private void PluginList_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PluginPageOption)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void PluginList_OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not PluginWorkspaceViewModel viewModel ||
            e.Data.GetData(typeof(PluginPageOption)) is not PluginPageOption source) return;
        var target = FindItem(e.OriginalSource as DependencyObject)?.DataContext as PluginPageOption;
        var targetIndex = target is null ? viewModel.Pages.Count - 1 : viewModel.Pages.IndexOf(target);
        await viewModel.MovePageAsync(source, targetIndex);
        e.Handled = true;
    }

    private ListBoxItem? FindItem(DependencyObject? source) =>
        source is null ? null : ItemsControl.ContainerFromElement(PluginList, source) as ListBoxItem;

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
