using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed record LessonItem(int Index, string Subject, string Teacher, TimeSpan Start, TimeSpan End);

internal sealed class MishaDashboardViewModel : INotifyPropertyChanged
{
    private readonly DateTime countdownTarget = new(2027, 6, 7, 9, 0, 0, DateTimeKind.Local);
    private DateTime now;
    private int? simulatedLessonIndex;

    private readonly MishaPlatformStore store;

    public MishaDashboardViewModel(MishaPlatformStore store)
    {
        this.store = store;
        Lessons = new(store.State.Schedule.Where(x => x.Enabled && x.Week == store.State.CycleWeek)
            .Select(x => new LessonItem(x.Index, x.Subject, x.Teacher,
                TimeSpan.TryParse(x.Start, out var start) ? start : TimeSpan.Zero,
                TimeSpan.TryParse(x.End, out var end) ? end : TimeSpan.Zero)));
        if (Lessons.Count == 0) Lessons.Add(new(1, "未安排课程", "", TimeSpan.Zero, TimeSpan.Zero));
        Tick(DateTime.Now);
    }

    public ObservableCollection<LessonItem> Lessons { get; }
    public string SchoolName => store.State.ProfileName;
    public string TimeText => now.ToString("HH:mm:ss");
    public string DateText => now.ToString("yyyy 年 M 月 d 日  dddd");
    public string CurrentSubject { get; private set; } = string.Empty;
    public string CurrentDetail { get; private set; } = string.Empty;
    public string NextSubject { get; private set; } = string.Empty;
    public string NextDetail { get; private set; } = string.Empty;
    public string CountdownText
    {
        get
        {
            var remaining = countdownTarget - now;
            if (remaining <= TimeSpan.Zero) return "目标日期已到";
            return $"{remaining.Days} 天 {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
        }
    }
    public double DayProgress => Math.Clamp(now.TimeOfDay.TotalMinutes / TimeSpan.FromDays(1).TotalMinutes * 100, 0, 100);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Tick(DateTime value)
    {
        now = value;
        UpdateLessonState();
        Notify(nameof(TimeText));
        Notify(nameof(DateText));
        Notify(nameof(CountdownText));
        Notify(nameof(DayProgress));
    }

    public void SimulateNextLesson()
    {
        var current = simulatedLessonIndex ?? ResolveCurrentIndex();
        simulatedLessonIndex = current < Lessons.Count - 1 ? current + 1 : 0;
        UpdateLessonState();
    }

    public void ResetSimulation()
    {
        simulatedLessonIndex = null;
        UpdateLessonState();
    }

    private int ResolveCurrentIndex()
    {
        var current = Lessons.Select((lesson, index) => (lesson, index))
            .FirstOrDefault(x => now.TimeOfDay >= x.lesson.Start && now.TimeOfDay < x.lesson.End);
        if (current.lesson is not null) return current.index;
        var upcoming = Lessons.Select((lesson, index) => (lesson, index))
            .FirstOrDefault(x => now.TimeOfDay < x.lesson.Start);
        return upcoming.lesson is null ? Lessons.Count - 1 : upcoming.index;
    }

    private void UpdateLessonState()
    {
        var index = simulatedLessonIndex ?? ResolveCurrentIndex();
        var lesson = Lessons[index];
        var isLive = simulatedLessonIndex is not null || now.TimeOfDay >= lesson.Start && now.TimeOfDay < lesson.End;
        CurrentSubject = isLive ? lesson.Subject : now.TimeOfDay < lesson.Start ? "课间准备" : "今日课程结束";
        CurrentDetail = isLive
            ? $"第 {lesson.Index} 节 · {lesson.Start:hh\\:mm}–{lesson.End:hh\\:mm} · {lesson.Teacher}"
            : now.TimeOfDay < lesson.Start ? $"下一节 {lesson.Start:hh\\:mm} 开始" : "请检查明日课表";
        var nextIndex = index + 1;
        NextSubject = nextIndex < Lessons.Count ? Lessons[nextIndex].Subject : "无后续课程";
        NextDetail = nextIndex < Lessons.Count
            ? $"{Lessons[nextIndex].Start:hh\\:mm} · {Lessons[nextIndex].Teacher}"
            : "今天辛苦了";
        Notify(nameof(CurrentSubject));
        Notify(nameof(CurrentDetail));
        Notify(nameof(NextSubject));
        Notify(nameof(NextDetail));
    }

    private void Notify([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new(propertyName));
}
