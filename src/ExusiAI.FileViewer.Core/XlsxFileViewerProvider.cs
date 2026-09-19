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

        XlsxWorksheet worksheet;
        using (var package = OpenXmlPackageGuard.Open(info, options))
        {
            worksheet = XlsxPackageReader.ReadFirstWorksheet(package);
            using var worksheetReader = package.OpenRequiredXml(worksheet.PartName);
        }

        ViewerDocument document = new StreamingXlsxDocument(info, options, worksheet);
        return ValueTask.FromResult(document);
    }
}

public sealed class StreamingXlsxDocument : ViewerDocument, ITabularPreviewDocument
{
    private readonly FileInfo file;
    private readonly ViewerOpenOptions options;
    private readonly XlsxWorksheet worksheet;
    private int reading;
    private bool disposed;

    internal StreamingXlsxDocument(FileInfo file, ViewerOpenOptions options, XlsxWorksheet worksheet)
        : base(new(
            file.FullName,
            file.Name,
            $"XLSX · {worksheet.Name}",
            file.Length,
            ViewerCapabilities.IncrementalRead | ViewerCapabilities.Tabular,
            true,
            ImmutableArray.Create(
                $"阶段 2B 当前预览首个工作表“{worksheet.Name}”；多工作表切换将在后续查看工作流中加入。",
                "公式不会执行，仅显示文件中已有的缓存结果；样式、日期格式、合并单元格、图表、批注、宏和外部链接不渲染或执行。")))
    {
        this.file = file;
        this.options = options;
        this.worksheet = worksheet;
    }

    public async IAsyncEnumerable<TabularPage> ReadPagesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Interlocked.Exchange(ref reading, 1) != 0)
            throw new InvalidOperationException("This document already has an active reader.");

        try
        {
            using var package = OpenXmlPackageGuard.Open(file, options);
            var sharedStrings = await XlsxPackageReader.ReadSharedStringsAsync(package, options, cancellationToken).ConfigureAwait(false);
            using var reader = package.OpenRequiredXml(worksheet.PartName);

            var page = ImmutableArray.CreateBuilder<ImmutableArray<string>>(options.SpreadsheetRowsPerPage);
            long startRow = 0;
            long rowCount = 0;

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element ||
                    reader.LocalName != "row" ||
                    reader.NamespaceURI != XlsxPackageReader.SpreadsheetNamespace)
                {
                    continue;
                }

                page.Add(XlsxPackageReader.ReadRow(reader, sharedStrings, options));
                rowCount++;

                if (page.Count != options.SpreadsheetRowsPerPage)
                    continue;

                yield return new(startRow, page.ToImmutable(), false);
                startRow = rowCount;
                page.Clear();
            }

            yield return new(startRow, page.ToImmutable(), true);
        }
        finally
        {
            Volatile.Write(ref reading, 0);
        }
    }

    public override ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed record XlsxWorksheet(string Name, string PartName);

internal static class XlsxPackageReader
{
    internal const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string OfficeRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string WorksheetRelationshipSuffix = "/worksheet";

    public static XlsxWorksheet ReadFirstWorksheet(OpenXmlPackageGuard package)
    {
        var relationships = ReadWorkbookRelationships(package);
        using var reader = package.OpenRequiredXml("xl/workbook.xml");

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                reader.LocalName != "sheet" ||
                reader.NamespaceURI != SpreadsheetNamespace)
            {
                continue;
            }

