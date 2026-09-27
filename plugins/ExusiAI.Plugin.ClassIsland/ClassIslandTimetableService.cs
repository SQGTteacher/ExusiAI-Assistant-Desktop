namespace ExusiAI.Plugin.ClassIsland;

public sealed record ClassIslandLesson(
    Guid PlanId,
    Guid SubjectId,
    ClassIslandSubject Subject,
    ClassIslandTimeLayoutItem Time,
    int Index);

public sealed class ClassIslandTimetableService
{
    private readonly ClassIslandProfileService profiles;

    public ClassIslandTimetableService(ClassIslandProfileService profiles) => this.profiles = profiles;

    public ClassIslandClassPlan? ResolvePlan(DateTime now, DateOnly? rotationAnchor = null)
    {
        var profile = profiles.Current;
        if (profile is null) return null;
        return profile.ClassPlans.Values.FirstOrDefault(x => x.IsEnabled && Matches(x.TimeRule, now, rotationAnchor));
    }

    public IReadOnlyList<ClassIslandLesson> GetLessons(DateTime now, DateOnly? rotationAnchor = null)
    {
        var profile = profiles.Current;
        if (profile is null) return [];
        var planPair = profile.ClassPlans.FirstOrDefault(x => x.Value.IsEnabled && Matches(x.Value.TimeRule, now, rotationAnchor));
        if (planPair.Value is null || !profile.TimeLayouts.TryGetValue(planPair.Value.TimeLayoutId, out var layout)) return [];
        var lessonTimes = layout.Layouts.Where(x => x.TimeType == 0).ToArray();
        var count = Math.Min(lessonTimes.Length, planPair.Value.Classes.Count);
        var result = new List<ClassIslandLesson>(count);
        for (var index = 0; index < count; index++)
        {
            var info = planPair.Value.Classes[index];
            if (!info.IsEnabled || !profile.Subjects.TryGetValue(info.SubjectId, out var subject)) continue;
            result.Add(new(planPair.Key, info.SubjectId, subject, lessonTimes[index], index));
        }
        return result;
    }

    public static bool Matches(ClassIslandTimeRule rule, DateTime now, DateOnly? rotationAnchor = null)
    {
        var date = DateOnly.FromDateTime(now);
        if (rule.RestrictsEnableRange && (date < rule.RangeStart || date > rule.RangeEnd)) return false;
        return rule.Type switch
        {
            ClassIslandTimeRuleType.Date => rule.EnableDates.Contains(date),
            ClassIslandTimeRuleType.Loop => MatchesLoop(rule, date, rotationAnchor ?? rule.RangeStart),
            _ => MatchesWeek(ClassIslandWeekRule.From(rule), date, rotationAnchor)
        };
    }

    private static bool MatchesWeek(ClassIslandWeekRule rule, DateOnly date, DateOnly? anchor)
    {
        if (rule.Day != (int)date.DayOfWeek) return false;
        if (rule.RotationWeek <= 0) return true;
        var start = anchor ?? DateOnly.FromDateTime(DateTime.Today);
        var week = Math.DivRem(date.DayNumber - start.DayNumber, 7, out _);
        var normalized = ((week % rule.RotationLength) + rule.RotationLength) % rule.RotationLength + 1;
        return normalized == rule.RotationWeek;
    }

    private static bool MatchesLoop(ClassIslandTimeRule rule, DateOnly date, DateOnly anchor)
    {
        var cycle = Math.Max(1, rule.LoopCycleDays);
        var value = date.DayNumber - anchor.DayNumber - rule.LoopOffsetDays;
        return ((value % cycle) + cycle) % cycle == 0;
    }
}
