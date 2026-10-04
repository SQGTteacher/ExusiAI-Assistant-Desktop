using System.Diagnostics;
using System.Runtime.InteropServices;
using ShapeEllipse = Avalonia.Controls.Shapes.Ellipse;
using ShapePath = Avalonia.Controls.Shapes.Path;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Forms = System.Windows.Forms;

namespace ExusiAI.Plugin.ClassIsland;

// A native Avalonia top-level owned by the plugin. The ExusiAI settings workbench
// stays on WPF's dispatcher; Avalonia runs its own message loop on a dedicated STA.
internal sealed class ClassIslandAvaloniaIsland : IDisposable
{
    private IBrush? darkSurface;
    private IBrush? lightSurface;
    private IBrush? darkEdge;
    private IBrush? lightEdge;
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
    private readonly ClassIslandSettingsService settings;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource lifetime = new();
    private Thread? thread;
    private Window? window;
    private Border? island;
    private StackPanel? lines;
    private readonly List<(ClassIslandComponentSettings Settings, TextBlock Text)> textViews = [];
    private readonly List<(Panel Host, int Seconds)> slideViews = [];
    private readonly List<(ClassIslandComponentSettings Settings, TextBlock Label, ShapePath? Ring, Border? Bar)> countdownViews = [];
    private TextBlock? defaultText;
    private TextBlock? notificationText;
    private DispatcherTimer? timer;
    private DispatcherTimer? dragEndTimer;
    private DispatcherTimer? opacityTimer;
    private long opacityStarted;
    private double opacityFrom;
    private double opacityTo;
    private bool pointerInside;
    private bool visible;
    private bool disposed;
    private bool dragging;

    internal ClassIslandAvaloniaIsland(ClassIslandTimetableService timetable, ClassIslandComponentService components,
        ClassIslandAppearanceService appearance, ClassIslandNotificationService notifications,
        ClassIslandWeatherService weather, ClassIslandSettingsService settings)
    {
        this.timetable = timetable;
        this.components = components;
        this.appearance = appearance;
        this.notifications = notifications;
        this.weather = weather;
        this.settings = settings;
        components.ComponentsChanged += OnComponentsChanged;
        appearance.Changed += OnChanged;
        weather.Changed += OnChanged;
        notifications.RequestStarted += OnNotification;
        notifications.RequestUpdated += OnNotification;
        notifications.RequestCompleted += OnNotification;
    }

    internal bool IsVisible => visible;

