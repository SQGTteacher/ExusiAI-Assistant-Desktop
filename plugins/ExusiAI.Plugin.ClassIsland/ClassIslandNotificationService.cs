using System.Collections.Concurrent;
using System.Diagnostics;

namespace ExusiAI.Plugin.ClassIsland;

public enum ClassIslandNotificationKind { Information, Warning, Error, ClassStarting, ClassEnding }
public enum ClassIslandNotificationState { None, Queued, Playing, Paused, Cancelled, Completed }

public sealed class ClassIslandNotificationSettings : ClassIslandJsonModel
{
    public bool IsNotificationEnabled { get; set; } = true;
    public bool IsSpeechEnabled { get; set; }
    public bool IsNotificationEffectEnabled { get; set; } = true;
    public bool IsNotificationSoundEnabled { get; set; }
    public string NotificationSoundPath { get; set; } = "";
    public bool IsNotificationTopmostEnabled { get; set; }
    public bool IsSettingsEnabled { get; set; }
}

public sealed class ClassIslandNotificationContent : ClassIslandJsonModel
{
    public object? Content { get; set; }
    public object? ContentTemplateResourceKey { get; set; }
    public bool IsSpeechEnabled { get; set; } = true;
    public string SpeechContent { get; set; } = "";
    public string? Color { get; set; }
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(5);
    public DateTime? EndTime { get; set; }
}

public sealed class ClassIslandNotificationSession
{
    public DateTime SessionStartTime { get; internal set; }
    public DateTime CurrentTicketStartTime { get; internal set; }
    public TimeSpan SessionPlayedTime { get; internal set; }
    public bool IsExplicitEndTime { get; internal set; }
    public bool IsCompleted { get; internal set; }
    internal Stopwatch TimingStopwatch { get; } = new();
}

public sealed class ClassIslandNotificationRequest : EventArgs
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly CancellationTokenSource completed = new();

    public ClassIslandNotificationContent MaskContent { get; set; } = new();
    public ClassIslandNotificationContent? OverlayContent { get; set; }
    public ClassIslandNotificationSettings RequestNotificationSettings { get; set; } = new();
    public Guid ChannelId { get; set; }
    public ClassIslandNotificationState State { get; internal set; }
    public double LeftProgress { get; internal set; } = 1;
    public ClassIslandNotificationSession MaskSession { get; } = new();
    public ClassIslandNotificationSession OverlaySession { get; } = new();
    public CancellationToken CancellationToken => cancellation.Token;
    public CancellationToken CompletedToken => completed.Token;
    public event EventHandler? Canceled;
    public event EventHandler? Completed;

    public ClassIslandNotificationRequest()
    {
        cancellation.Token.Register(() => Canceled?.Invoke(this, EventArgs.Empty));
        completed.Token.Register(() => Completed?.Invoke(this, EventArgs.Empty));
    }

    public void Cancel() => cancellation.Cancel();
    internal void MarkCompleted() => completed.Cancel();
}

public sealed class ClassIslandNotification : EventArgs
{
    public ClassIslandNotification(ClassIslandNotificationKind kind, string title, string message, DateTimeOffset createdAt, object? context = null)
    { Kind = kind; Title = title; Message = message; CreatedAt = createdAt; Context = context; }
    public ClassIslandNotificationKind Kind { get; }
    public string Title { get; }
    public string Message { get; }
    public DateTimeOffset CreatedAt { get; }
    public object? Context { get; }
}

public sealed class ClassIslandNotificationService : IAsyncDisposable
{
    private readonly ConcurrentQueue<ClassIslandNotificationRequest> queue = new();
    private readonly SemaphoreSlim signal = new(0);
    private readonly Queue<ClassIslandNotification> history = new();
    private readonly object sync = new();
    private CancellationTokenSource? lifetime;
    private Task? worker;

    public int HistoryLimit { get; init; } = 100;
    public ClassIslandNotificationRequest? Current { get; private set; }
    public IReadOnlyList<ClassIslandNotification> History { get { lock (sync) return history.ToArray(); } }
    public event EventHandler<ClassIslandNotification>? Published;
    public event EventHandler<ClassIslandNotificationRequest>? RequestStarted;
    public event EventHandler<ClassIslandNotificationRequest>? RequestCompleted;

    public void Start(CancellationToken cancellationToken = default)
    {
        if (worker is { IsCompleted: false }) return;
        cancellationToken.ThrowIfCancellationRequested();
        lifetime = new CancellationTokenSource();
        worker = RunAsync(lifetime.Token);
    }

    public async Task StopAsync()
    {
        if (lifetime is null) return;
        await lifetime.CancelAsync().ConfigureAwait(false);
        signal.Release();
        if (worker is not null) try { await worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        lifetime.Dispose(); lifetime = null; worker = null;
    }

    public void Enqueue(ClassIslandNotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.State != ClassIslandNotificationState.None) throw new InvalidOperationException("提醒请求已经进入生命周期。");
        request.State = ClassIslandNotificationState.Queued;
        queue.Enqueue(request); signal.Release();
    }

    public void Publish(ClassIslandNotificationKind kind, string title, string message, object? context = null)
    {
        var notification = new ClassIslandNotification(kind, title, message, DateTimeOffset.Now, context);
        lock (sync) { history.Enqueue(notification); while (history.Count > Math.Max(1, HistoryLimit)) history.Dequeue(); }
        Published?.Invoke(this, notification);
        Enqueue(new()
        {
            MaskContent = new() { Content = title, SpeechContent = title },
            OverlayContent = new() { Content = message, SpeechContent = message }
        });
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!queue.TryDequeue(out var request)) continue;
            Current = request;
            try
            {
                request.State = ClassIslandNotificationState.Playing; RequestStarted?.Invoke(this, request);
                await PlayAsync(request.MaskContent, request.MaskSession, request, cancellationToken).ConfigureAwait(false);
                if (request.OverlayContent is { } overlay)
                    await PlayAsync(overlay, request.OverlaySession, request, cancellationToken).ConfigureAwait(false);
                request.State = ClassIslandNotificationState.Completed; request.LeftProgress = 0; request.MarkCompleted();
            }
            catch (OperationCanceledException)
            {
                request.State = request.CancellationToken.IsCancellationRequested
                    ? ClassIslandNotificationState.Cancelled
                    : ClassIslandNotificationState.Paused;
                if (cancellationToken.IsCancellationRequested) throw;
            }
            finally { Current = null; RequestCompleted?.Invoke(this, request); }
        }
    }

    private static async Task PlayAsync(ClassIslandNotificationContent content, ClassIslandNotificationSession session, ClassIslandNotificationRequest request, CancellationToken hostToken)
    {
        var now = DateTime.Now;
        var duration = content.EndTime is { } end ? end - now : content.Duration;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        session.SessionStartTime = session.SessionStartTime == default ? now : session.SessionStartTime;
        session.CurrentTicketStartTime = now; session.IsExplicitEndTime = content.EndTime is not null; session.TimingStopwatch.Restart();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(hostToken, request.CancellationToken);
        try
        {
            var step = TimeSpan.FromMilliseconds(50);
            var remaining = duration;
            while (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining < step ? remaining : step, linked.Token).ConfigureAwait(false);
                remaining = duration - session.TimingStopwatch.Elapsed;
                request.LeftProgress = duration == TimeSpan.Zero ? 0 : Math.Clamp(remaining / duration, 0, 1);
            }
            session.IsCompleted = true;
        }
        finally { session.SessionPlayedTime += session.TimingStopwatch.Elapsed; session.TimingStopwatch.Reset(); }
    }

    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(false); signal.Dispose(); }
}
