using System.Runtime.CompilerServices;
using System.IO.Compression;
using b2xtranslator.OpenXmlLib;
using b2xtranslator.OpenXmlLib.PresentationML;
using b2xtranslator.PptFileFormat;
using b2xtranslator.PresentationMLMapping;
using b2xtranslator.StructuredStorage.Reader;

namespace ExusiAI.FileViewer.Core;

/// <summary>Converts binary PPT with in-process library code, never an external executable.</summary>
public sealed class LegacyPptFileViewerProvider : IFileViewerProvider
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ppt" };
    private static readonly SemaphoreSlim ConversionGate = new(1, 1);
    public string Id => "exusiai.viewer.ppt";
    public int Priority => 100;
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public async ValueTask<ViewerDocument> OpenAsync(string filePath, ViewerOpenOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = SafeFileAccess.Inspect(filePath, options);
        // The binary parser is not streaming. Keep its input budget separate from large PDF/video files.
        if (source.Length < 8) throw new FileRejectedException("PPT 文件头不完整。");
        if (source.Length > options.MaximumLegacyPresentationBytes)
            throw new FileRejectedException("旧版 PPT 超出程序内转换的安全大小预算。");
        await ConversionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var directory = Path.Combine(Path.GetTempPath(), "exusiai-ppt-" + Guid.NewGuid().ToString("N"));
        ViewerDocument? inner = null;
        try
        {
            Directory.CreateDirectory(directory);
            var output = Path.Combine(directory, "preview.pptx");
            // Cancellation does not abandon a running converter: cleanup waits until it releases all handles.
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var input = SafeFileAccess.OpenPackageRead(source);
                if (input.Length > options.MaximumLegacyPresentationBytes)
                    throw new FileRejectedException("PPT 文件在打开期间变大，已拒绝解析。");
                Span<byte> header = stackalloc byte[8];
                input.ReadExactly(header);
                if (!header.SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }))
                    throw new FileRejectedException("文件不是有效的二进制 PPT 容器。");
                input.Position = 0;
                using var storage = new StructuredStorageReader(input);
                var presentation = new PowerpointDocument(storage);
                if (presentation.SlideRecords.Count > options.MaximumPresentationSlides)
                    throw new FileRejectedException("PPT 幻灯片数量超出预算。");
                cancellationToken.ThrowIfCancellationRequested();
                // Converter.Convert owns/disposes target. Its Close is NOT idempotent; do not dispose twice.
                var target = PresentationDocument.Create(output, OpenXmlPackage.DocumentType.Document);
                Converter.Convert(presentation, target);
                NormalizeGeneratedPackage(output, options, cancellationToken);
            }, CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            inner = await new PptxFileViewerProvider().OpenAsync(output, options, cancellationToken).ConfigureAwait(false);
            return new ConvertedPptDocument(source, inner, directory);
        }
        catch
        {
            if (inner is not null) await inner.DisposeAsync().ConfigureAwait(false);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            throw;
        }
        finally { ConversionGate.Release(); }
    }

    private static void NormalizeGeneratedPackage(string path, ViewerOpenOptions options, CancellationToken cancellationToken)
    {
        // Upstream emits e.g. ppt/slideMasters/../slideLayouts. Canonicalize our own generated ZIP,
        // never user input, without extracting entries or weakening the Open XML input guard.
        var normalizedPath = path + ".normalized";
        using (var input = ZipFile.OpenRead(path))
        using (var output = ZipFile.Open(normalizedPath, ZipArchiveMode.Create))
        {
            if (input.Entries.Count > options.MaximumArchiveEntries) throw new FileRejectedException("转换后的 PPT 条目数超出预算。");
            var names = new HashSet<string>(StringComparer.Ordinal);
            long total = 0;
            foreach (var entry in input.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total = checked(total + entry.Length);
                if (entry.Length > options.MaximumArchiveEntryBytes || total > options.MaximumArchiveExpandedBytes)
                    throw new FileRejectedException("转换后的 PPT 展开大小超出预算。");
                var segments = new List<string>();
                foreach (var segment in entry.FullName.Replace('\\', '/').Split('/'))
                {
                    if (segment is "" or ".") continue;
                    if (segment == "..")
                    {
                        if (segments.Count == 0) throw new FileRejectedException("转换后的 PPT 路径越界。");
                        segments.RemoveAt(segments.Count - 1);
                    }
                    else segments.Add(segment);
                }
                var name = string.Join('/', segments);
                if (name.Length == 0 || !names.Add(name)) throw new FileRejectedException("转换后的 PPT 包含无效或重复部件。");
                using var source = entry.Open();
                using var destination = output.CreateEntry(name).Open();
                source.CopyTo(destination);
            }
        }
        File.Move(normalizedPath, path, overwrite: true);
    }
}

internal sealed class ConvertedPptDocument : ViewerDocument, ISlidePreviewDocument, IEmbeddedVideoDocument
{
    private readonly ViewerDocument inner;
    private readonly string directory;
    private bool disposed;
    internal ConvertedPptDocument(FileInfo source, ViewerDocument inner, string directory)
        : base(inner.Info with
        {
            FilePath = source.FullName, DisplayName = source.Name, Length = source.Length,
            FormatName = "PPT 程序内转换预览",
            Warnings = inner.Info.Warnings.Add("旧版 PPT 使用程序内 b2xtranslator 转换；复杂排版可能降级，不执行宏或 OLE 对象。")
        })
    { this.inner = inner; this.directory = directory; }
    public int SlideCount => ((ISlidePreviewDocument)inner).SlideCount;
    public ValueTask<SlidePreview> ReadSlideAsync(int slideNumber, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return ((ISlidePreviewDocument)inner).ReadSlideAsync(slideNumber, cancellationToken);
    }
    public async IAsyncEnumerable<SlidePreview> ReadSlidesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await foreach (var slide in ((ISlidePreviewDocument)inner).ReadSlidesAsync(cancellationToken).ConfigureAwait(false)) yield return slide;
    }
    public ValueTask<byte[]> ReadVideoAsync(string partName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return ((IEmbeddedVideoDocument)inner).ReadVideoAsync(partName, cancellationToken);
    }
    public override async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try { await inner.DisposeAsync().ConfigureAwait(false); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
