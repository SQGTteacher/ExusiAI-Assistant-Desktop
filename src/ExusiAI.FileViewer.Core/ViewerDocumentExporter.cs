using System.Text;

namespace ExusiAI.FileViewer.Core;

public static class ViewerDocumentExporter
{
    public static async Task ExportAsync(
        ViewerDocument document,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException($"The destination directory does not exist: '{directory}'.");
        if (string.Equals(fullPath, document.Info.FilePath, StringComparison.OrdinalIgnoreCase))
            throw new FileRejectedException("Export destination must be different from the source document.");

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024, leaveOpen: true))
            {
                switch (document)
                {
                    case ITextPreviewDocument text:
                        await ExportTextAsync(text, writer, cancellationToken).ConfigureAwait(false);
                        break;
                    case ITabularPreviewDocument table:
                        await ExportTableAsync(table, writer, cancellationToken).ConfigureAwait(false);
                        break;
                    case ISlidePreviewDocument slides:
                        await ExportSlidesAsync(slides, writer, cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        throw new NotSupportedException("This document does not expose exportable content.");
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullPath))
                File.Replace(temporaryPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task ExportTextAsync(
        ITextPreviewDocument document,
        TextWriter writer,
        CancellationToken cancellationToken)
    {
        await foreach (var chunk in document.ReadChunksAsync(cancellationToken).ConfigureAwait(false))
        {
            if (chunk.IsFinal) break;
            await writer.WriteAsync(chunk.Text.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ExportTableAsync(
        ITabularPreviewDocument document,
        TextWriter writer,
        CancellationToken cancellationToken)
    {
        await foreach (var page in document.ReadPagesAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var row in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(string.Join(',', row.Select(EscapeCsv))).ConfigureAwait(false);
            }
        }
    }

    private static async Task ExportSlidesAsync(
        ISlidePreviewDocument document,
        TextWriter writer,
        CancellationToken cancellationToken)
    {
        for (var slideNumber = 1; slideNumber <= document.SlideCount; slideNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slide = await document.ReadSlideAsync(slideNumber, cancellationToken).ConfigureAwait(false);
            if (slideNumber > 1) await writer.WriteLineAsync().ConfigureAwait(false);
            await writer.WriteLineAsync($"## 幻灯片 {slide.SlideNumber}").ConfigureAwait(false);
            await writer.WriteLineAsync(slide.Text).ConfigureAwait(false);
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
