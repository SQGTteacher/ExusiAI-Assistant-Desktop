using System;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.Services;

public class HangService : ObservableRecipient, IHangService, IDisposable
{
    private readonly object _gate = new();
    private bool _disposed;
    private bool _isHang = false;
    private bool _isChecking = false;
    private const int CHECK_INTERVAL_MS = 500;
    private const int HANG_MS = 100;


    public bool IsHang
    {
        get => _isHang;
        set
        {
            if (value == _isHang) return;
            _isHang = value;
            OnPropertyChanged();
        }
    }

    public bool IsChecking
    {
        get => _isChecking;
        set
        {
            if (value == _isChecking) return;
            _isChecking = value;
            OnPropertyChanged();
        }
    }

    private Timer Timer { get; } = new()
    {
        Interval = CHECK_INTERVAL_MS
    };

    public HangService()
    {
        Timer.Elapsed += TimerOnElapsed;
        Timer.Start();
    }

    public void AssumeHang()
    {
        lock (_gate)
        {
            if (_disposed) return;
            IsHang = true;
        }
        _ = CheckDispatcherHangAsync();
    }

    private async void TimerOnElapsed(object? sender, ElapsedEventArgs e)
    {
        await CheckDispatcherHangAsync();
    }

    private async Task CheckDispatcherHangAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disposed || IsChecking) return;
            IsChecking = true;
            _ = Dispatcher.UIThread.InvokeAsync(() => completion.TrySetResult(true));
        }
        await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromMilliseconds(HANG_MS)));
        lock (_gate)
        {
            if (_disposed) return;
            IsChecking = false;
            IsHang = !completion.Task.IsCompleted;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Timer.Stop();
            Timer.Elapsed -= TimerOnElapsed;
            Timer.Dispose();
            IsChecking = false;
        }
    }
}
