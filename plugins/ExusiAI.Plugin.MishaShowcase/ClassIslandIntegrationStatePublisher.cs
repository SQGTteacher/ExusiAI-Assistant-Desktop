using System.IO;
using System.Globalization;
using System.Text.Json.Nodes;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class ClassIslandIntegrationStatePublisher : IDisposable
{
    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExusiAI", "integration", "classisland-state.json");

    private readonly string path;
    private string? lastTransitionKey;
    private bool disposed;

    public ClassIslandIntegrationStatePublisher(string? path = null)
    {
        this.path = path ?? DefaultPath;
    }

    public void Publish(ClassIslandScheduleState state)
    {
        if (disposed || string.Equals(lastTransitionKey, state.TransitionKey, StringComparison.Ordinal))
            return;

        lastTransitionKey = state.TransitionKey;
        TryWrite(CreatePayload(state, available: true));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        TryWrite(new JsonObject
        {
            ["SchemaVersion"] = 1,
            ["Available"] = false,
            ["GeneratedAt"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)
        });
    }

    private void TryWrite(JsonObject payload)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, payload.ToJsonString());
            File.Move(temporary, path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Optional integrations must never destabilize ClassIsland.
        }
    }

    private static JsonObject CreatePayload(ClassIslandScheduleState state, bool available)
    {
        return new JsonObject
        {
            ["SchemaVersion"] = 1,
            ["Available"] = available,
            ["GeneratedAt"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            ["Date"] = state.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Phase"] = state.Phase.ToString(),
            ["Current"] = Lesson(state.Current),
            ["Previous"] = Lesson(state.Previous),
            ["Next"] = Lesson(state.Next)
        };
    }

    private static JsonObject? Lesson(ClassIslandLessonSnapshot? lesson)
    {
        if (lesson is null) return null;
        return new JsonObject
        {
            ["PlanId"] = lesson.PlanId,
            ["PlanName"] = lesson.PlanName,
            ["Index"] = lesson.Index,
            ["Subject"] = lesson.Subject,
            ["Teacher"] = lesson.Teacher,
            ["Start"] = lesson.Start.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            ["End"] = lesson.End.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
        };
    }
}
