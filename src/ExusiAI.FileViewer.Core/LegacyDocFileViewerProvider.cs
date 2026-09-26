using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using NPOI.HWPF;
using NPOI.HWPF.Extractor;

namespace ExusiAI.FileViewer.Core;

public sealed class LegacyDocFileViewerProvider : IFileViewerProvider
{
    private static readonly byte[] CompoundFileHeader = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".doc" };
    public string Id => "exusiai.viewer.doc";
    public int Priority => 100;
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public ValueTask<ViewerDocument> OpenAsync(string filePath, ViewerOpenOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = SafeFileAccess.Inspect(filePath, options);
        if (file.Length > options.MaximumLegacyWordBytes)
            throw new FileRejectedException($"旧版 DOC 超过 {options.MaximumLegacyWordBytes / 1024 / 1024:N0} MiB 安全解析预算；请先转换为 DOCX。" );
        ValidateHeader(file);
        ViewerDocument document = new LegacyDocDocument(file, options);
        return ValueTask.FromResult(document);
    }

    private static void ValidateHeader(FileInfo file)
    {
        Span<byte> header = stackalloc byte[CompoundFileHeader.Length];
        using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Read(header) != header.Length || !header.SequenceEqual(CompoundFileHeader))
            throw new FileRejectedException("The file is not a valid OLE compound Word document.");
    }
}

internal sealed class LegacyDocDocument : ViewerDocument, ITextPreviewDocument
{
    private readonly FileInfo file;
    private readonly ViewerOpenOptions options;
    private int reading;
    private bool disposed;

    internal LegacyDocDocument(FileInfo file, ViewerOpenOptions options)
        : base(new(
            file.FullName,
            file.Name,
            "Word 97–2003 文本",
            file.Length,
            ViewerCapabilities.Search | ViewerCapabilities.IncrementalRead,
            true,
            ImmutableArray.Create(
                "旧版二进制 DOC 以受限文本兼容模式打开，不保证与 Word 相同的分页、字体、图片或排版。",
                "宏、OLE、嵌入对象、外部链接和活动内容不会执行或渲染。")))
    {
        this.file = file;
        this.options = options;
    }

    public async IAsyncEnumerable<TextChunk> ReadChunksAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Interlocked.Exchange(ref reading, 1) != 0) throw new InvalidOperationException("This document already has an active reader.");
        try
        {
            var text = await Task.Run(ExtractText, cancellationToken).ConfigureAwait(false);
            long offset = 0;
            for (var index = 0; index < text.Length; index += options.TextChunkCharacters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = Math.Min(options.TextChunkCharacters, text.Length - index);
                var chunk = text.Substring(index, length);
                yield return new(offset, chunk, false);
                offset += length;
            }
            yield return new(offset, string.Empty, true);
        }
        finally { Volatile.Write(ref reading, 0); }
    }

    private string ExtractText()
    {
        try
        {
            using var stream = SafeFileAccess.OpenSequentialRead(file);
            using var document = new HWPFDocument(stream);
            var extractor = new WordExtractor(document);
            var text = extractor.Text ?? string.Empty;
            if (text.Length > options.MaximumLegacyWordCharacters)
                throw new FileRejectedException("旧版 DOC 解压后的文本超过安全字符预算。" );
            return text.Replace('\r', '\n').Replace("\n\n", "\n", StringComparison.Ordinal);
        }
        catch (FileRejectedException) { throw; }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            throw new FileRejectedException($"旧版 DOC 无法安全解析：{exception.Message}");
        }
    }

    public override ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }
}
