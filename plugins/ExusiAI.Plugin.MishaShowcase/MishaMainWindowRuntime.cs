using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class MishaMainWindowRuntime : IDisposable
{
    private readonly MishaPlatformStore store;
    private readonly IExtensionLogger logger;
    private readonly DispatcherTimer timer;
    private readonly List<Action<DateTime>> tickers = [];
    private readonly HashSet<string> warnedComponents = new(StringComparer.OrdinalIgnoreCase);
    private MishaMainWindow? window;
    private bool started;
    private bool refreshing;

    public MishaMainWindowRuntime(MishaPlatformStore store, IExtensionLogger logger)
    {
        this.store = store;
        this.logger = logger;
        timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        if (started) return;
        started = true;
        store.Changed += Store_OnChanged;
        timer.Start();
        _ = RefreshAsync();
    }

    public void Dispose()
    {
        if (!started) return;
        started = false;
        timer.Stop();
        store.Changed -= Store_OnChanged;
        window?.CloseForShutdown();
        window = null;
        tickers.Clear();
    }

    private void Store_OnChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.CheckAccess()) _ = RefreshAsync();
        else _ = dispatcher.InvokeAsync(() => _ = RefreshAsync());
    }

    private async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            var workspace = store.Workspace;
            if (workspace is null || !workspace.GetBool("IsMainWindowVisible", true))
            {
                window?.Hide();
                return;
            }

            var path = workspace.CurrentComponentLayoutPath;
            if (path is null || !File.Exists(path))
            {
                window?.Hide();
                return;
            }

            var layout = await ClassIslandComponentLayoutDocument.LoadAsync(path);
            tickers.Clear();
            warnedComponents.Clear();
            var content = MishaNativeMainWindowRenderer.Build(
                store,
                layout,
                tickers,
                id =>
                {
                    if (warnedComponents.Add(id))
                        logger.Warning($"ClassIsland component {id} is not available in the current Misha runtime and was skipped without a placeholder.");
                });

            window ??= new MishaMainWindow();
            window.ApplySettings(workspace);
            window.Content = content;
            window.UpdateLayout();
            window.Reposition(workspace);
            if (MishaNativeMainWindowRenderer.ShouldShow(store, workspace, GetClassIslandNow(workspace)))
                window.ShowWithoutActivation();
            else
                window.Hide();

            Tick();
        }
        catch (Exception exception)
        {
            logger.Error("ClassIsland main-window runtime could not refresh.", exception);
            window?.Hide();
        }
        finally
        {
            refreshing = false;
        }
    }

    private void Tick()
    {
        var workspace = store.Workspace;
        if (workspace is null) return;
        var now = GetClassIslandNow(workspace);

        foreach (var ticker in tickers.ToArray())
        {
            try { ticker(now); }
            catch (Exception exception) { logger.Error("A ClassIsland main-window component update failed.", exception); }
        }

        if (window is null) return;
        var shouldShow = workspace.GetBool("IsMainWindowVisible", true)
                         && MishaNativeMainWindowRenderer.ShouldShow(store, workspace, now);
        if (shouldShow && !window.IsVisible)
            window.ShowWithoutActivation();
        else if (!shouldShow && window.IsVisible)
            window.Hide();
    }

    internal static DateTime GetClassIslandNow(ClassIslandWorkspace workspace)
    {
        var offset = workspace.GetDouble("TimeOffsetSeconds") + workspace.GetDouble("DebugTimeOffsetSeconds");
        return DateTime.Now.AddSeconds(offset);
    }
}

internal sealed class MishaMainWindow : Window
{
    private bool shuttingDown;

    public MishaMainWindow()
    {
        Title = "ClassIsland";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        SourceInitialized += (_, _) => ApplyExtendedStyle();
        SizeChanged += (_, _) =>
        {
            if (Tag is ClassIslandWorkspace workspace)
                Dispatcher.BeginInvoke(() => Reposition(workspace), DispatcherPriority.Loaded);
        };
        Closing += (_, e) =>
        {
            if (shuttingDown) return;
            e.Cancel = true;
            Hide();
        };
    }

    public void ApplySettings(ClassIslandWorkspace workspace)
    {
        Tag = workspace;
        Topmost = workspace.GetInt("WindowLayer", 1) > 0;
        IsHitTestVisible = workspace.GetBool("IsMouseClickingEnabled", false);
        ApplyExtendedStyle();
    }

