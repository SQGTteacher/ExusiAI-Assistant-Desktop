namespace ExusiAI.Plugin.ClassIsland;

public sealed record ClassIslandComponentDefinition(Guid Id, string Name, string Description);

public static class ClassIslandComponentCatalog
{
    public static IReadOnlyList<ClassIslandComponentDefinition> BuiltIn { get; } =
    [
        new(new("DF3F8295-21F6-482E-BADA-FA0E5F14BB66"), "日期", "显示今天的日期和星期"),
        new(new("1DB2017D-E374-4BC6-9D57-0B4ADF03A6B8"), "课程表", "显示当前课程和下一节课"),
        new(new("9E1AF71D-8F77-4B21-A342-448787104DD9"), "时钟", "显示当前时间"),
        new(new("CA495086-E297-4BEB-9603-C5C1C1A8551E"), "天气简报", "显示天气概况和预警"),
        new(new("7C645D35-8151-48BA-B4AC-15017460D994"), "倒计时", "显示指定日期倒计时"),
        new(new("EE8F66BD-C423-4E7C-AB46-AA9976B00E08"), "文本", "显示自定义文本"),
        new(new("AB0F26D5-9DF6-4575-B844-73B04D0907C1"), "分割线", "在组件间显示分割线"),
        new(new("C911D762-107F-40C6-84CC-0146AB3C86B1"), "分组容器", "组合多个组件"),
        new(new("70FCD5EA-3FAE-4E06-ACA2-4F4DF47F9ACD"), "滚动容器", "滚动显示多个组件"),
        new(new("2D849ECE-9F21-4C78-9434-415CFC283294"), "堆叠容器", "堆叠显示多个组件"),
        new(new("7E19A113-D281-4F33-970A-834A0B78B5AD"), "轮播容器", "轮播多个组件")
    ];

    public static ClassIslandComponentDefinition? Find(string id) =>
        Guid.TryParse(id, out var value) ? BuiltIn.FirstOrDefault(x => x.Id == value) : null;
}