            var name = reader.GetAttribute("name");
            var relationshipId = reader.GetAttribute("id", OfficeRelationshipNamespace);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(relationshipId))
                throw new FileRejectedException("XLSX workbook contains a worksheet with missing name or relationship id.");

            if (!relationships.TryGetValue(relationshipId, out var target))
                throw new FileRejectedException($"XLSX worksheet '{name}' references a missing or unsupported relationship.");

            return new(name, ResolveWorkbookTarget(target));
        }

        throw new FileRejectedException("XLSX workbook does not contain a readable worksheet.");
    }

    private static Dictionary<string, string> ReadWorkbookRelationships(OpenXmlPackageGuard package)
    {
        var relationships = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = package.OpenRequiredXml("xl/_rels/workbook.xml.rels");

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                reader.LocalName != "Relationship" ||
                reader.NamespaceURI != PackageRelationshipNamespace)
            {
                continue;
            }

            var id = reader.GetAttribute("Id");
            var type = reader.GetAttribute("Type");
            var target = reader.GetAttribute("Target");
            var targetMode = reader.GetAttribute("TargetMode");

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(target) ||
                string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase) ||
                type is null || !type.EndsWith(WorksheetRelationshipSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            relationships[id] = target;
        }

        return relationships;
    }

    private static string ResolveWorkbookTarget(string target)
    {
        var normalized = target.Replace('\\', '/');
        if (normalized.Contains('\0') ||
            normalized.Contains(':') ||
            normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new FileRejectedException("XLSX worksheet relationship contains an unsafe target.");
        }

        normalized = normalized.TrimStart('/');
        if (normalized.StartsWith("xl/", StringComparison.Ordinal))
            return normalized;

        return $"xl/{normalized}";
    }

    public static async Task<ImmutableArray<string>> ReadSharedStringsAsync(
        OpenXmlPackageGuard package,
        ViewerOpenOptions options,
        CancellationToken cancellationToken)
    {
        using var reader = package.OpenOptionalXml("xl/sharedStrings.xml");
        if (reader is null)
            return ImmutableArray<string>.Empty;

        var values = ImmutableArray.CreateBuilder<string>();
        StringBuilder? current = null;
        long totalCharacters = 0;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.Element &&
                reader.NamespaceURI == SpreadsheetNamespace &&
                reader.LocalName == "si")
            {
                if (values.Count >= options.MaximumSpreadsheetSharedStrings)
                    throw new FileRejectedException($"XLSX shared string table exceeds the {options.MaximumSpreadsheetSharedStrings:N0}-entry safety limit.");
                current = new StringBuilder();
                continue;
            }

            if (current is not null &&
                reader.NodeType == XmlNodeType.Element &&
                reader.NamespaceURI == SpreadsheetNamespace &&
                reader.LocalName == "t")
            {
                var text = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                totalCharacters = checked(totalCharacters + text.Length);
                if (totalCharacters > options.MaximumXmlCharacters)
                    throw new FileRejectedException("XLSX shared strings exceed the configured XML character safety limit.");
                if (current.Length + text.Length > options.MaximumCsvFieldCharacters)
                    throw new FileRejectedException("XLSX shared string exceeds the configured cell text safety limit.");
                current.Append(text);
                continue;
            }

            if (current is not null &&
                reader.NodeType == XmlNodeType.EndElement &&
                reader.NamespaceURI == SpreadsheetNamespace &&
                reader.LocalName == "si")
            {
                values.Add(current.ToString());
                current = null;
            }
        }

        return values.ToImmutable();
    }

    public static ImmutableArray<string> ReadRow(
        XmlReader worksheetReader,
        ImmutableArray<string> sharedStrings,
        ViewerOpenOptions options)
    {
        using var rowReader = worksheetReader.ReadSubtree();
        var cells = new SortedDictionary<int, string>();
        var sequentialColumn = 0;

        while (rowReader.Read())
        {
            if (rowReader.NodeType != XmlNodeType.Element ||
                rowReader.LocalName != "c" ||
                rowReader.NamespaceURI != SpreadsheetNamespace)
            {
                continue;
            }

            var reference = rowReader.GetAttribute("r");
            var column = string.IsNullOrEmpty(reference) ? sequentialColumn : ParseColumnIndex(reference);
            sequentialColumn = column + 1;

            if (column < 0 || column >= options.MaximumSpreadsheetColumns)
                throw new FileRejectedException($"XLSX row exceeds the {options.MaximumSpreadsheetColumns:N0}-column safety limit.");

            cells[column] = ReadCell(rowReader, sharedStrings, options);
        }

        if (cells.Count == 0)
            return ImmutableArray<string>.Empty;

        var lastColumn = cells.Keys.Max();
        var row = ImmutableArray.CreateBuilder<string>(lastColumn + 1);
        for (var column = 0; column <= lastColumn; column++)
            row.Add(cells.TryGetValue(column, out var value) ? value : string.Empty);
        return row.ToImmutable();
    }

    private static string ReadCell(XmlReader worksheetReader, ImmutableArray<string> sharedStrings, ViewerOpenOptions options)
    {
        var cellType = worksheetReader.GetAttribute("t");
        using var cellReader = worksheetReader.ReadSubtree();
        string? rawValue = null;
        StringBuilder? inlineText = null;

        while (cellReader.Read())
        {
            if (cellReader.NodeType != XmlNodeType.Element || cellReader.NamespaceURI != SpreadsheetNamespace)
                continue;

            if (cellReader.LocalName == "v")
            {
                rawValue = cellReader.ReadElementContentAsString();
            }
            else if (cellReader.LocalName == "t")
            {
                inlineText ??= new StringBuilder();
                var text = cellReader.ReadElementContentAsString();
                if (inlineText.Length + text.Length > options.MaximumCsvFieldCharacters)
                    throw new FileRejectedException("XLSX cell text exceeds the configured cell text safety limit.");
                inlineText.Append(text);
            }
        }

        string value;
        switch (cellType)
        {
            case "s":
                if (!int.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                    index < 0 || index >= sharedStrings.Length)
                {
                    throw new FileRejectedException("XLSX cell references an invalid shared string index.");
                }
                value = sharedStrings[index];
                break;
            case "inlineStr":
                value = inlineText?.ToString() ?? string.Empty;
                break;
            case "b":
                value = rawValue == "1" ? "TRUE" : rawValue == "0" ? "FALSE" : rawValue ?? string.Empty;
                break;
            default:
                value = rawValue ?? inlineText?.ToString() ?? string.Empty;
                break;
        }

        if (value.Length > options.MaximumCsvFieldCharacters)
            throw new FileRejectedException("XLSX cell text exceeds the configured cell text safety limit.");

        return value;
    }

    private static int ParseColumnIndex(string reference)
    {
        long column = 0;
        var letters = 0;

        foreach (var character in reference)
        {
            if (character is >= 'A' and <= 'Z')
            {
                column = checked(column * 26 + (character - 'A' + 1));
                letters++;
            }
            else if (character is >= 'a' and <= 'z')
            {
                column = checked(column * 26 + (character - 'a' + 1));
                letters++;
            }
            else
            {
                break;
            }
        }

        if (letters == 0 || column <= 0 || column > int.MaxValue)
            throw new FileRejectedException($"XLSX cell reference '{reference}' is invalid.");

        return (int)column - 1;
    }
}
