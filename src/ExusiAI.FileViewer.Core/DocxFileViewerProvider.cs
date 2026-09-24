using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

namespace ExusiAI.FileViewer.Core;

public sealed class DocxFileViewerProvider : IFileViewerProvider
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx" };
    public string Id => "exusiai.viewer.docx";
    public int Priority => 100;
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public ValueTask<ViewerDocument> OpenAsync(string filePath, ViewerOpenOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = SafeFileAccess.Inspect(filePath, options);
        using (var package = OpenXmlPackageGuard.Open(info, options))
        using (package.OpenRequiredXml("word/document.xml")) { }
        ViewerDocument document = new StreamingDocxDocument(info, options);
        return ValueTask.FromResult(document);
    }
}

public sealed class StreamingDocxDocument : ViewerDocument, ITextPreviewDocument
{
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private readonly FileInfo file;
    private readonly ViewerOpenOptions options;
    private int reading;
    private bool disposed;

    internal StreamingDocxDocument(FileInfo file, ViewerOpenOptions options)
        : base(new(
            file.FullName,
            file.Name,
            "DOCX 结构化文本",
            file.Length,
            ViewerCapabilities.Search | ViewerCapabilities.IncrementalRead,
            true,
            ImmutableArray.Create(
                "当前显示文档正文的结构化文本，不承诺与 Word 相同的分页和排版。",
                "图片、图表、公式、批注、修订、字体替换、外部链接与嵌入对象不会渲染或执行。")))
    {
        this.file = file;
        this.options = options;
    }

    public async IAsyncEnumerable<TextChunk> ReadChunksAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Interlocked.Exchange(ref reading, 1) != 0) throw new InvalidOperationException("This document already has an active reader.");
        long offset = 0;
        long totalCharacters = 0;
        var buffer = new StringBuilder(options.TextChunkCharacters);
        try
        {
            using var package = OpenXmlPackageGuard.Open(file, options);
            using var reader = package.OpenRequiredXml("word/document.xml");
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != WordprocessingNamespace) continue;
                string? block = reader.LocalName switch
                {
                    "p" => await ReadParagraphAsync(reader, cancellationToken).ConfigureAwait(false),
                    "tbl" => await ReadTableAsync(reader, cancellationToken).ConfigureAwait(false),
                    _ => null
                };
                if (block is null) continue;
                Append(buffer, block, ref totalCharacters);

                while (buffer.Length >= options.TextChunkCharacters)
                {
                    var text = buffer.ToString(0, options.TextChunkCharacters);
                    buffer.Remove(0, options.TextChunkCharacters);
                    yield return new(offset, text, false);
                    offset += text.Length;
                }
            }

            if (buffer.Length > 0)
            {
                var text = buffer.ToString();
                yield return new(offset, text, false);
                offset += text.Length;
            }
            yield return new(offset, string.Empty, true);
        }
        finally { Volatile.Write(ref reading, 0); }
    }

    private void Append(StringBuilder buffer, string text, ref long totalCharacters)
    {
        totalCharacters = checked(totalCharacters + text.Length);
        if (totalCharacters > options.MaximumXmlCharacters)
            throw new FileRejectedException("DOCX text exceeds the configured XML character safety limit.");
        buffer.Append(text);
    }

    private static async Task<string> ReadParagraphAsync(XmlReader source, CancellationToken cancellationToken)
    {
        using var reader = source.ReadSubtree();
        var text = new StringBuilder();
        string? style = null;
        var numbered = false;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != WordprocessingNamespace) continue;
            switch (reader.LocalName)
            {
                case "pStyle":
                    style = reader.GetAttribute("val", WordprocessingNamespace);
                    break;
                case "numPr":
                    numbered = true;
                    break;
                case "t":
                    text.Append(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                    break;
                case "tab":
                    text.Append('\t');
                    break;
                case "br":
                case "cr":
                    text.AppendLine();
                    break;
            }
        }

        var prefix = ResolveParagraphPrefix(style, numbered);
        return prefix + text.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string ResolveParagraphPrefix(string? style, bool numbered)
    {
        if (string.Equals(style, "Title", StringComparison.OrdinalIgnoreCase)) return "# ";
        if (style?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true)
        {
            var suffix = style["Heading".Length..];
            var level = int.TryParse(suffix, out var parsed) ? Math.Clamp(parsed, 1, 6) : 2;
            return new string('#', level) + " ";
        }
        return numbered ? "• " : string.Empty;
    }

    private static async Task<string> ReadTableAsync(XmlReader source, CancellationToken cancellationToken)
    {
        using var reader = source.ReadSubtree();
        var table = new StringBuilder();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element ||
                reader.NamespaceURI != WordprocessingNamespace ||
                reader.LocalName != "tr") continue;
            var cells = await ReadTableRowAsync(reader, cancellationToken).ConfigureAwait(false);
            table.Append("| ").Append(string.Join(" | ", cells)).AppendLine(" |");
        }
        return table.AppendLine().ToString();
    }

    private static async Task<IReadOnlyList<string>> ReadTableRowAsync(XmlReader source, CancellationToken cancellationToken)
    {
        using var reader = source.ReadSubtree();
        var cells = new List<string>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element ||
                reader.NamespaceURI != WordprocessingNamespace ||
                reader.LocalName != "tc") continue;
            cells.Add(await ReadTableCellAsync(reader, cancellationToken).ConfigureAwait(false));
        }
        return cells;
    }

    private static async Task<string> ReadTableCellAsync(XmlReader source, CancellationToken cancellationToken)
    {
        using var reader = source.ReadSubtree();
        var text = new StringBuilder();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != WordprocessingNamespace) continue;
            if (reader.LocalName == "t")
                text.Append(await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
            else if (reader.LocalName == "tab")
                text.Append(' ');
            else if (reader.LocalName == "p" && text.Length > 0 && text[^1] != ' ')
                text.Append(' ');
        }
        return text.ToString().Trim().Replace("|", "\\|", StringComparison.Ordinal);
    }

    public override ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }
}
