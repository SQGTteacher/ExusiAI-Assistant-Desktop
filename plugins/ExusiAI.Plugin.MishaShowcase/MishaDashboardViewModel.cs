using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed record LessonItem(int Index, string Subject, string Teacher, TimeSpan Start, TimeSpan End, string PlanName);

internal sealed class MishaDashboardViewModel : INotifyPropertyChanged
{
    private readonly MishaPlatformStore store;
    private DateTime now;

    public MishaDashboardViewModel(MishaPlatformStore store)
    {
        this.store = store;
        store.Changed += (_, _) => RefreshLessons();
        RefreshLessons();
        Tick(DateTime.Now);
    }

    public ObservableCollection<LessonItem> Lessons { get; } = [];
    public string SchoolName => store.Profile?.Name ?? "尚未连接 ClassIsland 档案";
    public string WorkspaceText => store.Workspace is null
        ? "请先在“工作区”页面选择现有 ClassIsland Settings.json。"
        : store.Workspace.RootDirectory;
    public string TimeText => now.ToString("HH:mm:ss");
    public string DateText => now.ToString("yyyy 年 M 月 d 日  dddd");
    public string CurrentSubject { get; private set; } = "未连接档案";
    public string CurrentDetail { get; private set; } = "";
    public string NextSubject { get; private set; } = "";
    public string NextDetail { get; private set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Tick(DateTime value)
    {
        var dateChanged = now.Date != value.Date;
        now = value;
        if (dateChanged) RefreshLessons();
        UpdateLessonState();

        Notify(nameof(TimeText));
        Notify(nameof(DateText));
    }

    public void RefreshLessons()
    {
        Lessons.Clear();
        var profile = store.Profile;
        if (profile is not null)
        {
            var week = store.ResolveRotationWeek(DateTime.Today);
            foreach (var lesson in profile.GetLessonsForDate(DateTime.Today, week))
                Lessons.Add(new(lesson.Index, lesson.Subject, lesson.Teacher, lesson.Start, lesson.End, lesson.PlanName));
        }

        Notify(nameof(SchoolName));
        Notify(nameof(WorkspaceText));
        UpdateLessonState();
    }

    private void UpdateLessonState()
    {
        if (store.Profile is null)
        {
            CurrentSubject = "未连接档案";
            CurrentDetail = "请先选择真实的 ClassIsland Settings.json 或 Profile JSON。";
            NextSubject = "";
            NextDetail = "";
            NotifyLessonState();
            return;
        }

        if (Lessons.Count == 0)
        {
            CurrentSubject = "当前日期没有启用课程";
            CurrentDetail = $"轮换周：{store.ResolveRotationWeek(now)}";
            NextSubject = "";
            NextDetail = "";
            NotifyLessonState();
            return;
        }

        var liveIndex = -1;
        for (var i = 0; i < Lessons.Count; i++)
        {
            if (now.TimeOfDay >= Lessons[i].Start && now.TimeOfDay < Lessons[i].End)
            {
                liveIndex = i;
                break;
            }
        }

        if (liveIndex >= 0)
        {
            var lesson = Lessons[liveIndex];
            CurrentSubject = lesson.Subject;
            CurrentDetail = $"{lesson.PlanName} · 第 {lesson.Index} 节 · {lesson.Start:hh\:mm}–{lesson.End:hh\:mm} · {lesson.Teacher}";
            if (liveIndex + 1 < Lessons.Count)
            {
                var next = Lessons[liveIndex + 1];
                NextSubject = next.Subject;
                NextDetail = $"{next.Start:hh\:mm} · {next.Teacher}";
            }
            else
            {
                NextSubject = "无后续课程";
                NextDetail = "";
            }
            NotifyLessonState();
            return;
        }

        var nextIndex = -1;
        for (var i = 0; i < Lessons.Count; i++)
        {
            if (now.TimeOfDay < Lessons[i].Start)
            {
                nextIndex = i;
                break;
            }
        }

        if (nextIndex >= 0)
        {
            var next = Lessons[nextIndex];
            CurrentSubject = "课间 / 课前";
            CurrentDetail = $"下一节 {next.Start:hh\:mm} 开始";
            NextSubject = next.Subject;
            NextDetail = $"{next.PlanName} · {next.Teacher}";
        }
        else
        {
            CurrentSubject = "今日课程结束";
            CurrentDetail = "";
            NextSubject = "";
            NextDetail = "";
        }

        NotifyLessonState();
    }

    private void NotifyLessonState()
    {
        Notify(nameof(CurrentSubject));
        Notify(nameof(CurrentDetail));
        Notify(nameof(NextSubject));
        Notify(nameof(NextDetail));
    }

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}
