using System.Collections.Immutable;

namespace ExusiAI.FileViewer.Core;

public enum ViewerSearchLocationKind
{
    Text,
    Row,
    Slide
}

public sealed record ViewerSearchHit(
    ViewerSearchLocationKind Kind,
    long PrimaryIndex,
    int SecondaryIndex,
    string Snippet);

public static class ViewerSearchService
{
    public static async Task<ImmutableArray<ViewerSearchHit>> SearchAsync(
        ViewerDocument document,
        string query,
        int maximumResults = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (query.Length > 512) throw new ArgumentOutOfRangeException(nameof(query));
        if (maximumResults is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(maximumResults));

        return document switch
        {
            ITextPreviewDocument text => await SearchTextAsync(text, query, maximumResults, cancellationToken).ConfigureAwait(false),
            ITabularPreviewDocument table => await SearchTableAsync(table, query, maximumResults, cancellationToken).ConfigureAwait(false),
            ISlidePreviewDocument slides => await SearchSlidesAsync(slides, query, maximumResults, cancellationToken).ConfigureAwait(false),
            _ => ImmutableArray<ViewerSearchHit>.Empty
        };
    }

    private static async Task<ImmutableArray<ViewerSearchHit>> SearchTextAsync(
        ITextPreviewDocument document,
        string query,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        var hits = ImmutableArray.CreateBuilder<ViewerSearchHit>();
        var tail = string.Empty;
        var overlap = Math.Max(0, query.Length - 1);

        await foreach (var chunk in document.ReadChunksAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk.IsFinal) break;

            var combined = tail + chunk.Text;
            var combinedOffset = Math.Max(0, chunk.CharacterOffset - tail.Length);
            var searchFrom = 0;
            while (searchFrom <= combined.Length - query.Length)
            {
                var index = combined.IndexOf(query, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;

                if (index + query.Length > tail.Length || chunk.CharacterOffset == 0)
                {
                    hits.Add(new(
                        ViewerSearchLocationKind.Text,
                        combinedOffset + index,
                        0,
                        CreateSnippet(combined, index, query.Length)));
                    if (hits.Count >= maximumResults) return hits.ToImmutable();
                }

                searchFrom = index + Math.Max(1, query.Length);
            }

            tail = overlap == 0
                ? string.Empty
                : combined[^Math.Min(overlap, combined.Length)..];
        }

        return hits.ToImmutable();
    }

    private static async Task<ImmutableArray<ViewerSearchHit>> SearchTableAsync(
        ITabularPreviewDocument document,
        string query,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        var hits = ImmutableArray.CreateBuilder<ViewerSearchHit>();
        await foreach (var page in document.ReadPagesAsync(cancellationToken).ConfigureAwait(false))
        {
            for (var rowIndex = 0; rowIndex < page.Rows.Length; rowIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = page.Rows[rowIndex];
                for (var column = 0; column < row.Length; column++)
                {
                    var value = row[column];
                    var match = value.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (match < 0) continue;

                    hits.Add(new(
                        ViewerSearchLocationKind.Row,
                        page.StartRow + rowIndex + 1,
                        column + 1,
                        CreateSnippet(value, match, query.Length)));
                    if (hits.Count >= maximumResults) return hits.ToImmutable();
                }
            }
        }

        return hits.ToImmutable();
    }

    private static async Task<ImmutableArray<ViewerSearchHit>> SearchSlidesAsync(
        ISlidePreviewDocument document,
        string query,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        var hits = ImmutableArray.CreateBuilder<ViewerSearchHit>();
        for (var slideNumber = 1; slideNumber <= document.SlideCount; slideNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slide = await document.ReadSlideAsync(slideNumber, cancellationToken).ConfigureAwait(false);
            var match = slide.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (match < 0) continue;

            hits.Add(new(
                ViewerSearchLocationKind.Slide,
                slideNumber,
                0,
                CreateSnippet(slide.Text, match, query.Length)));
            if (hits.Count >= maximumResults) break;
        }

        return hits.ToImmutable();
    }

    private static string CreateSnippet(string text, int matchIndex, int matchLength)
    {
        const int context = 28;
        var start = Math.Max(0, matchIndex - context);
        var end = Math.Min(text.Length, matchIndex + matchLength + context);
        var snippet = text[start..end]
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
        if (start > 0) snippet = "…" + snippet;
        if (end < text.Length) snippet += "…";
        return snippet;
    }
}
