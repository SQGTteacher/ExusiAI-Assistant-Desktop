using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ExusiAI.Plugin.ClassIsland;

public sealed class ClassIslandPresentationService
{
    private readonly ClassIslandTimetableService timetable;
    private readonly ClassIslandAppearanceService appearance;
    private Window? window;
    private Border? island;
    private TextBlock? timeText;
    private TextBlock? lessonText;
    private TextBlock? detailText;
    private DispatcherTimer? timer;
    private TouchDevice? dragTouch;
    private Point dragStartScreen;
    private Point dragStartWindow;

    public ClassIslandPresentationService(ClassIslandTimetableService timetable, ClassIslandAppearanceService appearance)
    { this.timetable = timetable; this.appearance = appearance; }

    public bool IsVisible => window?.IsVisible == true;

    public void Start()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (!dispatcher.CheckAccess()) { dispatcher.Invoke(Start); return; }
        if (window is null) BuildWindow();
        ApplyAppearance(); Refresh(); window!.Show();
        timer!.Start();
    }

    public void Stop()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (!dispatcher.CheckAccess()) { dispatcher.Invoke(Stop); return; }
        timer?.Stop(); window?.Hide();
    }

    public void RefreshAppearance()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.Invoke(ApplyAppearance);
    }

    private void BuildWindow()
    {
        window = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Manual
        };
        island = new Border { Padding = new Thickness(22, 10, 22, 10), BorderThickness = new Thickness(1) };
        island.Cursor = Cursors.SizeAll;
        island.MouseLeftButtonDown += OnMouseLeftButtonDown;
        island.PreviewTouchDown += OnTouchDown;
        island.PreviewTouchMove += OnTouchMove;
        island.PreviewTouchUp += OnTouchUp;
        island.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        island.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeText = new TextBlock { FontSize = 25, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        timeText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var lesson = new StackPanel { Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        lessonText = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold };
        lessonText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        detailText = new TextBlock { FontSize = 12, Margin = new Thickness(0, 3, 0, 0) };
        detailText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        lesson.Children.Add(lessonText); lesson.Children.Add(detailText);
        Grid.SetColumn(lesson, 1); grid.Children.Add(timeText); grid.Children.Add(lesson);
        island.Child = grid; window.Content = island;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Refresh();
        appearance.Changed += (_, _) => ApplyAppearance();
    }

    private async void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (window is null || e.StylusDevice is not null) return;
        try { window.DragMove(); }
        catch (InvalidOperationException) { return; }
        await PersistDraggedPositionAsync();
    }

    private void OnTouchDown(object sender, TouchEventArgs e)
    {
        if (window is null || dragTouch is not null) return;
        dragTouch = e.TouchDevice;
        dragStartScreen = window.PointToScreen(e.GetTouchPoint(window).Position);
        dragStartWindow = new Point(window.Left, window.Top);
        e.TouchDevice.Capture(island);
        e.Handled = true;
    }

    private void OnTouchMove(object sender, TouchEventArgs e)
    {
        if (window is null || e.TouchDevice != dragTouch) return;
        var current = window.PointToScreen(e.GetTouchPoint(window).Position);
        window.Left = dragStartWindow.X + current.X - dragStartScreen.X;
        window.Top = dragStartWindow.Y + current.Y - dragStartScreen.Y;
        e.Handled = true;
    }

    private async void OnTouchUp(object sender, TouchEventArgs e)
    {
        if (e.TouchDevice != dragTouch) return;
        e.TouchDevice.Capture(null);
        dragTouch = null;
        e.Handled = true;
        await PersistDraggedPositionAsync();
    }

    private async Task PersistDraggedPositionAsync()
    {
        if (window is null) return;
        var s = appearance.Settings;
        var area = SystemParameters.WorkArea;
        s.OffsetX = s.DockPosition switch
        {
            ClassIslandDockPosition.TopLeft or ClassIslandDockPosition.BottomLeft => window.Left - area.Left,
            ClassIslandDockPosition.TopRight or ClassIslandDockPosition.BottomRight => area.Right - window.Width - window.Left,
            _ => window.Left - (area.Left + (area.Width - window.Width) / 2)
        };
        s.OffsetY = s.DockPosition switch
        {
            ClassIslandDockPosition.BottomLeft or ClassIslandDockPosition.BottomCenter or ClassIslandDockPosition.BottomRight => area.Bottom - window.Height - window.Top,
            _ => window.Top - area.Top
        };
        await appearance.SaveAsync();
    }

    private void Refresh()
    {
        if (timeText is null) return;
        var now = DateTime.Now;
        timeText.Text = now.ToString(appearance.Settings.ShowSeconds ? "HH:mm:ss" : "HH:mm");
        var lessons = timetable.GetLessons(now);
        var current = lessons.FirstOrDefault(x => now.TimeOfDay >= x.Time.StartTime && now.TimeOfDay < x.Time.EndTime);
        var next = lessons.FirstOrDefault(x => x.Time.StartTime > now.TimeOfDay);
        if (current is not null)
        {
            lessonText!.Text = current.Subject.Name;
            detailText!.Text = $"{current.Time.StartTime:hh\\:mm}–{current.Time.EndTime:hh\\:mm}  {current.Subject.TeacherName}  {current.Subject.Location}".Trim();
        }
        else if (next is not null)
        {
            lessonText!.Text = $"接下来 · {next.Subject.Name}";
            detailText!.Text = $"{next.Time.StartTime:hh\\:mm} 开始  {next.Subject.TeacherName}  {next.Subject.Location}".Trim();
        }
        else { lessonText!.Text = "当前没有课程"; detailText!.Text = "ClassIsland · ExusiAI"; }
    }

    private void ApplyAppearance()
    {
        if (window is null || island is null) return;
        var s = appearance.Settings;
        window.Width = s.Width * s.Scale; window.Height = s.Height * s.Scale; window.Opacity = s.Opacity; window.Topmost = s.Topmost;
        island.CornerRadius = new CornerRadius(Math.Min(s.CornerRadius, s.Height / 2));
        var area = SystemParameters.WorkArea;
        window.Left = s.DockPosition switch
        {
            ClassIslandDockPosition.TopLeft or ClassIslandDockPosition.BottomLeft => area.Left + s.OffsetX,
            ClassIslandDockPosition.TopRight or ClassIslandDockPosition.BottomRight => area.Right - window.Width - s.OffsetX,
            _ => area.Left + (area.Width - window.Width) / 2 + s.OffsetX
        };
        window.Top = s.DockPosition switch
        {
            ClassIslandDockPosition.BottomLeft or ClassIslandDockPosition.BottomCenter or ClassIslandDockPosition.BottomRight => area.Bottom - window.Height - s.OffsetY,
            _ => area.Top + s.OffsetY
        };
        island.Clip = new RectangleGeometry(new Rect(0, 0, window.Width, window.Height), island.CornerRadius.TopLeft, island.CornerRadius.TopLeft);
    }
}
