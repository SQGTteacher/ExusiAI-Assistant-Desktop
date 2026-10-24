using ClassIsland.Platforms.Abstraction.Services;
using Microsoft.Win32;

namespace ClassIsland.Platform.Windows.Services;

public class SystemEventsService : ISystemEventsService, IDisposable
{
    private int _disposed;
    public event EventHandler? TimeChanged;

    public SystemEventsService()
    {
        
        SystemEvents.TimeChanged += OnTimeChanged;
    }

    private void OnTimeChanged(object? sender, EventArgs args)
    {
        if (Volatile.Read(ref _disposed) == 0) TimeChanged?.Invoke(sender, args);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        SystemEvents.TimeChanged -= OnTimeChanged;
        TimeChanged = null;
        GC.SuppressFinalize(this);
    }
    
}
