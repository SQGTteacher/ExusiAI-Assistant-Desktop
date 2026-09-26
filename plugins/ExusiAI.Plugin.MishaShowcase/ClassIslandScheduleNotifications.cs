namespace ExusiAI.Plugin.MishaShowcase;

internal enum ClassIslandScheduleNotificationKind
{
    Prepare,
    ClassBegin,
    ClassEnd,
    AfterSchool
}

internal sealed record ClassIslandScheduleNotification(
    ClassIslandScheduleNotificationKind Kind,
    string Title,
    string Body,
    string Key);

internal sealed class ClassIslandScheduleNotificationTracker
{
    private string? lastStateKey;
    private string? lastPrepareKey;

    public ClassIslandScheduleNotification? Evaluate(
        ClassIslandScheduleState state,
        bool classBeginEnabled,
        bool classPrepareEnabled,
        bool classEndEnabled,
        int prepareSeconds)
    {
        var initialized = lastStateKey is not null;
        var stateChanged = !string.Equals(lastStateKey, state.TransitionKey, StringComparison.Ordinal);
        lastStateKey = state.TransitionKey;

        if (!initialized)
            return null;

        if (classPrepareEnabled && state.Next is not null)
        {
            var remaining = state.Now.Date.Add(state.Next.Start) - state.Now;
            var prepareKey = $"{state.Now:yyyy-MM-dd}|{state.Next.PlanId}|{state.Next.Index}";
            if (remaining > TimeSpan.Zero &&
                remaining <= TimeSpan.FromSeconds(Math.Clamp(prepareSeconds, 1, 3600)) &&
                !string.Equals(lastPrepareKey, prepareKey, StringComparison.Ordinal))
            {
                lastPrepareKey = prepareKey;
                return new(
                    ClassIslandScheduleNotificationKind.Prepare,
                    "即将上课",
                    $"{state.Next.Subject} · {FormatRemaining(remaining)}后开始",
                    "prepare|" + prepareKey);
            }
        }

        if (!stateChanged)
            return null;

        if (classBeginEnabled && state.Phase == ClassIslandSchedulePhase.OnClass && state.Current is not null)
            return new(
                ClassIslandScheduleNotificationKind.ClassBegin,
                "上课",
                string.IsNullOrWhiteSpace(state.Current.Teacher)
                    ? state.Current.Subject
                    : $"{state.Current.Subject} · {state.Current.Teacher}",
                "begin|" + state.TransitionKey);

        if (classEndEnabled && state.Phase == ClassIslandSchedulePhase.Breaking && state.Previous is not null)
            return new(
                ClassIslandScheduleNotificationKind.ClassEnd,
                "下课",
                state.Next is null ? "本节课程已结束" : $"下一节：{state.Next.Subject}",
                "end|" + state.TransitionKey);

        if (classEndEnabled && state.Phase == ClassIslandSchedulePhase.AfterSchool && state.Previous is not null)
            return new(
                ClassIslandScheduleNotificationKind.AfterSchool,
                "课程结束",
                "今天的课程已经全部结束",
                "after-school|" + state.TransitionKey);

        return null;
    }

    public void Reset()
    {
        lastStateKey = null;
        lastPrepareKey = null;
    }

    private static string FormatRemaining(TimeSpan remaining) =>
        remaining.TotalMinutes >= 1
            ? $"{Math.Ceiling(remaining.TotalMinutes):0} 分钟"
            : $"{Math.Max(1, Math.Ceiling(remaining.TotalSeconds)):0} 秒";
}
