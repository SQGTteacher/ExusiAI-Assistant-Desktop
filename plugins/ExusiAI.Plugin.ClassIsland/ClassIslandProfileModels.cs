using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExusiAI.Plugin.ClassIsland;

public abstract class ClassIslandJsonModel
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
}

public abstract class ClassIslandAttachableModel : ClassIslandJsonModel
{
    public Dictionary<Guid, JsonElement> AttachedObjects { get; set; } = [];

    public T? GetAttachedObject<T>(Guid id) =>
        AttachedObjects.TryGetValue(id, out var value) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? value.Deserialize<T>() : default;

    public void SetAttachedObject<T>(Guid id, T value) => AttachedObjects[id] = JsonSerializer.SerializeToElement(value);
}

// ClassIsland 2.1 built-in class notification attached setting. Keep unknown keys intact.
public sealed class ClassIslandClassNotificationAttachedSettings : ClassIslandJsonModel
{
    public static Guid Id { get; } = new("08F0D9C3-C770-4093-A3D0-02F3D90C24BC");
    public bool IsAttachSettingsEnabled { get; set; }
    public bool IsClassOnNotificationEnabled { get; set; } = true;
    public bool IsClassOnPreparingNotificationEnabled { get; set; } = true;
    public bool IsClassOffNotificationEnabled { get; set; } = true;
    public int ClassPreparingDeltaTime { get; set; } = 60;
    public string ClassOnPreparingText { get; set; } = "准备上课，请回到座位并保持安静，做好上课准备。";
    public string OutdoorClassOnPreparingText { get; set; } = "下节课程为户外课程，请合理规划时间，做好上课准备。";
    public string ClassOnPreparingMaskText { get; set; } = "即将上课";
    public string OutdoorClassOnPreparingMaskText { get; set; } = "即将上课";
    public string ClassOnMaskText { get; set; } = "上课";
    public string ClassOffMaskText { get; set; } = "课间休息";
    public string ClassOffOverlayText { get; set; } = "";
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

public sealed class ClassIslandSubject : ClassIslandAttachableModel
{
    public string Name { get; set; } = "";
    public string Initial { get; set; } = "";
    public string TeacherName { get; set; } = "";
    public bool IsOutDoor { get; set; }
}

public sealed class ClassIslandTimeLayout : ClassIslandAttachableModel
{
    public string Name { get; set; } = "新时间表";
    public List<ClassIslandTimeLayoutItem> Layouts { get; set; } = [];
    public bool IsActivated { get; set; }
    public bool IsActivatedManually { get; set; }
    public bool IsOverlay { get; set; }
    public Guid? OverlaySourceId { get; set; }
}

public sealed class ClassIslandTimeLayoutItem : ClassIslandAttachableModel
{
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int TimeType { get; set; }
    public bool IsHideDefault { get; set; }
    public Guid DefaultClassId { get; set; }
    public string BreakName { get; set; } = "";
}

public sealed class ClassIslandClassPlan : ClassIslandAttachableModel
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

public sealed class ClassIslandClassInfo : ClassIslandAttachableModel
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
