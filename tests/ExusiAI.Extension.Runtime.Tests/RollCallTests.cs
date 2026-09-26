using System.IO;
using ExusiAI.Plugin.RollCall;
using System.IO.Compression;

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

    [Fact]
    public async Task ImporterReadsFirstXlsxWorksheet()
    {
        var path = Path.Combine(Path.GetTempPath(), $"roster-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Write(archive, "xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="名单" sheetId="1" r:id="rId1"/></sheets></workbook>""");
                Write(archive, "xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Target="worksheets/sheet1.xml" Type="worksheet"/></Relationships>""");
                Write(archive, "xl/sharedStrings.xml", """<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>学号</t></si><si><t>姓名</t></si><si><t>班级</t></si><si><t>张三</t></si><si><t>高一一班</t></si></sst>""");
                Write(archive, "xl/worksheets/sheet1.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c></row><row r="2"><c r="A2"><v>01</v></c><c r="B2" t="s"><v>3</v></c><c r="C2" t="s"><v>4</v></c></row></sheetData></worksheet>""");
            }

            var result = await RosterImporter.ParseFileAsync(path);
            Assert.Equal(new RosterEntry("张三", "01", "高一一班"), Assert.Single(result));
        }
        finally { File.Delete(path); }
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }
}
