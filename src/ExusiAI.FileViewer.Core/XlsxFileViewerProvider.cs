using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

namespace ExusiAI.FileViewer.Core;

public sealed class XlsxFileViewerProvider : IFileViewerProvider
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xlsx" };
    public string Id => "exusiai.viewer.xlsx";
    public int Priority => 100;
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public ValueTask<ViewerDocument> OpenAsync(string filePath, ViewerOpenOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = SafeFileAccess.Inspect(filePath, options);
        using (var package = OpenXmlPackageGuard.Open(info, options))
        {
            using (package.OpenRequiredXml("xl/workbook.xml")) { }
            using (package.OpenRequiredXml("xl/_rels/workbook.xml.rels")) { }
        }

        ViewerDocument document = new StreamingXlsxDocument(info, options);
        return ValueTask.FromResult(document);
    }
}

public sealed class StreamingXlsxDocument : ViewerDocument, ITabularPreviewDocument
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string OfficeRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private readonly FileInfo file;
    private readonly ViewerOpenOptions options;
    private int reading;
    private bool disposed;

    internal StreamingXlsxDocument(FileInfo file, ViewerOpenOptions options)
        : base(new(
            file.FullName,
            file.Name,
            "XLSX 工作表预览",
            file.Length,
            ViewerCapabilities.IncrementalRead | ViewerCapabilities.Tabular,
            true,
            ImmutableArray.Create(
                "按工作表流式读取单元格缓存值，不计算公式，也不承诺与 Excel 相同的版式。",
                "图表、图片、批注、宏、外部链接、数据连接、条件格式与嵌入对象不会渲染或执行。")))
    {
        this.file = file;
        this.options = options;
    }

    public async IAsyncEnumerable<CsvPage> ReadPagesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Interlocked.Exchange(ref reading, 1) != 0) throw new InvalidOperationException("This document already has an active reader.");

        try
        {
            using var package = OpenXmlPackageGuard.Open(file, options);
            var relationships = await ReadWorksheetRelationshipsAsync(package, cancellationToken).ConfigureAwait(false);
            var sheets = await ReadSheetsAsync(package, relationships, cancellationToken).ConfigureAwait(false);
            var sharedStrings = await ReadSharedStringsAsync(package, cancellationToken).ConfigureAwait(false);

            for (var sheetIndex = 0; sheetIndex < sheets.Count; sheetIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sheet = sheets[sheetIndex];
                var page = ImmutableArray.CreateBuilder<ImmutableArray<string>>(options.CsvRowsPerPage);
                long startRow = 0;
                long rowNumber = 0;

                using var reader = package.OpenRequiredXml(sheet.Path);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != SpreadsheetNamespace || reader.LocalName != "row")
                        continue;

                    var row = await ReadRowAsync(reader, sharedStrings, cancellationToken).ConfigureAwait(false);
                    page.Add(row);
                    rowNumber++;
                    if (page.Count != options.CsvRowsPerPage) continue;

                    yield return new(startRow, page.ToImmutable(), false, sheet.Name);
                    startRow = rowNumber;
                    page.Clear();
                }

                var isFinal = sheetIndex == sheets.Count - 1;
                if (page.Count > 0 || rowNumber == 0)
                    yield return new(startRow, page.ToImmutable(), isFinal, sheet.Name);
            }
        }
        finally
        {
            Volatile.Write(ref reading, 0);
        }
    }

    private async Task<ImmutableArray<string>> ReadRowAsync(XmlReader rowReader, IReadOnlyList<string> sharedStrings, CancellationToken cancellationToken)
    {
        var cells = ImmutableArray.CreateBuilder<string>();
        using var row = rowReader.ReadSubtree();
        await row.ReadAsync().ConfigureAwait(false);

        while (await row.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.NodeType != XmlNodeType.Element || row.NamespaceURI != SpreadsheetNamespace || row.LocalName != "c") continue;

            var reference = row.GetAttribute("r") ?? string.Empty;
            var type = row.GetAttribute("t");
            var column = ParseColumnIndex(reference);
            if (column >= options.MaximumCsvFieldsPerRow)
                throw new FileRejectedException($"XLSX row exceeds the {options.MaximumCsvFieldsPerRow:N0}-column safety limit.");

            while (cells.Count <= column) cells.Add(string.Empty);
            cells[column] = await ReadCellValueAsync(row, type, sharedStrings, cancellationToken).ConfigureAwait(false);
        }

        return cells.ToImmutable();
    }

    private async Task<string> ReadCellValueAsync(XmlReader cellReader, string? type, IReadOnlyList<string> sharedStrings, CancellationToken cancellationToken)
    {
        string? value = null;
        var inline = new StringBuilder();
        using var cell = cellReader.ReadSubtree();
        await cell.ReadAsync().ConfigureAwait(false);

        while (await cell.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cell.NodeType != XmlNodeType.Element || cell.NamespaceURI != SpreadsheetNamespace) continue;
            if (cell.LocalName == "v")
            {
                value = await cell.ReadElementContentAsStringAsync().ConfigureAwait(false);
            }
            else if (type == "inlineStr" && cell.LocalName == "t")
            {
                AppendBounded(inline, await cell.ReadElementContentAsStringAsync().ConfigureAwait(false));
            }
        }

        if (type == "inlineStr") return inline.ToString();
        if (value is null) return string.Empty;
        if (value.Length > options.MaximumCsvFieldCharacters)
            throw new FileRejectedException("XLSX cell value exceeds the configured field safety limit.");

        if (type == "s")
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= sharedStrings.Count)
                throw new FileRejectedException("XLSX contains an invalid shared-string reference.");
            return sharedStrings[index];
        }

        return type == "b" ? value == "1" ? "TRUE" : "FALSE" : value;
    }

    private async Task<List<string>> ReadSharedStringsAsync(OpenXmlPackageGuard package, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        if (!package.ContainsEntry("xl/sharedStrings.xml")) return result;

        using var reader = package.OpenRequiredXml("xl/sharedStrings.xml");
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != SpreadsheetNamespace || reader.LocalName != "si") continue;
            if (result.Count >= options.MaximumSharedStrings)
                throw new FileRejectedException($"XLSX exceeds the {options.MaximumSharedStrings:N0} shared-string safety limit.");

            var text = new StringBuilder();
            using var item = reader.ReadSubtree();
            await item.ReadAsync().ConfigureAwait(false);
            while (await item.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.NodeType == XmlNodeType.Element && item.NamespaceURI == SpreadsheetNamespace && item.LocalName == "t")
                    AppendBounded(text, await item.ReadElementContentAsStringAsync().ConfigureAwait(false));
            }
            result.Add(text.ToString());
        }
        return result;
    }

    private async Task<List<SheetPart>> ReadSheetsAsync(OpenXmlPackageGuard package, IReadOnlyDictionary<string, string> relationships, CancellationToken cancellationToken)
    {
        var result = new List<SheetPart>();
        using var reader = package.OpenRequiredXml("xl/workbook.xml");
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != SpreadsheetNamespace || reader.LocalName != "sheet") continue;

            var name = reader.GetAttribute("name") ?? $"工作表 {result.Count + 1}";
            var relationshipId = reader.GetAttribute("id", OfficeRelationshipNamespace);
            if (relationshipId is null || !relationships.TryGetValue(relationshipId, out var path)) continue;
            if (!package.ContainsEntry(path)) throw new FileRejectedException($"XLSX worksheet part '{path}' is missing.");
            result.Add(new(name, path));
        }

        if (result.Count == 0) throw new FileRejectedException("XLSX does not contain a readable worksheet.");
        return result;
    }

    private static async Task<Dictionary<string, string>> ReadWorksheetRelationshipsAsync(OpenXmlPackageGuard package, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = package.OpenRequiredXml("xl/_rels/workbook.xml.rels");
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != PackageRelationshipNamespace || reader.LocalName != "Relationship") continue;
            if (string.Equals(reader.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)) continue;

            var id = reader.GetAttribute("Id");
            var target = reader.GetAttribute("Target");
            var type = reader.GetAttribute("Type");
            if (id is null || target is null || type is null || !type.EndsWith("/worksheet", StringComparison.Ordinal)) continue;
            result[id] = NormalizeWorkbookTarget(target);
        }
        return result;
    }

    private static string NormalizeWorkbookTarget(string target)
    {
        var normalized = target.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Split('/').Any(part => part is ".." or "."))
            throw new FileRejectedException("XLSX contains an unsafe worksheet relationship target.");
        return normalized.StartsWith("xl/", StringComparison.Ordinal) ? normalized : $"xl/{normalized}";
    }

    private static int ParseColumnIndex(string reference)
    {
        if (string.IsNullOrEmpty(reference)) return 0;
        var value = 0;
        var letters = 0;
        foreach (var character in reference)
        {
            if (!((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z'))) break;
            value = checked(value * 26 + (char.ToUpperInvariant(character) - 'A' + 1));
            letters++;
        }
        if (letters == 0) return 0;
        return value - 1;
    }

    private void AppendBounded(StringBuilder builder, string value)
    {
        if ((long)builder.Length + value.Length > options.MaximumCsvFieldCharacters ||
            (long)builder.Length + value.Length > options.MaximumSharedStringCharacters)
            throw new FileRejectedException("XLSX text value exceeds the configured safety limit.");
        builder.Append(value);
    }

    public override ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }

    private sealed record SheetPart(string Name, string Path);
}
