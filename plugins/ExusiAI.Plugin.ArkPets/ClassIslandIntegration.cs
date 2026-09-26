using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Plugin.ArkPets;

internal sealed record ClassIslandPetLesson(
    string PlanId,
    string PlanName,
    int Index,
    string Subject,
    string Teacher,
    TimeSpan Start,
    TimeSpan End);

internal sealed record ClassIslandPetState(
    DateOnly Date,
    string Phase,
    DateTimeOffset GeneratedAt,
    ClassIslandPetLesson? Current,
    ClassIslandPetLesson? Previous,
    ClassIslandPetLesson? Next)
{
    public string TransitionKey =>
        $"{Date:yyyy-MM-dd}|{Phase}|{Current?.PlanId}|{Current?.Index}|{Next?.PlanId}|{Next?.Index}";
}

internal static class ClassIslandStateFile
{
    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExusiAI", "integration", "classisland-state.json");

    public static ClassIslandPetState? Read(string? path = null)
    {
        var target = path ?? DefaultPath;
        if (!File.Exists(target)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(target));
            var root = document.RootElement;
            if (!root.TryGetProperty("Available", out var available) || available.ValueKind != JsonValueKind.True)
                return null;
            if (!root.TryGetProperty("Date", out var dateNode) ||
                !DateOnly.TryParseExact(dateNode.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return null;

            var phase = root.TryGetProperty("Phase", out var phaseNode) ? phaseNode.GetString() ?? "" : "";
            var generatedAt = root.TryGetProperty("GeneratedAt", out var generatedNode) &&
                              DateTimeOffset.TryParse(generatedNode.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;

            return new(date, phase, generatedAt, ReadLesson(root, "Current"), ReadLesson(root, "Previous"), ReadLesson(root, "Next"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static ClassIslandPetLesson? ReadLesson(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Object)
            return null;

        var start = TimeSpan.TryParse(ReadString(node, "Start"), CultureInfo.InvariantCulture, out var startValue)
            ? startValue
            : TimeSpan.Zero;
        var end = TimeSpan.TryParse(ReadString(node, "End"), CultureInfo.InvariantCulture, out var endValue)
            ? endValue
            : TimeSpan.Zero;
        return new(
            ReadString(node, "PlanId"),
            ReadString(node, "PlanName"),
            node.TryGetProperty("Index", out var indexNode) && indexNode.TryGetInt32(out var index) ? index : 0,
            ReadString(node, "Subject"),
            ReadString(node, "Teacher"),
            start,
            end);
    }

    private static string ReadString(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}

internal sealed class ClassIslandIntegrationBridge : IAsyncDisposable
{
    private readonly ArkPetsController controller;
    private readonly IExtensionLogger logger;
    private readonly CancellationTokenSource stop = new();
    private readonly PetReminderPresenter presenter = new();
    private Task? loop;
    private string? lastTransitionKey;

    public ClassIslandIntegrationBridge(ArkPetsController controller, IExtensionLogger logger)
    {
        this.controller = controller;
        this.logger = logger;
    }

    public void Start()
    {
        loop ??= Task.Run(RunAsync);
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        if (loop is not null)
        {
            try { await loop; }
            catch (OperationCanceledException) { }
        }
        await presenter.DisposeAsync();
        stop.Dispose();
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stop.Token))
        {
            var state = ClassIslandStateFile.Read();
            if (state is null)
            {
                lastTransitionKey = null;
                continue;
            }

            if (lastTransitionKey is null)
            {
                lastTransitionKey = state.TransitionKey;
                continue;
            }

            if (string.Equals(lastTransitionKey, state.TransitionKey, StringComparison.Ordinal))
                continue;

            lastTransitionKey = state.TransitionKey;
            await HandleTransitionAsync(state, stop.Token);
        }
    }

    private async Task HandleTransitionAsync(ClassIslandPetState state, CancellationToken cancellationToken)
    {
        if (controller.Settings.ClassIslandRemindersEnabled)
        {
            var (title, body) = state.Phase switch
            {
                "OnClass" when state.Current is not null =>
                    ("上课", string.IsNullOrWhiteSpace(state.Current.Teacher)
                        ? state.Current.Subject
                        : $"{state.Current.Subject} · {state.Current.Teacher}"),
                "Breaking" =>
                    ("下课", state.Next is null ? "课间休息" : $"下一节：{state.Next.Subject}"),
                "AfterSchool" => ("课程结束", "今天的课程已经全部结束"),
                _ => ("", "")
            };
            if (!string.IsNullOrWhiteSpace(title))
                await presenter.ShowAsync(title, body, controller.SelectedModel?.Name ?? "ArkPets");
        }

        if (controller.Settings.OrganizeDesktopDuringBreaks &&
            string.Equals(state.Phase, "Breaking", StringComparison.Ordinal) &&
            state.Previous is not null)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var moved = await DesktopLessonOrganizer.OrganizeAsync(desktop, state, cancellationToken);
            if (moved > 0)
                logger.Information($"ArkPets ClassIsland integration organized {moved} desktop file(s) after {state.Previous.Subject}.");
        }
    }
}

internal static class DesktopLessonOrganizer
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ppt", ".pptx", ".doc", ".docx", ".xls", ".xlsx", ".pdf",
        ".txt", ".md", ".rtf", ".csv", ".png", ".jpg", ".jpeg", ".webp"
    };

    public static Task<int> OrganizeAsync(
        string desktopDirectory,
        ClassIslandPetState state,
        CancellationToken cancellationToken = default)
    {
        if (state.Previous is null || !Directory.Exists(desktopDirectory))
            return Task.FromResult(0);

        var lessonStart = state.Date.ToDateTime(TimeOnly.FromTimeSpan(state.Previous.Start)).AddMinutes(-5);
        var observedAt = state.GeneratedAt == DateTimeOffset.MinValue ? DateTime.Now : state.GeneratedAt.LocalDateTime;
        var targetRoot = Path.Combine(desktopDirectory, "ExusiAI 课堂整理");
        var subject = Sanitize(string.IsNullOrWhiteSpace(state.Previous.Subject) ? "课堂" : state.Previous.Subject);
        var target = Path.Combine(targetRoot, state.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), subject);
        Directory.CreateDirectory(target);

        var moved = 0;
        var log = new StringBuilder();
        foreach (var source in Directory.EnumerateFiles(desktopDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(source);
            if (!AllowedExtensions.Contains(extension)) continue;

            try
            {
                var info = new FileInfo(source);
                if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0)
                    continue;
                if (info.LastWriteTime < lessonStart || info.LastWriteTime > observedAt.AddMinutes(2))
                    continue;

                var destination = UniqueDestination(target, info.Name);
                File.Move(source, destination);
                moved++;
                log.AppendLine($"{DateTimeOffset.Now:O}\t{source}\t{destination}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Locked classroom files stay in place and can be retried next break.
            }
        }

        if (log.Length > 0)
            File.AppendAllText(Path.Combine(target, "整理记录.txt"), log.ToString(), Encoding.UTF8);
        return Task.FromResult(moved);
    }

    private static string UniqueDestination(string directory, string fileName)
    {
        var destination = Path.Combine(directory, fileName);
        if (!File.Exists(destination)) return destination;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            destination = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(destination)) return destination;
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "课堂" : result;
    }
}

