using System.Collections.Immutable;

namespace ExusiAI.FileViewer.Core;

[Flags]
public enum ViewerCapabilities
{
    None = 0,
    Search = 1,
    IncrementalRead = 2,
    Tabular = 4,
    Edit = 8,
    Save = 16
}

public sealed record ViewerOpenOptions
{
    public static ViewerOpenOptions Default { get; } = new();

    public long MaximumFileBytes { get; init; } = 512L * 1024 * 1024;
    public int TextChunkCharacters { get; init; } = 64 * 1024;
    public int CsvRowsPerPage { get; init; } = 256;
    public int MaximumCsvFieldsPerRow { get; init; } = 16_384;
    public int MaximumCsvFieldCharacters { get; init; } = 1 * 1024 * 1024;

    internal void Validate()
    {
        if (MaximumFileBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumFileBytes));
        if (TextChunkCharacters is < 1024 or > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(TextChunkCharacters));
        if (CsvRowsPerPage is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(CsvRowsPerPage));
        if (MaximumCsvFieldsPerRow is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(MaximumCsvFieldsPerRow));
        if (MaximumCsvFieldCharacters is < 1 or > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(MaximumCsvFieldCharacters));
    }
}

public sealed record ViewerDocumentInfo(
    string FilePath,
    string DisplayName,
    string FormatName,
    long Length,
    ViewerCapabilities Capabilities,
    bool IsReadOnly,
    ImmutableArray<string> Warnings);

public interface IFileViewerProvider
{
    string Id { get; }
    int Priority { get; }
    IReadOnlySet<string> SupportedExtensions { get; }
    ValueTask<ViewerDocument> OpenAsync(string filePath, ViewerOpenOptions options, CancellationToken cancellationToken);
}

public abstract class ViewerDocument(ViewerDocumentInfo info) : IAsyncDisposable
{
    public ViewerDocumentInfo Info { get; } = info;
    public abstract ValueTask DisposeAsync();
}

public sealed record TextChunk(long CharacterOffset, string Text, bool IsFinal);

public sealed record CsvPage(long StartRow, ImmutableArray<ImmutableArray<string>> Rows, bool IsFinal);

public sealed class UnsupportedFileFormatException(string extension)
    : NotSupportedException($"No enabled file viewer provider supports '{extension}'.");

public sealed class FileRejectedException(string message) : InvalidDataException(message);
