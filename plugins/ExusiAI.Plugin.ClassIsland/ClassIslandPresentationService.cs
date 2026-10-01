using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using System.Text.Json;
using Forms = System.Windows.Forms;

namespace ExusiAI.Plugin.ClassIsland;

public sealed class ClassIslandPresentationService
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const int WsExAppWindow = 0x40000;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint handle, int index, int value);
    private readonly ClassIslandTimetableService timetable;
    private readonly ClassIslandComponentService components;
    private readonly ClassIslandAppearanceService appearance;
    private readonly ClassIslandNotificationService notifications;
    private readonly ClassIslandWeatherService weather;
    private Window? window;
    private Border? island;
    private TextBlock? timeText;
    private TextBlock? lessonText;
    private TextBlock? detailText;
    private Border? notificationOverlay;
    private TextBlock? notificationText;
    private Grid? defaultContent;
    private StackPanel? componentContent;
    private readonly List<(ClassIslandComponentSettings Settings, TextBlock Text)> componentTexts = [];
    private readonly List<(Grid Host, int Seconds)> slideHosts = [];
    private DispatcherTimer? timer;
    private TouchDevice? dragTouch;
    private Point dragStartScreen;
    private Point dragStartWindow;

    public ClassIslandPresentationService(ClassIslandTimetableService timetable, ClassIslandComponentService components, ClassIslandAppearanceService appearance, ClassIslandNotificationService notifications, ClassIslandWeatherService weather)
    {
        this.timetable = timetable; this.components = components; this.appearance = appearance; this.notifications = notifications; this.weather = weather;
        notifications.RequestStarted += (_, request) => ShowNotification(request);
        notifications.RequestCompleted += (_, request) => HideNotification(request);
        components.ComponentsChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke((Action)RebuildComponents);
        weather.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke((Action)RebuildComponents);
    }

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
            ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Manual
        };
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, (style | WsExToolWindow) & ~WsExAppWindow);
            ApplyAppearance();
        };
        window.DpiChanged += (_, _) => ApplyAppearance();
        window.MouseEnter += (_, _) => AnimateOpacity(true);
        window.MouseLeave += (_, _) => AnimateOpacity(false);
        island = new Border { Padding = new Thickness(17, 6, 17, 6), BorderThickness = new Thickness(1) };
        island.Cursor = Cursors.SizeAll;
        island.MouseLeftButtonDown += OnMouseLeftButtonDown;
        island.PreviewTouchDown += OnTouchDown;
        island.PreviewTouchMove += OnTouchMove;
        island.PreviewTouchUp += OnTouchUp;
        var grid = new Grid();
        defaultContent = grid;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeText = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var lesson = new StackPanel { Margin = new Thickness(15, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        lessonText = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        detailText = new TextBlock { FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        lesson.Children.Add(lessonText); lesson.Children.Add(detailText);
        Grid.SetColumn(lesson, 1); grid.Children.Add(timeText); grid.Children.Add(lesson);
        var layers = new Grid();
        layers.Children.Add(grid);
        componentContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        layers.Children.Add(componentContent);
        notificationText = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, FontSize = 16, FontWeight = FontWeights.SemiBold };
        notificationOverlay = new Border { Padding = new Thickness(18, 8, 18, 8), Visibility = Visibility.Collapsed,
            Child = notificationText };
        layers.Children.Add(notificationOverlay);
        island.Child = layers; window.Content = island;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Refresh();
        appearance.Changed += (_, _) => ApplyAppearance();
        RebuildComponents();
    }

    private void RebuildComponents()
    {
        if (componentContent is null || defaultContent is null) return;
        componentContent.Children.Clear();
        componentTexts.Clear();
        slideHosts.Clear();
        foreach (var line in components.CurrentComponents.Lines.Where(x => x.IsVisible))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var component in line.Children.Where(x => x.IsVisible))
            {
                if (BuildComponent(component, 0) is not { } presenter) continue;
                presenter.Margin = component.IsCustomMarginEnabled
                    ? new Thickness(component.MarginLeft, component.MarginTop, component.MarginRight, component.MarginBottom)
                    : new Thickness(row.Children.Count == 0 ? 0 : 14, 0, 0, 0);
                row.Children.Add(presenter);
            }
            if (row.Children.Count > 0) componentContent.Children.Add(row);
        }
        var hasComponents = componentContent.Children.Count > 0;
        componentContent.Visibility = hasComponents ? Visibility.Visible : Visibility.Collapsed;
        defaultContent.Visibility = hasComponents ? Visibility.Collapsed : Visibility.Visible;
        ApplyAppearance();
        Refresh();
    }

    private FrameworkElement? BuildComponent(ClassIslandComponentSettings component, int depth)
    {
        if (depth > 8 || !Guid.TryParse(component.Id, out var id)) return null;
        if (id == new Guid("AB0F26D5-9DF6-4575-B844-73B04D0907C1"))
            return new Border { Width = 1, Height = 20, Background = new SolidColorBrush(Color.FromArgb(100, 240, 245, 255)) };
        var group = id == new Guid("C911D762-107F-40C6-84CC-0146AB3C86B1");
        var rolling = id == new Guid("70FCD5EA-3FAE-4E06-ACA2-4F4DF47F9ACD");
        var stack = id == new Guid("2D849ECE-9F21-4C78-9434-415CFC283294");
        var slide = id == new Guid("7E19A113-D281-4F33-970A-834A0B78B5AD");
        if (group || rolling || stack || slide)
        {
            var children = ClassIslandComponentText.Children(component);
            if (children.Count == 0) return null;
            Panel host = stack || slide ? new Grid() : new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var child in children)
            {
                if (BuildComponent(child, depth + 1) is not { } element) continue;
                element.Margin = child.IsCustomMarginEnabled
                    ? new Thickness(child.MarginLeft, child.MarginTop, child.MarginRight, child.MarginBottom)
                    : new Thickness(host.Children.Count == 0 ? 0 : 12, 0, 0, 0);
                host.Children.Add(element);
            }
            if (host.Children.Count == 0) return null;
            if (slide)
            {
                var grid = (Grid)host;
                slideHosts.Add((grid, Math.Max(1, ReadSettingInt(component.Settings, "SlideSeconds", 15))));
            }
            if (rolling)
            {
                var speed = Math.Max(1, ReadSettingInt(component.Settings, "SpeedPixelPerSecond", 40));
                host.Loaded += (_, _) =>
                {
                    var transform = new TranslateTransform(); host.RenderTransform = transform;
                    var distance = Math.Max(1, host.ActualWidth);
                    transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -distance,
                        TimeSpan.FromSeconds(distance / speed)) { RepeatBehavior = RepeatBehavior.Forever });
                };
            }
            return host;
        }
        if (ClassIslandComponentText.Resolve(component, timetable, DateTime.Now, weather.Current) is null) return null;
        var block = new TextBlock
        {
            FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = component.IsMinWidthEnabled ? Math.Max(0, component.MinWidth) : 0,
            MaxWidth = component.IsFixedWidthEnabled ? Math.Max(40, component.FixedWidth)
                : component.IsMaxWidthEnabled ? Math.Max(40, component.MaxWidth) : 230
        };
        if (component.IsFixedWidthEnabled) block.Width = Math.Max(40, component.FixedWidth);
        componentTexts.Add((component, block));
        return block;
    }

    private static int ReadSettingInt(JsonElement? settings, string key, int fallback) =>
        settings is { ValueKind: JsonValueKind.Object } && settings.Value.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : fallback;

    private async void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (window is null || e.StylusDevice is not null) return;
        try { window.DragMove(); }
        catch (InvalidOperationException) { return; }
        await PersistDraggedPositionAsync();
    }

    private void OnTouchDown(object? sender, TouchEventArgs e)
    {
        if (window is null || dragTouch is not null) return;
        dragTouch = e.TouchDevice;
        dragStartScreen = window.PointToScreen(e.GetTouchPoint(window).Position);
        dragStartWindow = new Point(window.Left, window.Top);
        e.TouchDevice.Capture(island);
        e.Handled = true;
    }

    private void OnTouchMove(object? sender, TouchEventArgs e)
    {
        if (window is null || e.TouchDevice != dragTouch) return;
        var current = window.PointToScreen(e.GetTouchPoint(window).Position);
        window.Left = dragStartWindow.X + current.X - dragStartScreen.X;
        window.Top = dragStartWindow.Y + current.Y - dragStartScreen.Y;
        e.Handled = true;
    }

    private async void OnTouchUp(object? sender, TouchEventArgs e)
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
        var area = GetWorkArea(window, useSavedMonitor: false);
        var position = ClassIslandWindowPlacement.Clamp(new Point(window.Left, window.Top),
            new Size(window.Width, window.Height), area);
        window.Left = position.X;
        window.Top = position.Y;
        s.MonitorDeviceName = Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).DeviceName;
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
        foreach (var (component, text) in componentTexts)
            text.Text = ClassIslandComponentText.Resolve(component, timetable, now, weather.Current) ?? "";
        foreach (var (host, seconds) in slideHosts)
        {
            var active = (int)((now.Ticks / TimeSpan.TicksPerSecond / seconds) % host.Children.Count);
            for (var i = 0; i < host.Children.Count; i++)
                host.Children[i].Visibility = i == active ? Visibility.Visible : Visibility.Hidden;
        }
        timeText.Text = now.ToString(appearance.Settings.ShowSeconds ? "HH:mm:ss" : "HH:mm");
        var lessons = timetable.GetLessons(now);
        var current = lessons.FirstOrDefault(x => now.TimeOfDay >= x.Time.StartTime && now.TimeOfDay < x.Time.EndTime);
        var next = lessons.FirstOrDefault(x => x.Time.StartTime > now.TimeOfDay);
        if (current is not null)
        {
            lessonText!.Text = current.Subject.Name;
            detailText!.Text = $"{current.Time.StartTime:hh\\:mm}–{current.Time.EndTime:hh\\:mm}  {current.Subject.TeacherName}".Trim();
        }
        else if (next is not null)
        {
            lessonText!.Text = $"接下来 · {next.Subject.Name}";
            detailText!.Text = $"{next.Time.StartTime:hh\\:mm} 开始  {next.Subject.TeacherName}".Trim();
        }
        else { lessonText!.Text = lessons.Count == 0 ? "今天没有课程。" : "当前没有课程"; detailText!.Text = ""; }
    }

    private void ShowNotification(ClassIslandNotificationRequest request)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(() =>
        {
            if (notificationOverlay is null || notificationText is null) return;
            notificationText.Text = $"{request.MaskContent.Content}  {request.OverlayContent?.Content}".Trim();
            notificationOverlay.Visibility = Visibility.Visible;
        });
    }

    private void HideNotification(ClassIslandNotificationRequest request)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(() =>
        {
            if (notificationOverlay is not null && notifications.Current is null)
                notificationOverlay.Visibility = Visibility.Collapsed;
        });
    }

    private void ApplyAppearance()
    {
        if (window is null || island is null) return;
        var s = appearance.Settings;
        window.Width = s.Width * s.Scale;
        window.Height = Math.Max(s.Height, componentContent?.Children.Count * 28 + 12 ?? 0) * s.Scale;
        window.Topmost = s.Topmost;
        window.BeginAnimation(UIElement.OpacityProperty, null);
        window.Opacity = s.FadeOnPointerEnter && window.IsMouseOver ? s.HoverOpacity : s.Opacity;
        island.CornerRadius = new CornerRadius(Math.Min(s.CornerRadius * s.Scale, window.Height / 2));
        var light = s.IslandTheme == ClassIslandIslandTheme.LightGlass;
        var liquid = s.IslandTheme == ClassIslandIslandTheme.SqgtLiquidGlass;
        island.Background = liquid ? LiquidGlassSurface() : new SolidColorBrush(light ? Color.FromArgb(238, 242, 246, 252) : Color.FromArgb(234, 13, 15, 19));
        island.BorderBrush = liquid ? new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(245, 255, 255, 255), 0), new(Color.FromArgb(40, 46, 65, 85), 0.25),
            new(Color.FromArgb(223, 244, 252, 255), 0.465), new(Color.FromArgb(53, 56, 76, 96), 0.735),
            new(Color.FromArgb(234, 255, 255, 255), 1)
        }, new Point(0, 0), new Point(1, 1)) : new SolidColorBrush(light ? Color.FromArgb(120, 60, 70, 85) : Color.FromArgb(115, 220, 228, 240));
        timeText!.Foreground = new SolidColorBrush(light ? Color.FromRgb(38, 43, 51) : Colors.White);
        lessonText!.Foreground = timeText.Foreground;
        detailText!.Foreground = new SolidColorBrush(light ? Color.FromRgb(90, 98, 110) : Color.FromRgb(205, 212, 220));
        notificationText!.Foreground = timeText.Foreground;
        notificationOverlay!.Background = island.Background;
        foreach (var (_, block) in componentTexts) block.Foreground = timeText.Foreground;
        if (PresentationSource.FromVisual(window) is null) return;
        var area = GetWorkArea(window, useSavedMonitor: true);
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
        var position = ClassIslandWindowPlacement.Clamp(new Point(window.Left, window.Top),
            new Size(window.Width, window.Height), area);
        window.Left = position.X;
        window.Top = position.Y;
        island.Clip = new RectangleGeometry(new Rect(0, 0, window.Width, window.Height), island.CornerRadius.TopLeft, island.CornerRadius.TopLeft);
    }

    // WPF has no Avalonia ConicGradientBrush. The optical surface keeps the
    // upstream Crystal stops; the diagonal rim is a fallback for its conic edge.
    private static Brush LiquidGlassSurface()
    {
        var baseBrush = new SolidColorBrush(Color.FromArgb(195, 9, 17, 29));
        var sheen = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(48, 245, 250, 255), 0), new(Color.FromArgb(32, 246, 251, 255), 0.16),
            new(Color.FromArgb(20, 249, 252, 255), 0.4), new(Color.FromArgb(24, 247, 251, 255), 0.68),
            new(Color.FromArgb(44, 238, 247, 255), 1)
        }, new Point(0, 0), new Point(0, 1));
        var surface = new DrawingGroup();
        surface.Children.Add(new GeometryDrawing(baseBrush, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        surface.Children.Add(new GeometryDrawing(sheen, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        return new DrawingBrush(surface) { Stretch = Stretch.Fill };
    }

    private void AnimateOpacity(bool pointerInside)
    {
        if (window is null) return;
        var s = appearance.Settings;
        var target = pointerInside && s.FadeOnPointerEnter ? s.HoverOpacity : s.Opacity;
        window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd });
    }

    private Rect GetWorkArea(Window target, bool useSavedMonitor)
    {
        var handle = new WindowInteropHelper(target).Handle;
        var screen = useSavedMonitor
            ? Forms.Screen.AllScreens.FirstOrDefault(x => x.DeviceName == appearance.Settings.MonitorDeviceName)
            : null;
        screen ??= Forms.Screen.FromHandle(handle);
        var work = screen.WorkingArea;
        var transform = PresentationSource.FromVisual(target)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(work.Left, work.Top));
        var bottomRight = transform.Transform(new Point(work.Right, work.Bottom));
        return new Rect(topLeft, bottomRight);
    }
}
