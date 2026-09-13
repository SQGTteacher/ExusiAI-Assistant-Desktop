using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace ExusiAI.Plugin.MishaShowcase;

internal sealed class MishaPlatformState
{
    public string ProfileName { get; set; } = "高二（1）班";
    public int CycleWeek { get; set; } = 1;
    public bool MouseThrough { get; set; }
    public bool AutoHide { get; set; }
    public bool PasswordProtection { get; set; }
    public bool TimeSync { get; set; } = true;
    public string WeatherCity { get; set; } = "北京市";
    public string Theme { get; set; } = "跟随宿主";
    public ObservableCollection<ScheduleEntry> Schedule { get; set; } = [];
    public ObservableCollection<ComponentEntry> Components { get; set; } = [];
    public ObservableCollection<AutomationEntry> Automations { get; set; } = [];
    public ObservableCollection<BuiltInExtension> Extensions { get; set; } = [];

    public static MishaPlatformState CreateDefault() => new()
    {
        Schedule =
        [
            new(1, "语文", "林老师", "08:00", "08:40", 1, true),
            new(2, "数学", "周老师", "08:50", "09:30", 1, true),
            new(3, "英语", "陈老师", "09:50", "10:30", 1, true),
            new(4, "物理", "许老师", "10:40", "11:20", 1, true),
            new(5, "历史", "赵老师", "14:00", "14:40", 1, true),
            new(6, "信息技术", "王老师", "14:50", "15:30", 1, true)
        ],
        Components =
        [
            new("当前课程", true, 1), new("接下来", true, 1), new("时间", true, 2),
            new("日期", true, 2), new("天气简报", true, 2), new("倒计日", true, 2)
        ],
        Automations =
        [
            new("上课提醒", "课程开始前 1 分钟", "强调提醒 + 语音", true),
            new("下课提醒", "课程结束时", "播放提示音", true),
            new("午间隐藏", "每天 12:00", "临时隐藏主界面", false)
        ],
        Extensions =
        [
            new("weather", "天气服务", "天气、降水提示、6 小时及 3 天天气预报", true),
            new("countdown", "倒计日", "考试与纪念日倒计时组件", true),
            new("notification", "强调提醒", "音效、语音、置顶和强调动画", true),
            new("automation", "自动化行动", "按事件或时间执行提醒、文件、应用与网页行动", true),
            new("cses", "CSES 互操作", "导入和导出 CSES 课表数据", false)
        ]
    };
}

internal sealed record ScheduleEntry(int Index, string Subject, string Teacher, string Start, string End, int Week, bool Enabled)
{
    public int Index { get; set; } = Index;
    public string Subject { get; set; } = Subject;
    public string Teacher { get; set; } = Teacher;
    public string Start { get; set; } = Start;
    public string End { get; set; } = End;
    public int Week { get; set; } = Week;
    public bool Enabled { get; set; } = Enabled;
}

internal sealed record ComponentEntry(string Name, bool Enabled, int Row)
{
    public string Name { get; set; } = Name;
    public bool Enabled { get; set; } = Enabled;
    public int Row { get; set; } = Row;
}

internal sealed record AutomationEntry(string Name, string Trigger, string Action, bool Enabled)
{
    public string Name { get; set; } = Name;
    public string Trigger { get; set; } = Trigger;
    public string Action { get; set; } = Action;
    public bool Enabled { get; set; } = Enabled;
}

internal sealed record BuiltInExtension(string Id, string Name, string Description, bool Installed)
{
    public string Id { get; set; } = Id;
    public string Name { get; set; } = Name;
    public string Description { get; set; } = Description;
    public bool Installed { get; set; } = Installed;
}

internal sealed class MishaPlatformStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath;

    public MishaPlatformStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExusiAI Assistant Desktop", "extensions", "exusiai.misha-showcase");
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "profile.json");
        State = LoadCore();
    }

    public MishaPlatformState State { get; private set; }
    public event EventHandler? Changed;

    public async Task SaveAsync()
    {
        await gate.WaitAsync();
        try
        {
            var temporary = filePath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(State, JsonOptions));
            File.Move(temporary, filePath, true);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { gate.Release(); }
    }

    public async Task ExportAsync(string destination) =>
        await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(State, JsonOptions));

    public async Task ImportAsync(string source)
    {
        var imported = JsonSerializer.Deserialize<MishaPlatformState>(await File.ReadAllTextAsync(source), JsonOptions)
            ?? throw new InvalidDataException("档案内容为空。");
        State = imported;
        await SaveAsync();
    }

    private MishaPlatformState LoadCore()
    {
        if (!File.Exists(filePath)) return MishaPlatformState.CreateDefault();
        try { return JsonSerializer.Deserialize<MishaPlatformState>(File.ReadAllText(filePath), JsonOptions) ?? MishaPlatformState.CreateDefault(); }
        catch (JsonException) { return MishaPlatformState.CreateDefault(); }
    }
}
