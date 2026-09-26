using System.Collections.Immutable;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;

namespace ExusiAI.FileViewer.Core;

public sealed class PdfFileViewerProvider : IFileViewerProvider
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" };
    public string Id => "exusiai.viewer.pdf";
    public int Priority => 110;
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public ValueTask<ViewerDocument> OpenAsync(string filePath, ViewerOpenOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = SafeFileAccess.Inspect(filePath, options);
        ValidateHeader(file);
        try
        {
            ViewerDocument document = new PdfDocument(file, options);
            return ValueTask.FromResult(document);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new FileRejectedException($"PDF 无法安全打开：{exception.Message}");
        }
    }

    private static void ValidateHeader(FileInfo file)
    {
        Span<byte> header = stackalloc byte[5];
        using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Read(header) != header.Length || !header.SequenceEqual("%PDF-"u8))
            throw new FileRejectedException("The file does not contain a valid PDF header.");
    }
}

internal sealed class PdfDocument : ViewerDocument, IPagedPreviewDocument
{
    private readonly IDocReader reader;
    private readonly int cacheLimit;
    private readonly Dictionary<int, DocumentPagePreview> cache = [];
    private readonly LinkedList<int> cacheLru = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public int PageCount { get; }

    internal PdfDocument(FileInfo file, ViewerOpenOptions options)
        : base(new(
            file.FullName,
            file.Name,
            "PDF 分页文档",
            file.Length,
            ViewerCapabilities.Pages,
            true,
            ImmutableArray.Create(
                "页面由本地 PDFium 按需渲染；不会执行 JavaScript、启动附件或访问外部内容。",
                "内存中只保留少量最近页面，密码保护或损坏的 PDF 会被拒绝。")))
    {
        reader = DocLib.Instance.GetDocReader(file.FullName, new PageDimensions(options.PdfRenderWidth, options.PdfRenderWidth * 2));
        PageCount = reader.GetPageCount();
        if (PageCount < 1)
        {
            reader.Dispose();
            throw new FileRejectedException("PDF does not contain any readable pages.");
        }
        if (PageCount > options.MaximumPdfPages)
        {
            reader.Dispose();
            throw new FileRejectedException($"PDF 页数超过 {options.MaximumPdfPages:N0} 页安全上限。" );
        }
        cacheLimit = options.MaximumCachedDocumentPages;
    }

    public async ValueTask<DocumentPagePreview> ReadPageAsync(int pageNumber, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (pageNumber < 1 || pageNumber > PageCount) throw new ArgumentOutOfRangeException(nameof(pageNumber));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (cache.TryGetValue(pageNumber, out var cached))
            {
                Touch(pageNumber);
                return cached;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var page = await Task.Run(() => Render(pageNumber), cancellationToken).ConfigureAwait(false);
            cache[pageNumber] = page;
            cacheLru.AddFirst(pageNumber);
            while (cache.Count > cacheLimit)
            {
                var oldest = cacheLru.Last!.Value;
                cacheLru.RemoveLast();
                cache.Remove(oldest);
            }
            return page;
        }
        finally { gate.Release(); }
    }

    private DocumentPagePreview Render(int pageNumber)
    {
        using var page = reader.GetPageReader(pageNumber - 1);
        var pixels = page.GetImage();
        var width = page.GetPageWidth();
        var height = page.GetPageHeight();
        var expectedLength = checked(width * height * 4);
        if (pixels.Length != expectedLength)
            throw new InvalidDataException("PDFium returned an unexpected page buffer size.");
        return new(pageNumber, width, height, checked(width * 4), DocumentPixelFormat.Bgra32,
            pixels, page.GetText() ?? string.Empty);
    }

    private void Touch(int pageNumber)
    {
        cacheLru.Remove(pageNumber);
        cacheLru.AddFirst(pageNumber);
    }

    public override async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            cache.Clear();
            cacheLru.Clear();
            reader.Dispose();
        }
        finally
        {
            gate.Release();
        }
    }
}
