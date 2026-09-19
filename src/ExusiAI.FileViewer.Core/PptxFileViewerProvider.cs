using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;

namespace ExusiAI.FileViewer.Core;

public sealed class PptxFileViewerProvider : IFileViewerProvider
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pptx" };

    public string Id => "exusiai.viewer.pptx";
    public int Priority => 100;
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public ValueTask<ViewerDocument> OpenAsync(string filePath, ViewerOpenOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = SafeFileAccess.Inspect(filePath, options);

        ImmutableArray<PptxSlide> slides;
        using (var package = OpenXmlPackageGuard.Open(info, options))
            slides = PptxPackageReader.ReadSlides(package, options);

        ViewerDocument document = new StreamingPptxDocument(info, options, slides);
        return ValueTask.FromResult(document);
    }
}

public sealed class StreamingPptxDocument : ViewerDocument, ISlidePreviewDocument
{
    private readonly FileInfo file;
    private readonly ViewerOpenOptions options;
    private readonly ImmutableArray<PptxSlide> slides;
    private readonly Dictionary<int, SlidePreview> slideCache = [];
    private readonly LinkedList<int> cacheLru = [];
    private readonly object cacheGate = new();
    private int reading;
    private bool disposed;

    internal StreamingPptxDocument(FileInfo file, ViewerOpenOptions options, ImmutableArray<PptxSlide> slides)
        : base(new(
            file.FullName,
            file.Name,
            "PPTX 结构化幻灯片",
            file.Length,
            ViewerCapabilities.IncrementalRead | ViewerCapabilities.Slides,
            true,
            ImmutableArray.Create(
                "阶段 2C 当前按幻灯片顺序提取可见文本，保留幻灯片边界，但不承诺与 PowerPoint 相同的版式。",
                "图片、图表、SmartArt、动画、转场、音视频、批注、宏、外部链接和嵌入对象不会渲染或执行。")))
    {
        this.file = file;
        this.options = options;
        this.slides = slides;
    }

    public int SlideCount => slides.Length;

    public async ValueTask<SlidePreview> ReadSlideAsync(int slideNumber, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (slideNumber < 1 || slideNumber > slides.Length)
            throw new ArgumentOutOfRangeException(nameof(slideNumber), $"Slide number must be between 1 and {slides.Length:N0}.");

        lock (cacheGate)
        {
            if (slideCache.TryGetValue(slideNumber, out var cached))
            {
                cacheLru.Remove(slideNumber);
                cacheLru.AddFirst(slideNumber);
                return cached;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var package = OpenXmlPackageGuard.Open(file, options);
        var text = await PptxPackageReader.ReadSlideTextAsync(package, slides[slideNumber - 1].PartName, options, cancellationToken).ConfigureAwait(false);
        var preview = new SlidePreview(slideNumber, text, slideNumber == slides.Length);

        lock (cacheGate)
        {
            slideCache[slideNumber] = preview;
            cacheLru.Remove(slideNumber);
            cacheLru.AddFirst(slideNumber);
            while (cacheLru.Count > options.MaximumCachedSlides)
            {
                var oldest = cacheLru.Last!.Value;
                cacheLru.RemoveLast();
                slideCache.Remove(oldest);
            }
        }

        return preview;
    }

    public async IAsyncEnumerable<SlidePreview> ReadSlidesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Interlocked.Exchange(ref reading, 1) != 0)
            throw new InvalidOperationException("This document already has an active reader.");

        try
        {
            for (var index = 0; index < slides.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return await ReadSlideAsync(index + 1, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Volatile.Write(ref reading, 0);
        }
    }

    public override ValueTask DisposeAsync()
    {
        disposed = true;
        lock (cacheGate) { slideCache.Clear(); cacheLru.Clear(); }
        return ValueTask.CompletedTask;
    }
}

internal sealed record PptxSlide(string PartName);

internal static class PptxPackageReader
{
    private const string PresentationNamespace = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string OfficeRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string SlideRelationshipSuffix = "/slide";

    public static ImmutableArray<PptxSlide> ReadSlides(OpenXmlPackageGuard package, ViewerOpenOptions options)
    {
        var relationships = ReadPresentationRelationships(package);
        using var reader = package.OpenRequiredXml("ppt/presentation.xml");
        var slides = ImmutableArray.CreateBuilder<PptxSlide>();

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                reader.LocalName != "sldId" ||
                reader.NamespaceURI != PresentationNamespace)
            {
                continue;
            }

            if (slides.Count >= options.MaximumPresentationSlides)
                throw new FileRejectedException($"PPTX contains more than {options.MaximumPresentationSlides:N0} slides.");

            var relationshipId = reader.GetAttribute("id", OfficeRelationshipNamespace);
            if (string.IsNullOrWhiteSpace(relationshipId) || !relationships.TryGetValue(relationshipId, out var target))
                throw new FileRejectedException("PPTX presentation references a missing or unsupported slide relationship.");

            var partName = ResolvePresentationTarget(target);
            using (package.OpenRequiredXml(partName)) { }
            slides.Add(new(partName));
        }

        if (slides.Count == 0)
            throw new FileRejectedException("PPTX presentation does not contain a readable slide.");

        return slides.ToImmutable();
    }

    private static Dictionary<string, string> ReadPresentationRelationships(OpenXmlPackageGuard package)
    {
        using var reader = package.OpenRequiredXml("ppt/_rels/presentation.xml.rels");
        var relationships = new Dictionary<string, string>(StringComparer.Ordinal);

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
                type is null || !type.EndsWith(SlideRelationshipSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            relationships[id] = target;
        }

        return relationships;
    }

    private static string ResolvePresentationTarget(string target)
    {
        var normalized = target.Replace('\\', '/');
        if (normalized.Contains('\0') ||
            normalized.Contains(':') ||
            normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new FileRejectedException("PPTX slide relationship contains an unsafe target.");
        }

        normalized = normalized.TrimStart('/');
        return normalized.StartsWith("ppt/", StringComparison.Ordinal) ? normalized : $"ppt/{normalized}";
    }

    public static async Task<string> ReadSlideTextAsync(
        OpenXmlPackageGuard package,
        string partName,
        ViewerOpenOptions options,
        CancellationToken cancellationToken)
    {
        using var reader = package.OpenRequiredXml(partName);
        var text = new StringBuilder();
        var paragraphHasText = false;

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.Element &&
                reader.NamespaceURI == DrawingNamespace &&
                reader.LocalName == "t")
            {
                var run = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                if ((long)text.Length + run.Length > options.MaximumPresentationTextCharactersPerSlide)
                    throw new FileRejectedException("PPTX slide text exceeds the configured per-slide safety limit.");
                text.Append(run);
                paragraphHasText = true;
                continue;
            }

            if (reader.NodeType == XmlNodeType.EndElement &&
                reader.NamespaceURI == DrawingNamespace &&
                reader.LocalName == "p" &&
                paragraphHasText)
            {
                text.AppendLine();
                paragraphHasText = false;
            }
        }

        return text.ToString().TrimEnd();
    }
}
