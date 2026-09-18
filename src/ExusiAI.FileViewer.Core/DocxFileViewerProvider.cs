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
        var buffer = new StringBuilder(options.TextChunkCharacters);
        try
        {
            using var package = OpenXmlPackageGuard.Open(file, options);
            using var reader = package.OpenRequiredXml("word/document.xml");
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != WordprocessingNamespace) continue;
                switch (reader.LocalName)
                {
                    case "t":
                        Append(buffer, await reader.ReadElementContentAsStringAsync().ConfigureAwait(false));
                        break;
                    case "tab":
                        buffer.Append('\t');
                        break;
                    case "br":
                    case "cr":
                        buffer.AppendLine();
                        break;
                    case "p" when buffer.Length > 0 && buffer[^1] != '\n':
                        buffer.AppendLine();
                        break;
                }

                if (buffer.Length < options.TextChunkCharacters) continue;
                var text = buffer.ToString();
                buffer.Clear();
                yield return new(offset, text, false);
                offset += text.Length;
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

    private void Append(StringBuilder buffer, string text)
    {
        if ((long)buffer.Length + text.Length > options.MaximumXmlCharacters)
            throw new FileRejectedException("DOCX text exceeds the configured XML character safety limit.");
        buffer.Append(text);
    }

    public override ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }
}