    public void ShowWithoutActivation()
    {
        if (!IsVisible) Show();
    }

    public void CloseForShutdown()
    {
        shuttingDown = true;
        Close();
    }

    public void Reposition(ClassIslandWorkspace workspace)
    {
        var monitors = NativeMonitor.GetMonitors();
        if (monitors.Count == 0) return;

        var index = Math.Clamp(workspace.GetInt("WindowDockingMonitorIndex"), 0, monitors.Count - 1);
        var monitor = monitors[index];
        var areaPx = workspace.GetBool("IsIgnoreWorkAreaEnabled")
            ? monitor.Bounds
            : monitor.WorkArea;

        var dpi = VisualTreeHelper.GetDpi(this);
        var area = new Rect(
            areaPx.X / dpi.DpiScaleX,
            areaPx.Y / dpi.DpiScaleY,
            areaPx.Width / dpi.DpiScaleX,
            areaPx.Height / dpi.DpiScaleY);

        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);
        var docking = Math.Clamp(workspace.GetInt("WindowDockingLocation", 1), 0, 5);
        var x = docking % 3 switch
        {
            0 => area.Left,
            1 => area.Left + (area.Width - width) / 2,
            _ => area.Right - width
        };
        var y = docking < 3 ? area.Top : area.Bottom - height;
        Left = x + workspace.GetInt("WindowDockingOffsetX");
        Top = y + workspace.GetInt("WindowDockingOffsetY");
    }

    private void ApplyExtendedStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        const int gwlExStyle = -20;
        const int wsExTransparent = 0x20;
        const int wsExToolWindow = 0x80;
        const int wsExNoActivate = 0x08000000;

        var style = GetWindowLong(handle, gwlExStyle);
        style |= wsExToolWindow | wsExNoActivate;
        if (IsHitTestVisible) style &= ~wsExTransparent;
        else style |= wsExTransparent;
        _ = SetWindowLong(handle, gwlExStyle, style);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}

internal static class NativeMonitor
{
    internal sealed record MonitorBounds(Int32Rect Bounds, Int32Rect WorkArea);

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdcMonitor, IntPtr monitorRect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public Int32Rect ToInt32Rect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public RectNative Monitor;
        public RectNative Work;
        public uint Flags;
    }

    public static IReadOnlyList<MonitorBounds> GetMonitors()
    {
        var monitors = new List<MonitorBounds>();
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
                monitors.Add(new MonitorBounds(info.Monitor.ToInt32Rect(), info.Work.ToInt32Rect()));
            return true;
        };

        _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return monitors;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}

internal static class MishaNativeMainWindowRenderer
{
    private const string DateId = "df3f8295-21f6-482e-bada-fa0e5f14bb66";
    private const string ScheduleId = "1db2017d-e374-4bc6-9d57-0b4adf03a6b8";
    private const string ClockId = "9e1af71d-8f77-4b21-a342-448787104dd9";
    private const string WeatherId = "ca495086-e297-4beb-9603-c5c1c1a8551e";
    private const string CountdownId = "7c645d35-8151-48ba-b4ac-15017460d994";
    private const string TextId = "ee8f66bd-c423-4e7c-ab46-aa9976b00e08";
    private const string SeparatorId = "ab0f26d5-9df6-4575-b844-73b04d0907c1";
    private const string GroupId = "c911d762-107f-40c6-84cc-0146ab3c86b1";
    private const string StackId = "2d849ece-9f21-4c78-9434-415cfc283294";
    private const string SlideId = "7e19a113-d281-4f33-970a-834a0b78b5ad";
    private const string RollingId = "70fcd5ea-3fae-4e06-aca2-4f4df47f9acd";

    public static FrameworkElement Build(
        MishaPlatformStore store,
        ClassIslandComponentLayoutDocument layout,
        List<Action<DateTime>> tickers,
        Action<string> warnUnsupported)
    {
        var workspace = store.Workspace ?? throw new InvalidOperationException("ClassIsland 工作区尚未导入。");
        var scale = Math.Max(0.25, workspace.GetDouble("Scale", 1));
        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            LayoutTransform = new ScaleTransform(scale, scale)
        };