internal sealed class PetReminderPresenter : IAsyncDisposable
{
    private Window? window;
    private DispatcherTimer? closeTimer;

    public Task ShowAsync(string title, string body, string petName)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return Task.CompletedTask;
        return dispatcher.InvokeAsync(() =>
        {
            window ??= CreateWindow();
            if (window.Content is not Border { Child: StackPanel panel }) return;
            ((TextBlock)panel.Children[0]).Text = $"{petName} · {title}";
            ((TextBlock)panel.Children[1]).Text = body;
            window.Left = SystemParameters.WorkArea.Right - window.Width - 18;
            window.Top = SystemParameters.WorkArea.Bottom - window.Height - 18;
            if (!window.IsVisible) window.Show();

            closeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            closeTimer.Tick -= CloseTimer_OnTick;
            closeTimer.Tick += CloseTimer_OnTick;
            closeTimer.Stop();
            closeTimer.Start();
        }).Task;
    }

    public async ValueTask DisposeAsync()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        await dispatcher.InvokeAsync(() =>
        {
            closeTimer?.Stop();
            window?.Close();
            window = null;
        });
    }

    private void CloseTimer_OnTick(object? sender, EventArgs e)
    {
        closeTimer?.Stop();
        window?.Hide();
    }

    private static Window CreateWindow()
    {
        var title = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
        var body = new TextBlock { FontSize = 13, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        panel.Children.Add(title);
        panel.Children.Add(body);
        return new Window
        {
            Width = 330,
            Height = 90,
            Content = new Border
            {
                Child = panel,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(242, 42, 82, 140)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            },
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false
        };
    }
}
