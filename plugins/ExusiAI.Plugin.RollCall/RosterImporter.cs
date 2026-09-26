using System.Text;
using System.IO.Compression;
using System.Xml.Linq;

namespace ExusiAI.Plugin.RollCall;

public sealed record RosterEntry(string Name, string StudentId = "", string ClassName = "");

public static class RosterImporter
{
    private static readonly string[] NameHeaders = ["姓名", "名字", "name", "studentname", "student_name"];
    private static readonly string[] IdHeaders = ["学号", "编号", "id", "studentid", "student_id"];
    private static readonly string[] ClassHeaders = ["班级", "行政班", "class", "classname", "class_name"];

    public static async Task<IReadOnlyList<RosterEntry>> ParseFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return await ParseXlsxAsync(path, cancellationToken).ConfigureAwait(false);
        return Parse(await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false));
    }

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

    private static Task<IReadOnlyList<RosterEntry>> ParseXlsxAsync(string path, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(path);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        var shared = ReadXml(archive, "xl/sharedStrings.xml")?.Descendants(spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(spreadsheet + "t").Select(text => text.Value))).ToArray() ?? [];
        var workbook = ReadRequiredXml(archive, "xl/workbook.xml");
        var relationshipId = workbook.Descendants(spreadsheet + "sheet").FirstOrDefault()?.Attribute(relationships + "id")?.Value
            ?? throw new InvalidDataException("Excel 工作簿没有可读取的工作表。 ");
        var rels = ReadRequiredXml(archive, "xl/_rels/workbook.xml.rels");
        var target = rels.Descendants(packageRelationships + "Relationship")
            .FirstOrDefault(item => item.Attribute("Id")?.Value == relationshipId)?.Attribute("Target")?.Value
            ?? throw new InvalidDataException("Excel 工作表关系无效。 ");
        var sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target.TrimStart('/');
        var sheet = ReadRequiredXml(archive, NormalizeZipPath(sheetPath));
        var lines = new List<string>();
        foreach (var row in sheet.Descendants(spreadsheet + "row"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new SortedDictionary<int, string>();
            foreach (var cell in row.Elements(spreadsheet + "c"))
            {
                var reference = cell.Attribute("r")?.Value ?? "A1";
                var column = ColumnIndex(reference);
                var type = cell.Attribute("t")?.Value;
                var raw = cell.Element(spreadsheet + "v")?.Value ?? string.Concat(cell.Descendants(spreadsheet + "t").Select(text => text.Value));
                values[column] = type == "s" && int.TryParse(raw, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < shared.Length ? shared[sharedIndex] : raw;
            }
            if (values.Count == 0) continue;
            lines.Add(string.Join('\t', Enumerable.Range(0, values.Keys.Max() + 1).Select(index => EscapeTab(values.GetValueOrDefault(index, "")))));
        }
        return Task.FromResult(Parse(string.Join(Environment.NewLine, lines)));
    }

    private static XDocument? ReadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null) return null;
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.None);
    }

    private static XDocument ReadRequiredXml(ZipArchive archive, string path) => ReadXml(archive, path) ?? throw new InvalidDataException($"Excel 文件缺少 {path}。 ");
    private static string NormalizeZipPath(string path)
    {
        var segments = new Stack<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (segment == "..") { if (segments.Count > 0) segments.Pop(); }
            else if (segment is not "" and not ".") segments.Push(segment);
        }
        return string.Join('/', segments.Reverse());
    }
    private static int ColumnIndex(string reference)
    {
        var result = 0;
        foreach (var character in reference.TakeWhile(char.IsLetter)) result = result * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        return Math.Max(0, result - 1);
    }
    private static string EscapeTab(string value) => value.Contains('\t') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
