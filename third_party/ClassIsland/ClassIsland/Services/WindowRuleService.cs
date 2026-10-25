using System;
using ClassIsland.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using ClassIsland.Core.Helpers.Native;
using ClassIsland.Core.Models.Ruleset;
using ClassIsland.Core;
using ClassIsland.Models.Rules;
using Avalonia.Threading;
using ClassIsland.Platforms.Abstraction;
using ClassIsland.Platforms.Abstraction.Models;
using ClassIsland.Platforms.Abstraction.Services;

namespace ClassIsland.Services;

public class WindowRuleService : IWindowRuleService, IDisposable
{
    public ILogger<WindowRuleService> Logger { get; }
    public IRulesetService RulesetService { get; }

    public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundWindowChanged;

    private readonly IWindowPlatformService _windowPlatformService;
    private readonly EventHandler<ForegroundWindowChangedEventArgs> _foregroundWindowChangedHandler;
    private readonly Dictionary<string, RuleRegistryInfo.HandleDelegate> _ruleHandlers;
    private bool _isMoving = false;

    public WindowRuleService(ILogger<WindowRuleService> logger, IRulesetService rulesetService)
    {
        Logger = logger;
        RulesetService = rulesetService;
        _windowPlatformService = PlatformServices.WindowPlatformService;
        _foregroundWindowChangedHandler = (_, e) => ForegroundWindowChanged?.Invoke(this, e);
        _ruleHandlers = new()
        {
            ["classisland.windows.className"] = ClassNameHandler,
            ["classisland.windows.text"] = TextHandler,
            ["classisland.windows.status"] = StatusHandler,
            ["classisland.windows.processName"] = ProcessNameHandler
        };

        ForegroundWindowChanged += ((_, _) => RulesetService.NotifyStatusChanged());
        _windowPlatformService.RegisterForegroundWindowChangedEvent(_foregroundWindowChangedHandler);

        foreach (var (id, handler) in _ruleHandlers)
        {
            RulesetService.RegisterRuleHandler(id, handler);
        }
    }

    public void Dispose()
    {
        _windowPlatformService.UnregisterForegroundWindowChangedEvent(_foregroundWindowChangedHandler);
        foreach (var (id, handler) in _ruleHandlers)
        {
            if (IRulesetService.Rules.TryGetValue(id, out var info))
            {
                info.Handle -= handler;
            }
        }
    }

    private unsafe bool ProcessNameHandler(object? settings)
    {
        if (settings is not StringMatchingSettings s) return false;
        var pid = PlatformServices.WindowPlatformService.GetWindowPid(PlatformServices.WindowPlatformService
            .ForegroundWindowHandle);
        if (pid == 0)
        {
            return false;
        }
        try
        {
            var process = Process.GetProcessById((int)pid);
            return s.IsMatching(process.ProcessName);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "无法获取 pid 为 {} 的进程信息", pid);
            return false;
        }
    }

    private bool StatusHandler(object? settings)
    {
        if (settings is not WindowStatusRuleSettings s) return false;
        if (AppBase.Current.MainWindow is not MainWindow)
        {
            return false;
        }
        var mw = App.GetService<MainWindow>();
        var screen = mw.GetSelectedScreenSafe();
        if (screen == null)
        {
            return false;
        }

        var fullscreen = PlatformServices.WindowPlatformService.IsForegroundWindowFullscreen(screen);
        var maximize = PlatformServices.WindowPlatformService.IsForegroundWindowMaximized(screen);
        var minimize = PlatformServices.WindowPlatformService.IsWindowMinimized(PlatformServices.WindowPlatformService
            .ForegroundWindowHandle);
        return s.State switch
        {
            0 => !(fullscreen || maximize || minimize),
            1 => maximize && !fullscreen,
            2 => minimize,
            3 => fullscreen,
            _ => false
        };
    }

    private bool TextHandler(object? settings)
    {
        if (settings is not StringMatchingSettings s) return false;
        return s.IsMatching(PlatformServices.WindowPlatformService.GetWindowTitle(PlatformServices.WindowPlatformService
            .ForegroundWindowHandle));
    }

    private bool ClassNameHandler(object? settings)
    {
        if (settings is not StringMatchingSettings s) return false;
        return s.IsMatching(PlatformServices.WindowPlatformService.GetWindowClassName(PlatformServices.WindowPlatformService
            .ForegroundWindowHandle));
    }


    public unsafe bool IsForegroundWindowClassIsland()
    {
        var pid = PlatformServices.WindowPlatformService.GetWindowPid(PlatformServices.WindowPlatformService
            .ForegroundWindowHandle);
        var process = Process.GetProcessById(pid);
        return process.Id == Environment.ProcessId;
    }
}