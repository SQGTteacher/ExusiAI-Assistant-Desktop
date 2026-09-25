using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;

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
            ViewerCapabilities.Search | ViewerCapabilities.IncrementalRead | ViewerCapabilities.Slides,
            true,
            ImmutableArray.Create(
                "当前会按幻灯片坐标呈现基础文本框、字号、粗体和纯色填充；复杂主题效果仍可能降级。",
                "图片、图表、SmartArt、动画、转场、音视频、批注、宏、外部链接和嵌入对象不会执行。")))
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
        var definition = slides[slideNumber - 1];
        var content = await PptxPackageReader.ReadSlideContentAsync(
            package, definition.PartName, definition.Width, definition.Height, options, cancellationToken).ConfigureAwait(false);
        var preview = new SlidePreview(slideNumber, content.Text, slideNumber == slides.Length, content.Visual);

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

internal sealed record PptxSlide(string PartName, double Width, double Height);

internal static class PptxPackageReader
{
    private const string PresentationNamespace = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string OfficeRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string SlideRelationshipSuffix = "/slide";
    private const string ImageRelationshipSuffix = "/image";

    public static ImmutableArray<PptxSlide> ReadSlides(OpenXmlPackageGuard package, ViewerOpenOptions options)
    {
        var relationships = ReadPresentationRelationships(package);
        using var reader = package.OpenRequiredXml("ppt/presentation.xml");
        var parts = new List<string>();
        double slideWidth = 12_192_000;
        double slideHeight = 6_858_000;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != PresentationNamespace)
            {
                continue;
            }

            if (reader.LocalName == "sldSz")
            {
                if (double.TryParse(reader.GetAttribute("cx"), out var width) && width > 0) slideWidth = width;
                if (double.TryParse(reader.GetAttribute("cy"), out var height) && height > 0) slideHeight = height;
                continue;
            }

            if (reader.LocalName != "sldId") continue;

            if (parts.Count >= options.MaximumPresentationSlides)
                throw new FileRejectedException($"PPTX contains more than {options.MaximumPresentationSlides:N0} slides.");

            var relationshipId = reader.GetAttribute("id", OfficeRelationshipNamespace);
            if (string.IsNullOrWhiteSpace(relationshipId) || !relationships.TryGetValue(relationshipId, out var target))
                throw new FileRejectedException("PPTX presentation references a missing or unsupported slide relationship.");

