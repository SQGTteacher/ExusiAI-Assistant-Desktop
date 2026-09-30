using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExusiAI.Plugin.ClassIsland;

public abstract class ClassIslandJsonModel
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ClassIslandProfile : ClassIslandJsonModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Dictionary<Guid, ClassIslandSubject> Subjects { get; set; } = [];
    public Dictionary<Guid, ClassIslandTimeLayout> TimeLayouts { get; set; } = [];
    public Dictionary<Guid, ClassIslandClassPlan> ClassPlans { get; set; } = [];
    public bool IsOverlayClassPlanEnabled { get; set; }
    public Guid? OverlayClassPlanId { get; set; }
    public Guid? TempClassPlanId { get; set; }
    public DateTime TempClassPlanSetupTime { get; set; } = DateTime.Now;
    public Dictionary<Guid, ClassIslandClassPlanGroup> ClassPlanGroups { get; set; } =
        new() { [ClassIslandClassPlanGroup.DefaultGroupGuid] = new() { Name = "默认" }, [Guid.Empty] = new() { Name = "全局课表群", IsGlobal = true } };
    public Guid SelectedClassPlanGroupId { get; set; } = ClassIslandClassPlanGroup.DefaultGroupGuid;
    public Guid? TempClassPlanGroupId { get; set; }
    public DateTime TempClassPlanGroupExpireTime { get; set; } = DateTime.Now;
    public bool IsTempClassPlanGroupEnabled { get; set; }
    public ClassIslandTempClassPlanGroupType TempClassPlanGroupType { get; set; } = ClassIslandTempClassPlanGroupType.Inherit;
    public Dictionary<DateTime, ClassIslandOrderedSchedule> OrderedSchedules { get; set; } = [];
    public Dictionary<Guid, ClassIslandScheduleItem> ScheduleItems { get; set; } = [];
    public ClassIslandScheduleType ScheduleType { get; set; }
    public List<ClassIslandProfileMigration> Migrations { get; set; } = [];
}

public sealed class ClassIslandSubject : ClassIslandJsonModel
{
    public string Name { get; set; } = "";
    public string Initial { get; set; } = "";
    public string TeacherName { get; set; } = "";
    public bool IsOutDoor { get; set; }
    public string Icon { get; set; } = "lucide(\ue54f)";
    public string ColorHex { get; set; } = "#66ccff";
    public string Location { get; set; } = "";
}

public sealed class ClassIslandTimeLayout : ClassIslandJsonModel
{
    public string Name { get; set; } = "新时间表";
    public List<ClassIslandTimeLayoutItem> Layouts { get; set; } = [];
    public bool IsActivated { get; set; }
    public bool IsActivatedManually { get; set; }
    public bool IsOverlay { get; set; }
    public Guid? OverlaySourceId { get; set; }
}

public sealed class ClassIslandTimeLayoutItem : ClassIslandJsonModel
{
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int TimeType { get; set; }
    public bool IsHideDefault { get; set; }
    public Guid DefaultClassId { get; set; }
    public string BreakName { get; set; } = "";
}

public sealed class ClassIslandClassPlan : ClassIslandJsonModel
{
    public string Name { get; set; } = "新课表";
    public Guid TimeLayoutId { get; set; }
    public List<ClassIslandClassInfo> Classes { get; set; } = [];
    public ClassIslandTimeRule TimeRule { get; set; } = new();
    public bool IsActivated { get; set; }
    public bool IsOverlay { get; set; }
    public Guid? OverlaySourceId { get; set; }
    public DateTime OverlaySetupTime { get; set; } = DateTime.Now;
    public bool IsEnabled { get; set; } = true;
    public Guid AssociatedGroup { get; set; } = new("ACAF4EF0-E261-4262-B941-34EA93CB4369");
}

public sealed class ClassIslandClassPlanGroup : ClassIslandJsonModel
{
    public static Guid DefaultGroupGuid { get; } = new("ACAF4EF0-E261-4262-B941-34EA93CB4369");
    public string Name { get; set; } = "新课表群";
    public bool IsGlobal { get; set; }
}

public sealed class ClassIslandOrderedSchedule : ClassIslandJsonModel
{
    public Guid ClassPlanId { get; set; }
}

public sealed class ClassIslandScheduleItem : ClassIslandJsonModel
{
    public Guid SubjectId { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public ClassIslandTimeRule EnableRule { get; set; } = new();
}

public sealed class ClassIslandProfileMigration : ClassIslandJsonModel
{
    public string Id { get; set; } = "";
    public bool AllowDowngrade { get; set; } = true;
    public bool RemoveOnDowngrade { get; set; }
}

public sealed class ClassIslandClassInfo : ClassIslandJsonModel
{
    public Guid SubjectId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsChangedClass { get; set; }
}

public sealed class ClassIslandTimeRule : ClassIslandJsonModel
{
    public ClassIslandTimeRuleType Type { get; set; }
    public bool RestrictsEnableRange { get; set; }
    public DateOnly RangeStart { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly RangeEnd { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int WeekDay { get; set; }
    public int WeekCountDiv { get; set; }
    public int WeekCountDivTotal { get; set; } = 2;
    public List<DateOnly> EnableDates { get; set; } = [];
    public int LoopCycleDays { get; set; } = 3;
    public int LoopOffsetDays { get; set; }
}

public enum ClassIslandTimeRuleType
{
    Weekly,
    Date,
    Loop
}

public enum ClassIslandTempClassPlanGroupType { Override, Inherit }
public enum ClassIslandScheduleType { Classic, Schedule }

public readonly record struct ClassIslandWeekRule(int Day, int RotationWeek, int RotationLength)
{
    public static ClassIslandWeekRule From(ClassIslandTimeRule rule) =>
        new(rule.WeekDay, rule.WeekCountDiv, Math.Max(1, rule.WeekCountDivTotal));
}
