using System.IO;
using System.Text.Json.Nodes;
using ExusiAI.Extension.Abstractions;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class MishaAutomationRuntime : IDisposable
{
    private readonly IExtensionLogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<string> warnings = new(StringComparer.OrdinalIgnoreCase);
    private string? lastStateKey;

    public MishaAutomationRuntime(IExtensionLogger logger)
    {
        this.logger = logger;
    }

    public async Task ProcessAsync(
        ClassIslandScheduleState state,
        ClassIslandWorkspace workspace,
        MishaScheduleNotificationPresenter presenter)
    {
        var previousKey = lastStateKey;
        lastStateKey = state.TransitionKey;
        if (previousKey is null || string.Equals(previousKey, state.TransitionKey, StringComparison.Ordinal))
            return;
        if (!workspace.GetBool("IsAutomationEnabled"))
            return;

        var path = workspace.CurrentAutomationPath;
        if (path is null || !File.Exists(path) || !await gate.WaitAsync(0))
            return;

        try
        {
            var document = await ClassIslandAutomationDocument.LoadAsync(path);
            foreach (var workflow in document.Workflows.Where(x => x.IsEnabled))
            {
                if (workflow.IsConditionEnabled)
                {
                    WarnOnce($"ruleset:{workflow.Name}", $"Automation workflow '{workflow.Name}' uses a ruleset and was skipped because ruleset execution is not enabled yet.");
                    continue;
                }

                if (!workflow.Triggers.Any(trigger => Matches(trigger.Id, state.Phase)))
                    continue;

                foreach (var action in workflow.Actions)
                    ExecuteAction(workflow, action, workspace, presenter);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            logger.Error("ClassIsland automation configuration could not be evaluated.", exception);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        // The final async evaluation may still be unwinding while the plugin stops. Keeping the
        // lightweight semaphore alive avoids a dispose/release race during shutdown.
        lastStateKey = null;
    }

    internal static bool Matches(string triggerId, ClassIslandSchedulePhase phase) => triggerId switch
    {
        "classisland.lessons.onClass" => phase == ClassIslandSchedulePhase.OnClass,
        "classisland.lessons.onBreakingTime" => phase == ClassIslandSchedulePhase.Breaking,
        "classisland.lessons.onAfterSchool" => phase == ClassIslandSchedulePhase.AfterSchool,
        "classisland.lessons.currentTimeStateChanged" => true,
        _ => false
    };

    private void ExecuteAction(
        ClassIslandWorkflowRow workflow,
        ClassIslandActionRow action,
        ClassIslandWorkspace workspace,
        MishaScheduleNotificationPresenter presenter)
    {
        if (!string.Equals(action.Id, "classisland.showNotification", StringComparison.OrdinalIgnoreCase))
        {
            WarnOnce($"action:{action.Id}", $"Automation action '{action.Id}' was skipped because it is not in the safe runtime allowlist.");
            return;
        }

        var settings = action.Node["Settings"] as JsonObject;
        var title = ReadString(settings, "Mask");
        var content = ReadString(settings, "Content");
        if (string.IsNullOrWhiteSpace(title)) title = workflow.Name;
        if (string.IsNullOrWhiteSpace(content)) content = title;
        presenter.Show(
            new ClassIslandScheduleNotification(
                ClassIslandScheduleNotificationKind.ClassBegin,
                title,
                content,
                $"automation|{workflow.Index}|{action.Index}"),
            workspace);
    }

    private void WarnOnce(string key, string message)
    {
        if (warnings.Add(key)) logger.Warning(message);
    }

    private static string ReadString(JsonObject? owner, string key) =>
        owner?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? "" : "";
}
