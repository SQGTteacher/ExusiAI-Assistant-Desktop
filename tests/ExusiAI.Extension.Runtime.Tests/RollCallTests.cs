using ExusiAI.Plugin.RollCall;

namespace ExusiAI.Extension.Runtime.Tests;

public sealed class RollCallTests
{
    [Fact]
    public void ImporterRecognizesChineseCsvHeadersAndQuotedValues()
    {
        var result = RosterImporter.Parse("学号,姓名,班级\n01,张三,高一一班\n02,\"李,四\",高一一班");

        Assert.Collection(result,
            first => Assert.Equal(new RosterEntry("张三", "01", "高一一班"), first),
            second => Assert.Equal(new RosterEntry("李,四", "02", "高一一班"), second));
    }

    [Fact]
    public void ImporterAcceptsOneNamePerLineAndRemovesDuplicates()
    {
        var result = RosterImporter.Parse("张三\n李四\n张三\n");

        Assert.Equal(["张三", "李四"], result.Select(item => item.Name));
    }

    [Fact]
    public void SessionDrawsEveryStudentOnlyOncePerRound()
    {
        var session = new RollCallSession([new("甲"), new("乙"), new("丙")]);
        var names = Enumerable.Range(0, 3).Select(_ => session.DrawNext()!.Name).ToArray();

        Assert.Equal(3, names.Distinct().Count());
        Assert.Equal(0, session.RemainingCount);
        Assert.Null(session.DrawNext());
    }

    [Fact]
    public void UndoReturnsCurrentStudentToPool()
    {
        var session = new RollCallSession([new("甲")]);
        session.DrawNext();
        session.MarkCurrentAbsent();
        session.Undo();

        Assert.Equal(1, session.RemainingCount);
        Assert.Empty(session.Events);
        Assert.Null(session.Current);
    }
}