            var partName = ResolvePresentationTarget(target);
            using (package.OpenRequiredXml(partName)) { }
            parts.Add(partName);
        }

        if (parts.Count == 0)
            throw new FileRejectedException("PPTX presentation does not contain a readable slide.");

        return parts.Select(part => new PptxSlide(part, slideWidth, slideHeight)).ToImmutableArray();
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

    public static async Task<(string Text, SlideVisualPreview? Visual)> ReadSlideContentAsync(
        OpenXmlPackageGuard package,
        string partName,
        double slideWidth,
        double slideHeight,
        ViewerOpenOptions options,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = package.OpenRequiredXml(partName);
        var document = XDocument.Load(reader, LoadOptions.None);
        XNamespace p = PresentationNamespace;
        XNamespace a = DrawingNamespace;
        var elements = ImmutableArray.CreateBuilder<SlideElementPreview>();
        var images = ImmutableArray.CreateBuilder<SlideImagePreview>();
        var allText = new StringBuilder();

        foreach (var shape in document.Descendants(p + "sp"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paragraphs = shape.Descendants(a + "p")
                .Select(paragraph => string.Concat(paragraph.Descendants(a + "t").Select(node => node.Value)))
                .Where(value => value.Length > 0)
                .ToArray();
            if (paragraphs.Length == 0) continue;
            var shapeText = string.Join(Environment.NewLine, paragraphs);
            if ((long)allText.Length + shapeText.Length > options.MaximumPresentationTextCharactersPerSlide)
                throw new FileRejectedException("PPTX slide text exceeds the configured per-slide safety limit.");
            if (allText.Length > 0) allText.AppendLine();
            allText.Append(shapeText);

            var transform = shape.Descendants(a + "xfrm").FirstOrDefault();
            var offset = transform?.Element(a + "off");
            var extent = transform?.Element(a + "ext");
            if (!TryNumber(offset, "x", out var x) || !TryNumber(offset, "y", out var y) ||
                !TryNumber(extent, "cx", out var width) || !TryNumber(extent, "cy", out var height) ||
                width <= 0 || height <= 0)
                continue;

            var runProperties = shape.Descendants(a + "rPr").FirstOrDefault();
            var endProperties = shape.Descendants(a + "endParaRPr").FirstOrDefault();
            var fontSize = ReadFontSize(runProperties) ?? ReadFontSize(endProperties) ?? 18;
            var bold = string.Equals(runProperties?.Attribute("b")?.Value, "1", StringComparison.Ordinal) ||
                       string.Equals(runProperties?.Attribute("b")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            var fill = ReadColor(shape.Element(p + "spPr")?.Element(a + "solidFill"));
            var textColor = ReadColor(runProperties?.Element(a + "solidFill"));
            elements.Add(new(shapeText, x, y, width, height, fill, textColor, fontSize, bold));
        }

        var relationships = ReadSlideImageRelationships(package, partName);
        foreach (var picture in document.Descendants(p + "pic"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relationshipId = picture.Descendants(a + "blip")
                .Select(node => node.Attribute(XName.Get("embed", OfficeRelationshipNamespace))?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (relationshipId is null || !relationships.TryGetValue(relationshipId, out var imagePart)) continue;

            var transform = picture.Descendants(a + "xfrm").FirstOrDefault();
            var offset = transform?.Element(a + "off");
            var extent = transform?.Element(a + "ext");
            if (!TryNumber(offset, "x", out var x) || !TryNumber(offset, "y", out var y) ||
                !TryNumber(extent, "cx", out var width) || !TryNumber(extent, "cy", out var height) ||
                width <= 0 || height <= 0)
                continue;

            var data = package.ReadRequiredPart(imagePart, options.MaximumPresentationImageBytes);
            var contentType = ResolveImageContentType(imagePart, data);
            if (contentType is null) continue;
            images.Add(new(data.ToImmutableArray(), contentType, x, y, width, height));
        }

        var visual = elements.Count == 0 && images.Count == 0
            ? null
            : new SlideVisualPreview(slideWidth, slideHeight, elements.ToImmutable(), images.ToImmutable());
        return (allText.ToString().TrimEnd(), visual);
    }

    private static Dictionary<string, string> ReadSlideImageRelationships(OpenXmlPackageGuard package, string slidePart)
    {
        var directory = slidePart[..slidePart.LastIndexOf('/')];
        var fileName = slidePart[(slidePart.LastIndexOf('/') + 1)..];
        var relationshipsPart = $"{directory}/_rels/{fileName}.rels";
        using var reader = package.OpenOptionalXml(relationshipsPart);
        if (reader is null) return [];

        var relationships = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship" ||
                reader.NamespaceURI != PackageRelationshipNamespace) continue;
            var id = reader.GetAttribute("Id");
            var type = reader.GetAttribute("Type");
            var target = reader.GetAttribute("Target");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(target) ||
                string.Equals(reader.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase) ||
                type is null || !type.EndsWith(ImageRelationshipSuffix, StringComparison.Ordinal)) continue;
            relationships[id] = ResolvePartTarget(directory, target);
        }
        return relationships;
    }

    private static string ResolvePartTarget(string baseDirectory, string target)
    {
        var normalized = target.Replace('\\', '/');
        if (normalized.Contains('\0') || normalized.Contains(':') || normalized.StartsWith('/'))
            throw new FileRejectedException("PPTX image relationship contains an unsafe target.");
        var segments = new List<string>(baseDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count <= 1) throw new FileRejectedException("PPTX image relationship escapes the package root.");
                segments.RemoveAt(segments.Count - 1);
            }
            else segments.Add(segment);
        }
        return string.Join('/', segments);
    }

    private static string? ResolveImageContentType(string partName, byte[] data)
    {
        var extension = Path.GetExtension(partName).ToLowerInvariant();
        return extension switch
        {
            ".png" when data.AsSpan().StartsWith([0x89, 0x50, 0x4E, 0x47]) => "image/png",
            ".jpg" or ".jpeg" when data.AsSpan().StartsWith([0xFF, 0xD8, 0xFF]) => "image/jpeg",
            ".gif" when data.AsSpan().StartsWith("GIF8"u8) => "image/gif",
            ".bmp" when data.AsSpan().StartsWith("BM"u8) => "image/bmp",
            _ => null
        };
    }

    private static bool TryNumber(XElement? element, string name, out double value) =>
        double.TryParse(element?.Attribute(name)?.Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    private static double? ReadFontSize(XElement? properties) =>
        int.TryParse(properties?.Attribute("sz")?.Value, out var size) ? size / 100d : null;

    private static string? ReadColor(XElement? fill)
    {
        var rgb = fill?.Element(XName.Get("srgbClr", DrawingNamespace))?.Attribute("val")?.Value;
        return rgb is { Length: 6 } && rgb.All(Uri.IsHexDigit) ? "#" + rgb : null;
    }
}
