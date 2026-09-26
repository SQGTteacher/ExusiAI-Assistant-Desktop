using System.Text;

namespace ExusiAI.Plugin.RollCall;

public sealed record RosterEntry(string Name, string StudentId = "", string ClassName = "");

public static class RosterImporter
{
    private static readonly string[] NameHeaders = ["姓名", "名字", "name", "studentname", "student_name"];
    private static readonly string[] IdHeaders = ["学号", "编号", "id", "studentid", "student_id"];
    private static readonly string[] ClassHeaders = ["班级", "行政班", "class", "classname", "class_name"];

    public static IReadOnlyList<RosterEntry> Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var delimiter = DetectDelimiter(text);
        var rows = ParseRows(text, delimiter)
            .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .ToList();
        if (rows.Count == 0) return [];

        var header = rows[0].Select(NormalizeHeader).ToArray();
        var nameColumn = FindColumn(header, NameHeaders);
        var idColumn = FindColumn(header, IdHeaders);
        var classColumn = FindColumn(header, ClassHeaders);
        var hasHeader = nameColumn >= 0 || idColumn >= 0 || classColumn >= 0;
        nameColumn = nameColumn >= 0 ? nameColumn : delimiter is null ? 0 : GuessNameColumn(rows, hasHeader ? 1 : 0);

        return rows.Skip(hasHeader ? 1 : 0)
            .Select(row => new RosterEntry(Get(row, nameColumn), Get(row, idColumn), Get(row, classColumn)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .DistinctBy(entry => (entry.Name.Trim(), entry.StudentId.Trim()), StringTupleComparer.Instance)
            .Select(entry => new RosterEntry(entry.Name.Trim(), entry.StudentId.Trim(), entry.ClassName.Trim()))
            .ToArray();
    }

    private static char? DetectDelimiter(string text)
    {
        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var candidates = new[] { '\t', ',', ';' };
        var selected = candidates.Select(value => (value, count: firstLine.Count(character => character == value)))
            .OrderByDescending(item => item.count).First();
        return selected.count > 0 ? selected.value : null;
    }

    private static List<string[]> ParseRows(string text, char? delimiter)
    {
        if (delimiter is null)
            return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(line => new[] { line.Trim() }).ToList();

        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"') { cell.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (!quoted && character == delimiter)
            {
                row.Add(cell.ToString().Trim()); cell.Clear();
            }
            else if (!quoted && (character == '\r' || character == '\n'))
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                row.Add(cell.ToString().Trim()); cell.Clear(); rows.Add(row.ToArray()); row.Clear();
            }
            else cell.Append(character);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString().Trim()); rows.Add(row.ToArray()); }
        return rows;
    }

    private static int GuessNameColumn(IReadOnlyList<string[]> rows, int start) =>
        Enumerable.Range(0, rows.Skip(start).Select(row => row.Length).DefaultIfEmpty(1).Max())
            .OrderBy(column => rows.Skip(start).Count(row => !string.IsNullOrWhiteSpace(Get(row, column)) && !Get(row, column).All(char.IsDigit)))
            .Last();

    private static int FindColumn(IReadOnlyList<string> header, IReadOnlyCollection<string> aliases) =>
        Enumerable.Range(0, header.Count).FirstOrDefault(index => aliases.Contains(header[index]), -1);

    private static string NormalizeHeader(string value) => new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string Get(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count ? row[index] : "";

    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static readonly StringTupleComparer Instance = new();
        public bool Equals((string, string) x, (string, string) y) => StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1) && StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);
        public int GetHashCode((string, string) value) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item1), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item2));
    }
}
