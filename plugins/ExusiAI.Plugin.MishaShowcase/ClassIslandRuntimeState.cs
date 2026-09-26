namespace ExusiAI.Plugin.MishaShowcase;

internal enum ClassIslandSchedulePhase
{
    NoProfile,
    NoLessons,
    BeforeClass,
    OnClass,
    Breaking,
    AfterSchool
}

internal sealed record ClassIslandScheduleState(
    DateTime Now,
    ClassIslandSchedulePhase Phase,
    IReadOnlyList<ClassIslandLessonSnapshot> Lessons,
    ClassIslandLessonSnapshot? Current,
    ClassIslandLessonSnapshot? Previous,
    ClassIslandLessonSnapshot? Next)
{
    public string TransitionKey => $"{Now:yyyy-MM-dd}|{Phase}|{Current?.PlanId}|{Current?.Index}|{Next?.PlanId}|{Next?.Index}";
}

internal static class ClassIslandRuntimeStateResolver
{
    public static ClassIslandScheduleState Resolve(MishaPlatformStore store, DateTime now)
    {
        if (store.Profile is null)
            return new(now, ClassIslandSchedulePhase.NoProfile, [], null, null, null);

        var lessons = store.Profile.GetLessonsForDate(now.Date, store.ResolveRotationWeek(now.Date));
        return Resolve(lessons, now);
    }

    public static ClassIslandScheduleState Resolve(
        IReadOnlyList<ClassIslandLessonSnapshot> lessons,
        DateTime now)
    {
        var ordered = lessons.OrderBy(x => x.Start).ThenBy(x => x.Index).ToArray();
        if (ordered.Length == 0)
            return new(now, ClassIslandSchedulePhase.NoLessons, ordered, null, null, null);

        var time = now.TimeOfDay;
        var current = ordered.FirstOrDefault(x => time >= x.Start && time < x.End);
        var previous = ordered.LastOrDefault(x => x.End <= time);
        var next = ordered.FirstOrDefault(x => x.Start > time);
        var phase = current is not null
            ? ClassIslandSchedulePhase.OnClass
            : time < ordered[0].Start
                ? ClassIslandSchedulePhase.BeforeClass
                : time >= ordered[^1].End
                    ? ClassIslandSchedulePhase.AfterSchool
                    : ClassIslandSchedulePhase.Breaking;

        return new(now, phase, ordered, current, previous, next);
    }
}
