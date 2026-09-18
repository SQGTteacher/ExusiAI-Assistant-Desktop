using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;

namespace ExusiAI.FileViewer.Core;

public sealed class CsvFileViewerProvider : IFileViewerProvider
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csv" };
    public string Id => "exusiai.viewer.csv";
    public int Priority => 100;
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public ValueTask<ViewerDocument> OpenAsync(string filePath, ViewerOpenOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = SafeFileAccess.Inspect(filePath, options);
        ViewerDocument document = new StreamingCsvDocument(info, options);
        return ValueTask.FromResult(document);
    }
}

public sealed class StreamingCsvDocument : ViewerDocument
{
    private readonly FileInfo file;
    private readonly ViewerOpenOptions options;
    private int reading;
    private bool disposed;

    internal StreamingCsvDocument(FileInfo file, ViewerOpenOptions options)
        : base(new(
            file.FullName,
            file.Name,
            "CSV",
            file.Length,
            ViewerCapabilities.Search | ViewerCapabilities.IncrementalRead | ViewerCapabilities.Tabular,
            true,
            ImmutableArray.Create("阶段 1 提供只读、按页加载的 RFC 4180 风格预览；不会修改原文件。")))
    {
        this.file = file;
        this.options = options;
    }

    public async IAsyncEnumerable<CsvPage> ReadPagesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Interlocked.Exchange(ref reading, 1) != 0) throw new InvalidOperationException("This document already has an active reader.");
        try
        {
            await using var stream = SafeFileAccess.OpenSequentialRead(file);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: false);
            var parser = new CsvRecordParser(reader, options.MaximumCsvFieldsPerRow, options.MaximumCsvFieldCharacters);
            var page = ImmutableArray.CreateBuilder<ImmutableArray<string>>(options.CsvRowsPerPage);
            long startRow = 0;
            long row = 0;
            while (await parser.ReadRecordAsync(cancellationToken).ConfigureAwait(false) is { } record)
            {
                page.Add(record);
                row++;
                if (page.Count != options.CsvRowsPerPage) continue;
                yield return new(startRow, page.ToImmutable(), false);
                startRow = row;
                page.Clear();
            }

            yield return new(startRow, page.ToImmutable(), true);
        }
        finally
        {
            Volatile.Write(ref reading, 0);
        }
    }

    public override ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class CsvRecordParser(TextReader reader, int maximumFields, int maximumFieldCharacters)
{
    private readonly char[] singleCharacter = new char[1];
    private int? pending;

    public async ValueTask<ImmutableArray<string>?> ReadRecordAsync(CancellationToken cancellationToken)
    {
        var fields = ImmutableArray.CreateBuilder<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var hasInput = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
            if (value < 0)
            {
                if (inQuotes) throw new FileRejectedException("CSV contains an unterminated quoted field.");
                if (!hasInput && fields.Count == 0 && field.Length == 0) return null;
                AddField(fields, field);
                return fields.ToImmutable();
            }

            hasInput = true;
            var character = (char)value;
            if (inQuotes)
            {
                if (character != '"')
                {
                    Append(field, character);
                    continue;
                }

                var next = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
                if (next == '"')
                {
                    Append(field, '"');
                    continue;
                }

                inQuotes = false;
                pending = next;
                continue;
            }

            switch (character)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    AddField(fields, field);
                    break;
                case '\r':
                    var next = await ReadCharacterAsync(cancellationToken).ConfigureAwait(false);
                    if (next != '\n') pending = next;
                    AddField(fields, field);
                    return fields.ToImmutable();
                case '\n':
                    AddField(fields, field);
                    return fields.ToImmutable();
                default:
                    Append(field, character);
                    break;
            }
        }
    }

    private async ValueTask<int> ReadCharacterAsync(CancellationToken cancellationToken)
    {
        if (pending is { } buffered)
        {
            pending = null;
            return buffered;
        }

        return await reader.ReadAsync(singleCharacter.AsMemory(), cancellationToken).ConfigureAwait(false) == 0 ? -1 : singleCharacter[0];
    }

    private void AddField(ImmutableArray<string>.Builder fields, StringBuilder field)
    {
        if (fields.Count >= maximumFields) throw new FileRejectedException($"CSV row exceeds the {maximumFields:N0}-field safety limit.");
        fields.Add(field.ToString());
        field.Clear();
    }

    private void Append(StringBuilder field, char character)
    {
        if (field.Length >= maximumFieldCharacters)
            throw new FileRejectedException($"CSV field exceeds the {maximumFieldCharacters:N0}-character safety limit.");
        field.Append(character);
    }
}
