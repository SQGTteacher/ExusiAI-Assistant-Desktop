namespace ExusiAI.Plugin.ClassIsland;

public enum ClassIslandNotificationKind
{
    Information,
    Warning,
    Error,
    ClassStarting,
    ClassEnding
}

public sealed class ClassIslandNotification : EventArgs
{
    public ClassIslandNotification(ClassIslandNotificationKind kind, string title, string message, DateTimeOffset createdAt, object? context = null)
    {
        Kind = kind;
        Title = title;
        Message = message;
        CreatedAt = createdAt;
        Context = context;
    }

    public ClassIslandNotificationKind Kind { get; }
    public string Title { get; }
    public string Message { get; }
    public DateTimeOffset CreatedAt { get; }
    public object? Context { get; }
}

public sealed class ClassIslandNotificationService
{
    private readonly Queue<ClassIslandNotification> history = new();
    private readonly object sync = new();

    public int HistoryLimit { get; init; } = 100;
    public event EventHandler<ClassIslandNotification>? Published;

    public IReadOnlyList<ClassIslandNotification> History
    {
        get { lock (sync) return history.ToArray(); }
    }

    public void Publish(ClassIslandNotificationKind kind, string title, string message, object? context = null)
    {
        var notification = new ClassIslandNotification(kind, title, message, DateTimeOffset.Now, context);
        lock (sync)
        {
            history.Enqueue(notification);
            while (history.Count > Math.Max(1, HistoryLimit)) history.Dequeue();
        }
        Published?.Invoke(this, notification);
    }
}
