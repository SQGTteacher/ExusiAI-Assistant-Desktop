using System.Collections;
using System.Reflection;

namespace ExusiAI.Desktop;

internal static partial class ClassIslandVerification
{
    private sealed record SessionProbe(object Automation, object Settings, object Hang, object Trigger,
        object CronSettings, object TriggerSettings, object ActionSet, object Action, Task[] Workers);

    private static object? Property(object source, string name) => source.GetType()
        .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source);

    private static void SetProperty(object source, string name, object value) => source.GetType()
        .GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(source, value);

    private static Task[] CronWorkers(object trigger) => trigger.GetType().GetField("_workers",
        BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(trigger) is IEnumerable workers
        ? workers.Cast<Task>().ToArray() : [];

    private static SessionProbe PrepareSessionProbe(Type entry, object app)
    {
        var services = GetEmbeddedServices(entry);
        var core = SharedAssembly(entry, "ClassIsland.Core");
        var native = SharedAssembly(entry, "ClassIsland");
        var shared = SharedAssembly(entry, "ClassIsland.Shared");
        object Service(string name) => services.GetService(core.GetType("ClassIsland.Core.Abstractions.Services." + name, true)!)!;
        object New(Assembly assembly, string name) => Activator.CreateInstance(assembly.GetType(name, true)!)!;
        var automation = Service("IAutomationService");
        var hang = Service("IHangService");
        var settings = Property(app, "Settings")!;
        SetProperty(settings, "IsAutomationEnabled", true);
        VerifyDefaultActionMenus(core, Service("IActionService"));
        var workflows = (IList)Property(automation, "Workflows")!;
        var workers = new List<Task>();
        foreach (var previous in workflows.Cast<object>())
            foreach (var previousTrigger in ((IEnumerable)Property(previous, "Triggers")!).Cast<object>())
                if (Property(previousTrigger, "TriggerInstance") is { } instance)
                    workers.AddRange(CronWorkers(instance));
        // ObservableCollection.Clear raises Reset without old items. The old
        // schedules must still be unloaded before the replacement profile runs.
        workflows.Clear();
        var workflow = New(core, "ClassIsland.Core.Models.Automation.Workflow");
        var triggerSettings = New(core, "ClassIsland.Core.Models.Automation.TriggerSettings");
        var cronSettings = New(native, "ClassIsland.Models.Automation.Triggers.CronTriggerSettings");
        SetProperty(cronSettings, "CronExpression", "0 0 * * *");
        SetProperty(triggerSettings, "Id", "classisland.cron");
        SetProperty(triggerSettings, "Settings", cronSettings);
        ((IList)Property(workflow, "Triggers")!).Add(triggerSettings);
        var actionSet = Property(workflow, "ActionSet")!;
        SetProperty(actionSet, "Name", "ExusiAI lifecycle probe");
        var action = New(shared, "ClassIsland.Shared.Models.Automation.ActionItem");
        var sleep = New(native, "ClassIsland.Models.Actions.SleepActionSettings");
        SetProperty(sleep, "Value", 60d);
        SetProperty(action, "Id", "classisland.action.sleep");
        SetProperty(action, "Settings", sleep);
        ((IList)Property(actionSet, "ActionItems")!).Add(action);
        workflows.Add(workflow);
        var trigger = Property(triggerSettings, "TriggerInstance")
            ?? throw new InvalidOperationException("Lifecycle cron trigger was not loaded.");
        workers.AddRange(CronWorkers(trigger));
        SetProperty(cronSettings, "CronExpression", "1 0 * * *");
        workers.AddRange(CronWorkers(trigger));
        SetProperty(cronSettings, "CronExpression", "2 0 * * *");
        workers.AddRange(CronWorkers(trigger));
        FireTrigger(trigger);
        hang.GetType().GetMethod("AssumeHang")!.Invoke(hang, null);
        if (Property(Property(hang, "Timer")!, "Enabled") is not true)
            throw new InvalidOperationException("Lifecycle hang timer was not running before disable.");
        return new(automation, settings, hang, trigger, cronSettings, triggerSettings, actionSet, action,
            workers.Distinct().ToArray());
    }

    private static void FireTrigger(object trigger) => trigger.GetType().GetMethod("Trigger",
        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(trigger, null);

    private static void VerifyActiveSessionProbe(SessionProbe probe)
    {
        if (!CronWorkers(probe.Trigger).Any(task => !task.IsCompleted) ||
            Property(probe.ActionSet, "IsWorking") is not true || Property(probe.Action, "IsWorking") is not true)
            throw new InvalidOperationException("Lifecycle probe did not start a real cron worker and in-flight action.");
    }

    private static void VerifyStoppedSessionProbe(SessionProbe probe)
    {
        if (Property(Property(probe.Hang, "Timer")!, "Enabled") is not false ||
            Property(probe.Hang, "IsChecking") is not false)
            throw new InvalidOperationException("Suspended session retained its hang timer or dispatcher check.");
        if (probe.Workers.Length == 0 || probe.Workers.Any(task => !task.IsCompletedSuccessfully) ||
            Property(probe.TriggerSettings, "TriggerInstance") is not null)
            throw new InvalidOperationException("Suspended session retained a cron worker or loaded trigger.");
        if (Property(probe.ActionSet, "IsWorking") is not false || Property(probe.Action, "IsWorking") is not false)
            throw new InvalidOperationException("Suspended session retained an in-flight automation action.");
        if (HasHandlerTarget(probe.Settings, "PropertyChanged", probe.Automation) ||
            HasHandlerTarget(probe.Settings, "PropertyChanging", probe.Automation) ||
            HasHandlerTarget(probe.CronSettings, "PropertyChanged", probe.Trigger))
            throw new InvalidOperationException("Suspended session retained automation settings subscriptions.");
        FireTrigger(probe.Trigger);
        SetProperty(probe.CronSettings, "CronExpression", "3 0 * * *");
        probe.Hang.GetType().GetMethod("AssumeHang")!.Invoke(probe.Hang, null);
        if (Property(probe.ActionSet, "IsWorking") is not false ||
            CronWorkers(probe.Trigger).Any(task => !task.IsCompletedSuccessfully) ||
            Property(probe.Hang, "IsChecking") is not false)
            throw new InvalidOperationException("A stale callback restarted work after suspension.");
    }

    private static void VerifyDefaultActionMenus(Assembly core, object actionService)
    {
        var tree = (IEnumerable)core.GetType("ClassIsland.Core.Abstractions.Services.IActionService", true)!
            .GetField("ActionMenuTree")!.GetValue(null)!;
        var group = tree.Cast<object>().Single(item => Equals(Property(item, "Name"), "运行"));
        var children = (IList)Property(group, "Children")!;
        var original = children.Cast<object>().ToArray();
        var custom = Activator.CreateInstance(core.GetType("ClassIsland.Core.Models.Automation.ActionMenuTreeItem", true)!,
            new object?[] { "exusiai.probe", "Probe plugin action", null })!;
        try
        {
            children.Clear();
            children.Add(custom);
            var type = actionService.GetType();
            var logger = type.GetField("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actionService)!;
            Activator.CreateInstance(type, new[] { logger });
            Activator.CreateInstance(type, new[] { logger });
            var names = children.Cast<object>().Select(item => (string)Property(item, "Name")!).ToArray();
            string[] expected = ["Probe plugin action", "应用程序", "cmd 命令", "文件", "文件夹", "Url 链接"];
            if (!ReferenceEquals(children[0], custom) || !names.SequenceEqual(expected))
                throw new InvalidOperationException("Built-in action menu merge lost plugin entries or added duplicates.");
        }
        finally
        {
            children.Clear();
            foreach (var item in original) children.Add(item);
        }
    }
}
