using System.Security.Cryptography;
using System.Text.Json;

namespace ExusiAI.Plugin.RollCall;

public enum RollCallStatus { Pending, Called, Absent }

public sealed record RollCallStudent(string Name, string StudentId = "", string ClassName = "", RollCallStatus Status = RollCallStatus.Pending);
public sealed record RollCallEvent(DateTimeOffset Time, string Name, string StudentId, RollCallStatus Status);
public sealed record RollCallSnapshot(IReadOnlyList<RollCallStudent> Students, IReadOnlyList<RollCallEvent> Events, string? CurrentStudentId = null);

public sealed class RollCallSession
{
    private readonly List<RollCallStudent> students;
    private readonly List<RollCallEvent> events;
    public IReadOnlyList<RollCallStudent> Students => students;
    public IReadOnlyList<RollCallEvent> Events => events;
    public RollCallStudent? Current { get; private set; }
    public int RemainingCount => students.Count(student => student.Status == RollCallStatus.Pending);

    public RollCallSession(IEnumerable<RosterEntry> roster) : this(new RollCallSnapshot(roster.Select(item => new RollCallStudent(item.Name, item.StudentId, item.ClassName)).ToArray(), [])) { }

    public RollCallSession(RollCallSnapshot snapshot)
    {
        students = snapshot.Students.ToList();
        events = snapshot.Events.ToList();
        Current = students.FirstOrDefault(student => Identity(student) == snapshot.CurrentStudentId);
    }

    public RollCallStudent? DrawNext()
    {
        var pending = students.Select((student, index) => (student, index)).Where(item => item.student.Status == RollCallStatus.Pending).ToArray();
        if (pending.Length == 0) return null;
        var selected = pending[RandomNumberGenerator.GetInt32(pending.Length)];
        var updated = selected.student with { Status = RollCallStatus.Called };
        students[selected.index] = updated;
        Current = updated;
        events.Add(new(DateTimeOffset.Now, updated.Name, updated.StudentId, updated.Status));
        return updated;
    }

    public void MarkCurrentAbsent()
    {
        if (Current is null) return;
        var index = students.FindIndex(student => Identity(student) == Identity(Current));
        if (index < 0) return;
        Current = students[index] with { Status = RollCallStatus.Absent };
        students[index] = Current;
        events.Add(new(DateTimeOffset.Now, Current.Name, Current.StudentId, Current.Status));
    }

    public void Undo()
    {
        if (Current is null) return;
        var index = students.FindIndex(student => Identity(student) == Identity(Current));
        if (index >= 0) students[index] = students[index] with { Status = RollCallStatus.Pending };
        events.RemoveAll(item => item.Name == Current.Name && item.StudentId == Current.StudentId);
        Current = null;
    }

    public void ResetRound()
    {
        for (var index = 0; index < students.Count; index++) students[index] = students[index] with { Status = RollCallStatus.Pending };
        events.Clear();
        Current = null;
    }

    public RollCallSnapshot Snapshot() => new(students.ToArray(), events.ToArray(), Current is null ? null : Identity(Current));
    private static string Identity(RollCallStudent student) => string.IsNullOrWhiteSpace(student.StudentId) ? student.Name : student.StudentId;
}

internal static class RollCallStore
{
    private static readonly string DataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExusiAI", "RollCall", "session.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static async Task<RollCallSnapshot?> LoadAsync()
    {
        try
        {
            if (!File.Exists(DataPath)) return null;
            await using var stream = File.OpenRead(DataPath);
            return await JsonSerializer.DeserializeAsync<RollCallSnapshot>(stream, Options);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public static async Task SaveAsync(RollCallSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
        var temporary = DataPath + ".tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, snapshot, Options);
        File.Move(temporary, DataPath, true);
    }
}
