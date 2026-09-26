using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;

namespace ExusiAI.Desktop;

public partial class PluginWorkspacePage : UserControl
{
    private PluginPageOption? previousPage;
    private System.Windows.Point dragStart;
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

    private async void PluginList_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0 ||
            DataContext is not PluginWorkspaceViewModel viewModel ||
            PluginList.SelectedItem is not PluginPageOption page) return;

        var index = viewModel.Pages.IndexOf(page);
        int insertionIndex;
        switch (e.Key)
        {
            case Key.Left:
            case Key.Up:
                if (index <= 0) return;
                insertionIndex = index - 1;
                break;
            case Key.Right:
            case Key.Down:
                if (index < 0 || index >= viewModel.Pages.Count - 1) return;
                insertionIndex = index + 2;
                break;
            default:
                return;
        }

        await viewModel.MovePageAsync(page, insertionIndex);
        e.Handled = true;
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

        var insertionIndex = GetDropInsertionIndex(e.GetPosition(PluginList));
        await viewModel.MovePageAsync(source, insertionIndex);
        e.Handled = true;
    }

    private int GetDropInsertionIndex(System.Windows.Point position)
    {
        for (var index = 0; index < PluginList.Items.Count; index++)
        {
            if (PluginList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem item) continue;

            var topLeft = item.TranslatePoint(new System.Windows.Point(0, 0), PluginList);
            var bounds = new Rect(topLeft, item.RenderSize);

            if (position.Y < bounds.Top) return index;
            if (position.Y > bounds.Bottom) continue;
            if (position.X < bounds.Left + bounds.Width / 2) return index;
            if (position.X <= bounds.Right + item.Margin.Right) return index + 1;
        }

        return PluginList.Items.Count;
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