        var font = workspace.GetString("MainWindowFont");
        if (!string.IsNullOrWhiteSpace(font))
        {
            try { TextElement.SetFontFamily(root, new FontFamily(font)); }
            catch { }
        }
        TextElement.SetFontWeight(root, FontWeight.FromOpenTypeWeight(Math.Clamp(workspace.GetInt("MainWindowFontWeight2", 500), 1, 999)));
        TextElement.SetForeground(root, ResolveForeground(workspace, null));

        var lineMargin = Math.Max(0, workspace.GetDouble("MainWindowLineVerticalMargin", 5));
        var separated = workspace.GetBool("IsIslandSeperated");
        foreach (var line in layout.Lines)
        {
            var linePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                MinHeight = 40
            };

            foreach (var component in line.Components)
            {
                var rendered = CreateComponent(store, component.Node, workspace, tickers, warnUnsupported);
                if (rendered is null) continue;
                var view = ApplyComponentLayout(rendered, component.Node, workspace);
                linePanel.Children.Add(separated
                    ? WrapIsland(view, workspace, component.Node)
                    : view);
            }

            if (linePanel.Children.Count == 0) continue;
            FrameworkElement lineView = separated
                ? linePanel
                : WrapIsland(linePanel, workspace, line.Node);
            lineView.Opacity = Math.Clamp(ClassIslandComponentLayoutDocument.ReadDouble(line.Node, "Opacity", 1), 0, 1);
            lineView.Margin = new Thickness(0, 0, 0, lineMargin);
            root.Children.Add(lineView);
        }

        return root;
    }

    public static bool ShouldShow(MishaPlatformStore store, ClassIslandWorkspace workspace, DateTime now)
    {
        if (!workspace.GetBool("HideOnClass")) return true;
        var profile = store.Profile;
        if (profile is null) return true;
        var lessons = profile.GetLessonsForDate(now.Date, store.ResolveRotationWeek(now.Date));
        return !lessons.Any(x => now.TimeOfDay >= x.Start && now.TimeOfDay < x.End);
    }

    private static FrameworkElement? CreateComponent(
        MishaPlatformStore store,
        JsonObject node,
        ClassIslandWorkspace workspace,
        List<Action<DateTime>> tickers,
        Action<string> warnUnsupported)
    {
        var id = ReadString(node, "Id").ToLowerInvariant();
        var settings = node["Settings"] as JsonObject;
        FrameworkElement? element = id switch
        {
            DateId => CreateDate(workspace, tickers),
            ClockId => CreateClock(settings, workspace, tickers),
            ScheduleId => CreateSchedule(store, settings, workspace, tickers),
            TextId => CreateText(settings, workspace),
            CountdownId => CreateCountdown(store, settings, workspace, tickers),
            SeparatorId => CreateSeparator(workspace),
            WeatherId => CreateWeather(workspace),
            GroupId => CreateContainer(store, settings, workspace, tickers, warnUnsupported, false),
            StackId => CreateContainer(store, settings, workspace, tickers, warnUnsupported, true),
            SlideId => CreateSlide(store, settings, workspace, tickers, warnUnsupported),
            RollingId => CreateRolling(store, settings, workspace, tickers, warnUnsupported),
            _ => null
        };

        if (element is null && !string.IsNullOrWhiteSpace(id))
            warnUnsupported(id);
        return element;
    }

    private static FrameworkElement CreateDate(ClassIslandWorkspace workspace, List<Action<DateTime>> tickers)
    {
        var text = BaseText(workspace.GetDouble("MainWindowBodyFontSize", 16));
        void Update(DateTime now) => text.Text = now.ToString("ddd MM/dd", CultureInfo.CurrentCulture);
        tickers.Add(Update);
        Update(MishaMainWindowRuntime.GetClassIslandNow(workspace));
        return text;
    }

    private static FrameworkElement CreateClock(JsonObject? settings, ClassIslandWorkspace workspace, List<Action<DateTime>> tickers)
    {
        var text = BaseText(workspace.GetDouble("MainWindowEmphasizedFontSize", 18));
        var showSeconds = ReadBool(settings, "ShowSeconds");
        var showRealTime = ReadBool(settings, "ShowRealTime");
        var flash = ReadBool(settings, "FlashTimeSeparator", true);
        void Update(DateTime adjusted)
        {
            var now = showRealTime ? DateTime.Now : adjusted;
            var value = now.ToString(showSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
            if (flash && !showSeconds && now.Second % 2 == 0)
                value = value.Replace(':', ' ');
            text.Text = value;
        }
        tickers.Add(Update);
        Update(MishaMainWindowRuntime.GetClassIslandNow(workspace));
        return text;
    }

    private static FrameworkElement? CreateSchedule(
        MishaPlatformStore store,
        JsonObject? settings,
        ClassIslandWorkspace workspace,
        List<Action<DateTime>> tickers)
    {
        if (store.Profile is null) return null;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void Update(DateTime now)
        {
            panel.Children.Clear();
            var lessons = store.Profile.GetLessonsForDate(now.Date, store.ResolveRotationWeek(now.Date));
            var current = lessons.FirstOrDefault(x => now.TimeOfDay >= x.Start && now.TimeOfDay < x.End);
            var hideFinished = ReadBool(settings, "HideFinishedClass");
            var currentOnly = ReadBool(settings, "ShowCurrentLessonOnlyOnClass");
            IEnumerable<ClassIslandLessonSnapshot> visible = lessons;
            if (hideFinished) visible = visible.Where(x => x.End >= now.TimeOfDay);
            if (currentOnly && current is not null) visible = visible.Where(x => x == current);

            var items = visible.ToArray();
            if (items.Length == 0)
            {
                if (ReadBool(settings, "ShowPlaceholderOnEmptyClassPlan", true))
                {
                    var placeholder = lessons.Count == 0
                        ? ReadString(settings, "PlaceholderTextNoClass", "今天没有课程。")
                        : ReadString(settings, "PlaceholderTextAllClassEnded", "今日课程已全部结束。");
                    panel.Children.Add(BaseText(workspace.GetDouble("MainWindowBodyFontSize", 16), placeholder));
                }
                return;
            }

            foreach (var lesson in items)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 6, 0) };
                var subject = BaseText(workspace.GetDouble("MainWindowBodyFontSize", 16), lesson.Subject);
                if (current is not null && lesson == current)
                    subject.FontWeight = FontWeights.SemiBold;
                item.Children.Add(subject);
                if (!string.IsNullOrWhiteSpace(lesson.Teacher))
                {
                    var teacher = BaseText(workspace.GetDouble("MainWindowSecondaryFontSize", 14), "  " + lesson.Teacher);
                    teacher.Opacity = 0.72;
                    item.Children.Add(teacher);
                }
                panel.Children.Add(item);
            }
        }
        tickers.Add(Update);
        Update(MishaMainWindowRuntime.GetClassIslandNow(workspace));
        return panel;
    }

    private static FrameworkElement CreateText(JsonObject? settings, ClassIslandWorkspace workspace)
    {
        var text = BaseText(ReadDouble(settings, "FontSize", 16), ReadString(settings, "TextContent"));
        if (ReadBool(settings, "UseCustomFontColor", true))
            text.Foreground = ParseBrush(settings?["FontColor"], ResolveForeground(workspace, null));
        return text;
    }

    private static FrameworkElement CreateCountdown(
        MishaPlatformStore store,
        JsonObject? settings,
        ClassIslandWorkspace workspace,
        List<Action<DateTime>> tickers)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var prefix = BaseText(workspace.GetDouble("MainWindowBodyFontSize", 16));
        var value = BaseText(ReadDouble(settings, "FontSize", 16));
        value.Foreground = ParseBrush(settings?["FontColor"], ResolveForeground(workspace, null));
        panel.Children.Add(prefix);
        panel.Children.Add(value);

        void Update(DateTime now)
        {
            var range = ResolveCountdownRange(store, settings, workspace, now);
            var delta = range.end - now;
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
            var format = ReadString(settings, "CustomStringFormat", "%D天");
            var total = range.end - range.start;
            var ratio = total <= TimeSpan.Zero ? 0 : Math.Clamp((total - delta).TotalSeconds / total.TotalSeconds, 0, 1);
            var formatted = format
                .Replace("%D", Math.Ceiling(delta.TotalDays).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("%H", Math.Ceiling(delta.TotalHours).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("%M", Math.Ceiling(delta.TotalMinutes).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("%S", Math.Ceiling(delta.TotalSeconds).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("%P", ratio.ToString("P0", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("%p", ratio.ToString("P2", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("%d", delta.Days.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("%h", delta.Hours.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("%m", delta.Minutes.ToString("00", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("%s", delta.Seconds.ToString("00", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("%x", delta.Milliseconds.ToString("000", CultureInfo.InvariantCulture), StringComparison.Ordinal);

            var compact = ReadBool(settings, "IsCompactModeEnabled");
            var name = ReadString(settings, "CountDownName", "倒计时");
            var connector = ReadString(settings, "CountDownConnector", "还有");
            prefix.Text = compact ? name : $"距离{name}{connector}";
            value.Text = formatted;
        }

        tickers.Add(Update);
        Update(MishaMainWindowRuntime.GetClassIslandNow(workspace));
        return panel;
    }

    private static (DateTime start, DateTime end) ResolveCountdownRange(
        MishaPlatformStore store,
        JsonObject? settings,
        ClassIslandWorkspace workspace,
        DateTime now)
    {
        var source = ReadInt(settings, "CountdownSource");
        if (source == 0)
            return (ReadDateTime(settings, "StartTime", now.Date), ReadDateTime(settings, "OverTime", now.Date));
        if (source == 1)
        {
            var cycleStart = ReadDateTime(settings, "CycleStartTime", now);
            var before = ReadBool(settings, "IsAdvancedCycleTimingEnabled") ? ReadTimeSpan(settings, "CycleBeforeDuration") : TimeSpan.Zero;
            var duration = ReadTimeSpan(settings, "CycleDuration", TimeSpan.FromDays(1));
            var after = ReadBool(settings, "IsAdvancedCycleTimingEnabled") ? ReadTimeSpan(settings, "CycleAfterDuration") : TimeSpan.Zero;
            var total = before + duration + after;
            if (total <= TimeSpan.Zero) return (cycleStart, cycleStart);
            var cycles = Math.Max(0, (int)Math.Floor((now - cycleStart).TotalSeconds / total.TotalSeconds));
            if (ReadBool(settings, "IsCycleCountLimited"))
                cycles = Math.Min(cycles, Math.Max(0, ReadInt(settings, "CycleCountLimit", 2)));
            var start = cycleStart + TimeSpan.FromTicks(total.Ticks * cycles) + before;
            return (start, start + duration);
        }
        if (source == 2)
        {
            var lessons = store.Profile?.GetLessonsForDate(now.Date, store.ResolveRotationWeek(now.Date)) ?? [];
            if (lessons.Count > 0) return (now.Date + lessons.First().Start, now.Date + lessons.Last().End);
            return (now.Date, now.Date.AddDays(1));
        }

        var startDay = ReadBool(settings, "IsCustomWeekCountdownStartDayEnabled")
            ? (DayOfWeek)Math.Clamp(ReadInt(settings, "WeekCountdownStartDay", (int)DayOfWeek.Monday), 0, 6)
            : ParseDayOfWeek(workspace.GetString("SingleWeekStartTime"), DayOfWeek.Sunday);
        var weekStart = now.Date.AddDays(-((7 + (int)now.DayOfWeek - (int)startDay) % 7));
        return (weekStart, weekStart.AddDays(7));
    }

    private static FrameworkElement CreateSeparator(ClassIslandWorkspace workspace) =>
        new Border
        {
            Width = 1,
            Height = Math.Max(18, workspace.GetDouble("MainWindowBodyFontSize", 16) + 4),
            Background = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
            Margin = new Thickness(6, 0, 6, 0)
        };

    private static FrameworkElement? CreateWeather(ClassIslandWorkspace workspace)
    {
        var current = workspace.Settings["LastWeatherInfo"]?["Current"];
        var temperature = current?["Temperature"];
        var value = NodeString(temperature?["Value"]);
        var unit = NodeString(temperature?["Unit"]);
        if (string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(unit)) return null;
        return BaseText(workspace.GetDouble("MainWindowBodyFontSize", 16), value + unit);
    }

    private static FrameworkElement CreateContainer(
        MishaPlatformStore store,
        JsonObject? settings,
        ClassIslandWorkspace workspace,
        List<Action<DateTime>> tickers,
        Action<string> warnUnsupported,
        bool overlay)
    {
        Panel panel = overlay
            ? new Grid()
            : new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in Children(settings))
        {
            var view = CreateComponent(store, child, workspace, tickers, warnUnsupported);
            if (view is not null) panel.Children.Add(ApplyComponentLayout(view, child, workspace));
        }
        return panel;
    }

    private static FrameworkElement CreateSlide(
        MishaPlatformStore store,
        JsonObject? settings,
        ClassIslandWorkspace workspace,
        List<Action<DateTime>> tickers,
        Action<string> warnUnsupported)
    {
        var children = Children(settings).ToArray();
        var host = new ContentControl();
        if (children.Length == 0) return host;
        var views = children.Select(x => CreateComponent(store, x, workspace, tickers, warnUnsupported))
            .Where(x => x is not null).Cast<FrameworkElement>().ToArray();
        if (views.Length == 0) return host;

        var seconds = Math.Max(1, ReadDouble(settings, "SlideSeconds", 15));
        var mode = ReadInt(settings, "SlideMode");
        var started = MishaMainWindowRuntime.GetClassIslandNow(workspace);
        var previousIndex = -1;
        void Update(DateTime now)
        {
            var raw = (int)Math.Floor((now - started).TotalSeconds / seconds);
            var index = mode switch
            {
                2 when views.Length > 1 => Math.Abs(raw % ((views.Length - 1) * 2) - (views.Length - 1)),
                _ => Math.Abs(raw) % views.Length
            };
            if (mode == 1)
            {
                var seed = now.Date.GetHashCode() ^ raw;
                index = new Random(seed).Next(views.Length);
            }
            if (index == previousIndex) return;
            previousIndex = index;
            host.Content = views[index];
        }
        tickers.Add(Update);
        Update(started);
        return host;
    }

    private static FrameworkElement CreateRolling(
        MishaPlatformStore store,
        JsonObject? settings,
        ClassIslandWorkspace workspace,
        List<Action<DateTime>> tickers,
        Action<string> warnUnsupported)
    {
        var viewport = new Border { ClipToBounds = true };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in Children(settings))
        {
            var view = CreateComponent(store, child, workspace, tickers, warnUnsupported);
            if (view is not null) panel.Children.Add(ApplyComponentLayout(view, child, workspace));
        }
        viewport.Child = panel;

        var transform = new TranslateTransform();
        panel.RenderTransform = transform;
        var speed = Math.Max(1, ReadDouble(settings, "SpeedPixelPerSecond", 40));
        var started = MishaMainWindowRuntime.GetClassIslandNow(workspace);
        tickers.Add(now =>
        {
            if (panel.ActualWidth <= 0 || viewport.ActualWidth <= 0) return;
            var distance = Math.Max(panel.ActualWidth + 16, viewport.ActualWidth);
            transform.X = -(((now - started).TotalSeconds * speed) % distance);
        });
        return viewport;
    }

    private static IEnumerable<JsonObject> Children(JsonObject? settings) =>
        settings?["Children"] is JsonArray array ? array.OfType<JsonObject>() : [];

    private static FrameworkElement ApplyComponentLayout(FrameworkElement view, JsonObject node, ClassIslandWorkspace workspace)
    {
        var container = new Border { Child = view, VerticalAlignment = VerticalAlignment.Center };
        container.Opacity = Math.Clamp(ReadDouble(node, "Opacity", 1), 0, 1);
        container.Margin = ReadBool(node, "IsCustomMarginEnabled")
            ? new Thickness(
                ReadDouble(node, "MarginLeft"),
                ReadDouble(node, "MarginTop"),
                ReadDouble(node, "MarginRight"),
                ReadDouble(node, "MarginBottom"))
            : new Thickness(6, 0, 6, 0);

        if (ReadBool(node, "IsFixedWidthEnabled"))
            container.Width = Math.Max(0, ReadDouble(node, "FixedWidth", 200));
        else
        {
            if (ReadBool(node, "IsMinWidthEnabled")) container.MinWidth = Math.Max(0, ReadDouble(node, "MinWidth", 100));
            if (ReadBool(node, "IsMaxWidthEnabled")) container.MaxWidth = Math.Max(container.MinWidth, ReadDouble(node, "MaxWidth", 300));
        }

        if (ReadBool(node, "IsCustomForegroundColorEnabled"))
            TextElement.SetForeground(container, ParseBrush(node["ForegroundColor"], ResolveForeground(workspace, null)));
        return container;
    }

    private static Border WrapIsland(FrameworkElement child, ClassIslandWorkspace workspace, JsonObject? overrides)
    {
        var opacity = ReadBool(overrides, "IsCustomBackgroundOpacityEnabled")
            ? ReadDouble(overrides, "BackgroundOpacity", workspace.GetDouble("Opacity", 0.5))
            : workspace.GetDouble("Opacity", 0.5);
        var color = ReadBool(overrides, "IsCustomBackgroundColorEnabled")
            ? ParseColor(overrides?["BackgroundColor"], Colors.Black)
            : ParseColor(workspace.Settings["BackgroundColor"], Colors.Black);
        var radius = ReadBool(overrides, "IsCustomCornerRadiusEnabled")
            ? ReadDouble(overrides, "CustomCornerRadius", workspace.GetDouble("RadiusX", 8))
            : workspace.GetDouble("RadiusX", 8);

        return new Border
        {
            Child = child,
            Background = new SolidColorBrush(color) { Opacity = Math.Clamp(opacity, 0, 1) },
            CornerRadius = new CornerRadius(Math.Max(0, radius)),
            Padding = new Thickness(8, 4, 8, 4)
        };
    }

    private static TextBlock BaseText(double fontSize, string text = "") =>
        new()
        {
            Text = text,
            FontSize = Math.Max(8, fontSize),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        };

    private static Brush ResolveForeground(ClassIslandWorkspace workspace, JsonObject? node)
    {
        if (node is not null && ReadBool(node, "IsCustomForegroundColorEnabled"))
            return ParseBrush(node["ForegroundColor"], Brushes.White);
        if (workspace.GetBool("IsCustomForegroundColorEnabled"))
            return ParseBrush(workspace.Settings["CustomForegroundColor"], Brushes.White);
        return Brushes.White;
    }

    private static Brush ParseBrush(JsonNode? node, Brush fallback) =>
        new SolidColorBrush(ParseColor(node, fallback is SolidColorBrush solid ? solid.Color : Colors.White));

    private static Color ParseColor(JsonNode? node, Color fallback)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
        {
            try
            {
                var parsed = ColorConverter.ConvertFromString(text);
                if (parsed is Color color) return color;
            }
            catch { }
        }
        if (node is JsonObject obj)
        {
            var a = Math.Clamp(ReadInt(obj, "A", 255), 0, 255);
            var r = Math.Clamp(ReadInt(obj, "R"), 0, 255);
            var g = Math.Clamp(ReadInt(obj, "G"), 0, 255);
            var b = Math.Clamp(ReadInt(obj, "B"), 0, 255);
            return Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);
        }
        return fallback;
    }

    private static string ReadString(JsonObject? node, string key, string fallback = "") =>
        node is null ? fallback : NodeString(node[key], fallback);

    private static string NodeString(JsonNode? node, string fallback = "")
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text ?? fallback;
            return node.ToJsonString().Trim('"');
        }
        return fallback;
    }

    private static bool ReadBool(JsonObject? node, string key, bool fallback = false)
    {
        if (node?[key] is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var result)) return result;
            if (value.TryGetValue<string>(out var text) && bool.TryParse(text, out result)) return result;
        }
        return fallback;
    }

    private static int ReadInt(JsonObject? node, string key, int fallback = 0)
    {
        if (node?[key] is JsonValue value)
        {
            if (value.TryGetValue<int>(out var result)) return result;
            if (value.TryGetValue<double>(out var number)) return (int)number;
            if (value.TryGetValue<string>(out var text) && int.TryParse(text, out result)) return result;
        }
        return fallback;
    }

    private static double ReadDouble(JsonObject? node, string key, double fallback = 0)
    {
        if (node?[key] is JsonValue value)
        {
            if (value.TryGetValue<double>(out var result)) return result;
            if (value.TryGetValue<int>(out var integer)) return integer;
            if (value.TryGetValue<string>(out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) return result;
        }
        return fallback;
    }

    private static DateTime ReadDateTime(JsonObject? node, string key, DateTime fallback)
    {
        var text = ReadString(node, key);
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value) ? value : fallback;
    }

    private static TimeSpan ReadTimeSpan(JsonObject? node, string key, TimeSpan fallback = default)
    {
        var text = ReadString(node, key);
        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static DayOfWeek ParseDayOfWeek(string text, DayOfWeek fallback) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value.DayOfWeek
            : fallback;
}
