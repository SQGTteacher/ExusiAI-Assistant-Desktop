using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Models.Automation.Triggers;
using Microsoft.Extensions.Logging;
using TimeCrontab;

namespace ClassIsland.Services.Automation.Triggers;

[TriggerInfo("classisland.cron", "cron", "\ue125")]
public class CronTrigger : TriggerBase<CronTriggerSettings>, IAsyncDisposable
{
    private CancellationTokenSource? _stopCancellationTokenSource;
    private readonly List<Task> _workers = [];
    private bool _loaded;

    ILogger<CronTrigger> Logger { get; } = App.GetService<ILogger<CronTrigger>>();
    IExactTimeService ExactTimeService { get; } = App.GetService<IExactTimeService>();

    public override void Loaded()
    {
        if (_loaded) return;
        _loaded = true;
        Settings.PropertyChanged += SettingsOnPropertyChanged;
        LoadCron();
    }

    private async Task CronWorker(Crontab crontab, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = ExactTimeService.GetCurrentLocalDateTime();
                await Task.Delay(crontab.GetSleepTimeSpan(now), cancellationToken);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Reload/unload may happen while this callback is queued.
                    // Each worker must retain its own cancellation token.
                    if (!cancellationToken.IsCancellationRequested) Trigger();
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { Logger.LogError(exception, "cron 调度失败。"); }
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e) => LoadCron();

    private void LoadCron()
    {
        if (!_loaded) return;
        CancelWorker();
        try
        {
            var crontab = Crontab.Parse(Settings.CronExpression);
            var now = ExactTimeService.GetCurrentLocalDateTime();
            Logger.LogInformation("cron 表达式解析成功，下次触发时间：{}", crontab.GetNextOccurrence(now).ToLongTimeString());
            _stopCancellationTokenSource = new CancellationTokenSource();
            var token = _stopCancellationTokenSource.Token;
            _workers.RemoveAll(task => task.IsCompletedSuccessfully);
            _workers.Add(Task.Run(() => CronWorker(crontab, token)));
        }
        catch (TimeCrontabException) { Logger.LogWarning("cron 表达式解析失败。"); }
    }

    public override void UnLoaded()
    {
        _loaded = false;
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        CancelWorker();
    }

    private void CancelWorker()
    {
        var cancellation = _stopCancellationTokenSource;
        _stopCancellationTokenSource = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        UnLoaded();
        await Task.WhenAll(_workers);
    }
}