    internal bool Start()
    {
        if (disposed) return false;
        if (thread is null)
        {
            thread = new Thread(Run) { IsBackground = true, Name = "ClassIsland Avalonia island" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        if (!ready.Task.Wait(TimeSpan.FromSeconds(8)) || !ready.Task.Result) return false;
        Dispatcher.UIThread.Post(() =>
        {
            Refresh();
            window?.Show();
            timer?.Start();
            visible = true;
        });
        return true;
    }

    internal void Stop()
    {
        visible = false;
        if (ready.Task.IsCompletedSuccessfully && ready.Task.Result)
            Dispatcher.UIThread.Post(() => { timer?.Stop(); opacityTimer?.Stop(); window?.Hide(); });
    }

    internal void RefreshAppearance()
    {
        if (ready.Task.IsCompletedSuccessfully && ready.Task.Result)
            Dispatcher.UIThread.Post(Refresh);
    }

    private void OnChanged(object? sender, EventArgs e) => RefreshAppearance();
    private void OnComponentsChanged(object? sender, EventArgs e)
    {
        if (ready.Task.IsCompletedSuccessfully && ready.Task.Result)
            Dispatcher.UIThread.Post(() => { RebuildComponents(); Refresh(); });
    }
    private void OnNotification(object? sender, ClassIslandNotificationRequest e) => RefreshAppearance();

    private void Run()
    {
        try
        {
            AppBuilder.Configure<IslandApplication>().UsePlatformDetect().Start((app, _) =>
            {
                BuildWindow();
                ready.TrySetResult(true);
                app.Run(lifetime.Token);
                timer?.Stop();
                opacityTimer?.Stop();
                window?.Close();
            }, []);
        }
        catch
        {
            // The existing WPF renderer remains available when Avalonia cannot initialize.
            ready.TrySetResult(false);
        }
    }

    private void BuildWindow()
    {
        darkSurface = Surface(true);
        lightSurface = Surface(false);
        darkEdge = Edge(true);
        lightEdge = Edge(false);
        lines = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        island = new Border { Padding = new Thickness(17, 6), BorderThickness = new Thickness(1), Child = lines };
        window = new Window
        {
            SystemDecorations = SystemDecorations.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = island
        };
        window.Opened += (_, _) =>
        {
            var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
            if (handle == 0) return;
            var style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, (style | WsExToolWindow) & ~WsExAppWindow);
        };
        dragEndTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        dragEndTimer.Tick += async (_, _) =>
        {
            dragEndTimer.Stop();
            if (!dragging) return;
            dragging = false;
            await PersistPositionAsync();
        };
        window.PositionChanged += (_, _) =>
        {
            if (!dragging) return;
            dragEndTimer.Stop();
            dragEndTimer.Start();
        };
        island.PointerEntered += (_, _) => FadeOnPointer(true);
        island.PointerExited += (_, _) => FadeOnPointer(false);
        var fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        opacityTimer = fadeTimer;
        fadeTimer.Tick += (_, _) =>
        {
            if (window is null) return;
            var progress = Math.Clamp(Stopwatch.GetElapsedTime(opacityStarted).TotalMilliseconds / 180, 0, 1);
            var eased = 1 - (1 - progress) * (1 - progress);
            window.Opacity = opacityFrom + (opacityTo - opacityFrom) * eased;
            if (progress >= 1) fadeTimer.Stop();
        };
        island.PointerPressed += (_, e) =>
        {
            if (window is null || !e.GetCurrentPoint(island).Properties.IsLeftButtonPressed) return;
            dragging = true;
            window.BeginMoveDrag(e);
        };
        window.PointerReleased += async (_, _) =>
        {
            if (!dragging) return;
            dragEndTimer.Stop();
            dragging = false;
            await PersistPositionAsync();
        };
        RebuildComponents();
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Refresh();
    }

    private void RebuildComponents()
    {
        if (lines is null) return;
        lines.Children.Clear();
        textViews.Clear();
        countdownViews.Clear();
        slideViews.Clear();
        defaultText = null;
        notificationText = null;
        foreach (var line in components.CurrentComponents.Lines.Where(x => x.IsVisible))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var component in line.Children.Where(x => x.IsVisible))
            {
                if (BuildComponent(component, 0) is not { } view) continue;
                view.Margin = ComponentMargin(component, row.Children.Count, 14);
                row.Children.Add(view);
            }
            if (row.Children.Count > 0) lines.Children.Add(row);
        }
        if (lines.Children.Count == 0)
        {
            defaultText = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
            lines.Children.Add(defaultText);
        }
        var notificationLabel = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, IsVisible = false };
        notificationText = notificationLabel;
        notificationLabel.PointerPressed += (_, e) =>
        {
            if (notifications.Current is not { } request || !e.GetCurrentPoint(notificationLabel).Properties.IsLeftButtonPressed) return;
            request.Cancel();
            e.Handled = true;
        };
        lines.Children.Add(notificationText);
    }

    private Control? BuildComponent(ClassIslandComponentSettings component, int depth)
    {
        if (depth > 8 || !Guid.TryParse(component.Id, out var id)) return null;
        if (id == new Guid("7C645D35-8151-48BA-B4AC-15017460D994"))
            return BuildCountdown(component);
        if (id == new Guid("AB0F26D5-9DF6-4575-B844-73B04D0907C1"))
            return new Border { Width = 1, Height = 20, Background = new SolidColorBrush(Color.FromArgb(100, 240, 245, 255)) };
        var group = id == new Guid("C911D762-107F-40C6-84CC-0146AB3C86B1");
        var rolling = id == new Guid("70FCD5EA-3FAE-4E06-ACA2-4F4DF47F9ACD");
        var stack = id == new Guid("2D849ECE-9F21-4C78-9434-415CFC283294");
        var slide = id == new Guid("7E19A113-D281-4F33-970A-834A0B78B5AD");
        if (group || rolling || stack || slide)
        {
            var children = ClassIslandComponentText.Children(component);
            Panel host = stack || slide ? new Grid() : new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var child in children)
            {
                if (BuildComponent(child, depth + 1) is not { } view) continue;
                view.Margin = ComponentMargin(child, host.Children.Count, 12);
                host.Children.Add(view);
            }
            if (host.Children.Count == 0) return null;
            if (slide)
                slideViews.Add((host, Math.Max(1, ReadSeconds(component.Settings))));
            // The original rolling transition uses a compositor animation. Its
            // children retain their native layout until that transition is ported.
            return host;
        }
        if (ClassIslandComponentText.Resolve(component, timetable, DateTime.Now, weather.Current) is null) return null;
        var text = new TextBlock
        {
            FontSize = component.MainWindowBodyFontSize,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = component.Opacity
        };
        if (component.IsFixedWidthEnabled) text.Width = Math.Max(40, component.FixedWidth);
        else
        {
            if (component.IsMinWidthEnabled) text.MinWidth = Math.Max(0, component.MinWidth);
            if (component.IsMaxWidthEnabled) text.MaxWidth = Math.Max(40, component.MaxWidth);
        }
        textViews.Add((component, text));
        return text;
    }

    private Control? BuildCountdown(ClassIslandComponentSettings component)
    {
        if (ClassIslandComponentText.Resolve(component, timetable, DateTime.Now) is null) return null;
        var root = new StackPanel { Orientation = Orientation.Vertical, Opacity = component.Opacity };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        ShapePath? ring = null;
        Border? bar = null;
        if (ReadBool(component.Settings, "ShowProgress"))
        {
            if (ReadInt(component.Settings, "ProgressBarMode", 0) == 1)
            {
                bar = new Border { Width = 0, Height = 3, HorizontalAlignment = HorizontalAlignment.Left };
                var track = new Border { Width = 100, Height = 3, Background = new SolidColorBrush(Color.FromArgb(72, 218, 225, 235)), Child = bar };
                root.Children.Add(row);
                root.Children.Add(track);
            }
            else
            {
                var circle = new Grid { Width = 22, Height = 22, Margin = new Thickness(0, 0, 6, 0) };
                circle.Children.Add(new ShapeEllipse
                {
                    Width = 21.4, Height = 21.4, StrokeThickness = 2.6,
                    Stroke = new SolidColorBrush(Color.FromArgb(72, 218, 225, 235))
                });
                ring = new ShapePath { Width = 22, Height = 22, StrokeThickness = 3.1, StrokeLineCap = PenLineCap.Round };
                circle.Children.Add(ring);
                row.Children.Add(circle);
            }
        }
        if (bar is null) root.Children.Add(row);
        var label = new TextBlock
        {
            FontSize = ReadInt(component.Settings, "FontSize", (int)component.MainWindowBodyFontSize),
            FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(label);
        countdownViews.Add((component, label, ring, bar));
        return root;
    }

    private static bool ReadBool(JsonElement? settings, string key, bool fallback = false) =>
        settings is { ValueKind: JsonValueKind.Object } && settings.Value.TryGetProperty(key, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;

    private static int ReadInt(JsonElement? settings, string key, int fallback) =>
        settings is { ValueKind: JsonValueKind.Object } && settings.Value.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : fallback;

    private static string? ReadString(JsonElement? settings, string key) =>
        settings is { ValueKind: JsonValueKind.Object } && settings.Value.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double CountdownProgress(ClassIslandComponentSettings component, DateTime now)
    {
        if (!ClassIslandComponentText.TryGetCountdownWindow(component.Settings, now, out var start, out var end) || end <= start)
            return 0;
        var elapsed = ReadBool(component.Settings, "IsProgressInverted") ? end - now : now - start;
        return Math.Clamp(elapsed.TotalSeconds / (end - start).TotalSeconds, 0, .999999);
    }

    private static Geometry? ProgressArc(double fraction)
    {
        if (fraction <= 0) return null;
        var angle = fraction * Math.PI * 2 - Math.PI / 2;
        var x = 11 + 9.5 * Math.Cos(angle);
        var y = 11 + 9.5 * Math.Sin(angle);
        var figure = new PathFigure
        {
            StartPoint = new Point(11, 1.5),
            Segments = new PathSegments
            {
                new ArcSegment
                {
                    Point = new Point(x, y),
                    Size = new Size(9.5, 9.5),
                    IsLargeArc = fraction > .5,
                    SweepDirection = SweepDirection.Clockwise,
                    IsStroked = true
                }
            },
            IsClosed = false
        };
        var geometry = new PathGeometry();
        geometry.Figures?.Add(figure);
        return geometry;
    }

    private static Thickness ComponentMargin(ClassIslandComponentSettings component, int position, double spacing) =>
        component.IsCustomMarginEnabled
            ? new Thickness(component.MarginLeft, component.MarginTop, component.MarginRight, component.MarginBottom)
            : new Thickness(position == 0 ? 0 : spacing, 0, 0, 0);

    private static int ReadSeconds(JsonElement? settings) =>
        settings is { ValueKind: JsonValueKind.Object } && settings.Value.TryGetProperty("SlideSeconds", out var seconds)
            && seconds.ValueKind == JsonValueKind.Number && seconds.TryGetInt32(out var value) ? value : 15;

    private static IBrush Surface(bool dark)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
        };
        var stops = dark
            ? new[] { (0d, "#50091729"), (.16, "#3B0A1625"), (.4, "#2A08121E"), (.68, "#30091424"), (1d, "#480C1C30") }
            : new[] { (0d, "#30F5FAFF"), (.16, "#20F6FBFF"), (.4, "#14F9FCFF"), (.68, "#18F7FBFF"), (1d, "#2CEEF7FF") };
        foreach (var (offset, color) in stops)
            brush.GradientStops.Add(new GradientStop(Color.Parse(color), offset));
        return brush;
    }

    private static IBrush Edge(bool dark)
    {
        // Avalonia supports the upstream conic edge directly, without WPF's
        // hundreds of sampled drawing wedges.
        var brush = new ConicGradientBrush { Center = new RelativePoint(.5, .5, RelativeUnit.Relative) };
        var stops = dark
            ? new[] { (0d, "#EFFFFFFF"), (.145, "#687F9EB5"), (.25, "#2616293E"), (.465, "#D0E4F6FF"), (.515, "#EEFFFFFF"), (.735, "#2511263F"), (.95, "#DDEEFFFE"), (1d, "#EFFFFFFF") }
            : new[] { (0d, "#F5FFFFFF"), (.145, "#65758CA3"), (.25, "#282E4155"), (.465, "#DFF4FCFF"), (.515, "#F8FFFFFF"), (.735, "#35384C60"), (.95, "#EAFFFFFF"), (1d, "#F5FFFFFF") };
        foreach (var (offset, color) in stops)
            brush.GradientStops.Add(new GradientStop(Color.Parse(color), offset));
        return brush;
    }

    private void FadeOnPointer(bool inside)
    {
        pointerInside = inside;
        if (window is null || opacityTimer is null) return;
        var s = appearance.Settings;
        opacityFrom = window.Opacity;
        opacityTo = inside && s.FadeOnPointerEnter ? s.HoverOpacity : s.Opacity;
        opacityStarted = Stopwatch.GetTimestamp();
        opacityTimer.Start();
    }

    private void Refresh()
    {
        if (lines is null || island is null || window is null) return;
        var s = appearance.Settings;
        var light = s.IslandTheme is ClassIslandIslandTheme.LightGlass or ClassIslandIslandTheme.SqgtLiquidGlassLight;
        var ink = new SolidColorBrush(light ? Color.FromRgb(38, 43, 51) : Colors.White);
        var now = DateTime.Now.AddSeconds(settings.TimeOffsetSeconds);
        foreach (var (component, block) in textViews)
        {
            block.Text = ClassIslandComponentText.Resolve(component, timetable, now, weather.Current) ?? "";
            block.Foreground = ink;
        }
        foreach (var (component, label, ring, bar) in countdownViews)
        {
            label.Text = ClassIslandComponentText.Resolve(component, timetable, now) ?? "";
            IBrush accent;
            try { accent = new SolidColorBrush(Color.Parse(ReadString(component.Settings, "FontColor") ?? "#FFFF0000")); }
            catch (FormatException) { accent = new SolidColorBrush(Colors.Red); }
            label.Foreground = accent;
            var progress = CountdownProgress(component, now);
            if (ring is not null)
            {
                ring.Stroke = ReadBool(component.Settings, "UseAccentOnProgressBar", true) ? accent : ink;
                ring.Data = ProgressArc(progress);
            }
            if (bar is not null)
            {
                bar.Width = progress * 100;
                bar.Background = ReadBool(component.Settings, "UseAccentOnProgressBar", true) ? accent : ink;
            }
        }
        foreach (var (host, seconds) in slideViews)
        {
            var selected = (int)((now.Ticks / TimeSpan.TicksPerSecond / seconds) % host.Children.Count);
            for (var index = 0; index < host.Children.Count; index++)
                host.Children[index].IsVisible = index == selected;
        }
        var notification = notifications.Current;
        if (notificationText is not null)
        {
            notificationText.Text = (notification is { MaskSession.IsCompleted: true, OverlayContent: { } overlay }
                ? overlay.Content : notification?.MaskContent.Content)?.ToString() ?? "";
            notificationText.Foreground = ink;
            notificationText.IsVisible = notification is not null;
        }
        foreach (var row in lines.Children)
            if (row != notificationText) row.IsVisible = notification is null;
        if (defaultText is not null)
        {
            var lessons = timetable.GetLessons(now);
            var current = lessons.FirstOrDefault(x => now.TimeOfDay >= x.Time.StartTime && now.TimeOfDay < x.Time.EndTime);
            var next = lessons.FirstOrDefault(x => x.Time.StartTime > now.TimeOfDay);
            defaultText.Text = $"{now.ToString(s.ShowSeconds ? "HH:mm:ss" : "HH:mm")}    {current?.Subject.Name ?? (next is null ? "今天没有课程。" : $"接下来 · {next.Subject.Name}")}";
            defaultText.Foreground = ink;
        }
        var liquid = s.IslandTheme is ClassIslandIslandTheme.SqgtLiquidGlass or ClassIslandIslandTheme.SqgtLiquidGlassLight;
        island.Background = liquid ? light ? lightSurface : darkSurface :
            new SolidColorBrush(light ? Color.FromArgb(238, 242, 246, 252) : Color.FromArgb(234, 13, 15, 19));
        island.BorderBrush = liquid ? light ? lightEdge : darkEdge :
            new SolidColorBrush(light ? Color.FromArgb(120, 60, 70, 85) : Color.FromArgb(115, 220, 228, 240));
        var dpi = window.RenderScaling;
        var maximumWidth = s.Width * s.Scale / dpi;
        lines.Measure(new Size(Math.Max(40, maximumWidth - 36), double.PositiveInfinity));
        window.Width = Math.Min(maximumWidth, Math.Max(1, lines.DesiredSize.Width + 36));
        window.Height = Math.Max(s.Height * s.Scale / dpi, lines.DesiredSize.Height + 14);
        island.CornerRadius = new CornerRadius(Math.Min(s.CornerRadius * s.Scale / dpi, window.Height / 2));
        window.Topmost = s.Topmost;
        if (opacityTimer?.IsEnabled != true)
            window.Opacity = pointerInside && s.FadeOnPointerEnter ? s.HoverOpacity : s.Opacity;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(x => x.DeviceName == s.MonitorDeviceName)
            ?? Forms.Screen.PrimaryScreen;
        if (screen is null) return;
        var area = screen.WorkingArea;
        var widthPx = (int)Math.Ceiling(window.Width * dpi);
        var heightPx = (int)Math.Ceiling(window.Height * dpi);
        var x = s.DockPosition switch
        {
            ClassIslandDockPosition.TopLeft or ClassIslandDockPosition.BottomLeft => area.Left + (int)s.OffsetX,
            ClassIslandDockPosition.TopRight or ClassIslandDockPosition.BottomRight => area.Right - widthPx - (int)s.OffsetX,
            _ => area.Left + (area.Width - widthPx) / 2 + (int)s.OffsetX
        };
        var y = s.DockPosition is ClassIslandDockPosition.BottomLeft or ClassIslandDockPosition.BottomCenter or ClassIslandDockPosition.BottomRight
            ? area.Bottom - heightPx - (int)s.OffsetY : area.Top + (int)s.OffsetY;
        if (!dragging)
            window.Position = new PixelPoint(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - widthPx)),
                Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - heightPx)));
    }

    private async Task PersistPositionAsync()
    {
        if (window is null) return;
        var s = appearance.Settings;
        var handle = window.TryGetPlatformHandle()?.Handle ?? 0;
        var screen = handle == 0 ? Forms.Screen.PrimaryScreen : Forms.Screen.FromHandle(handle);
        if (screen is null) return;
        var area = screen.WorkingArea;
        var position = window.Position;
        var width = (int)Math.Ceiling(window.Width * window.RenderScaling);
        var height = (int)Math.Ceiling(window.Height * window.RenderScaling);
        s.MonitorDeviceName = screen.DeviceName;
        s.OffsetX = s.DockPosition switch
        {
            ClassIslandDockPosition.TopLeft or ClassIslandDockPosition.BottomLeft => position.X - area.Left,
            ClassIslandDockPosition.TopRight or ClassIslandDockPosition.BottomRight => area.Right - width - position.X,
            _ => position.X - (area.Left + (area.Width - width) / 2)
        };
        s.OffsetY = s.DockPosition is ClassIslandDockPosition.BottomLeft or ClassIslandDockPosition.BottomCenter or ClassIslandDockPosition.BottomRight
            ? area.Bottom - height - position.Y : position.Y - area.Top;
        await appearance.SaveAsync();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        components.ComponentsChanged -= OnComponentsChanged;
        appearance.Changed -= OnChanged;
        weather.Changed -= OnChanged;
        notifications.RequestStarted -= OnNotification;
        notifications.RequestUpdated -= OnNotification;
        notifications.RequestCompleted -= OnNotification;
        Stop();
        lifetime.Cancel();
    }

    public sealed class IslandApplication : Application { }
}
