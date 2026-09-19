using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;

namespace ExusiAI.FileViewer.Core;

public sealed class TextFileViewerProvider : IFileViewerProvider
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown"
    };

    public string Id => "exusiai.viewer.text";
    public int Priority => 100;
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public ValueTask<ViewerDocument> OpenAsync(string filePath, ViewerOpenOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = SafeFileAccess.Inspect(filePath, options);
        var format = string.Equals(info.Extension, ".txt", StringComparison.OrdinalIgnoreCase) ? "纯文本" : "Markdown 源文本";
        ViewerDocument document = new StreamingTextDocument(info, format, options.TextChunkCharacters);
        return ValueTask.FromResult(document);
    }
}

public sealed class StreamingTextDocument : ViewerDocument, ITextPreviewDocument
{
    private readonly FileInfo file;
    private readonly int chunkCharacters;
    private int reading;
    private bool disposed;

    internal StreamingTextDocument(FileInfo file, string format, int chunkCharacters)
        : base(new(
            file.FullName,
            file.Name,
            format,
            file.Length,
            ViewerCapabilities.Search | ViewerCapabilities.IncrementalRead,
            true,
            ImmutableArray.Create("阶段 1 以只读源文本方式显示；不会执行 Markdown 中的脚本或加载外部内容。")))
    {
        this.file = file;
        this.chunkCharacters = chunkCharacters;
    }

    public async IAsyncEnumerable<TextChunk> ReadChunksAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Interlocked.Exchange(ref reading, 1) != 0) throw new InvalidOperationException("This document already has an active reader.");
        try
        {
            await using var stream = SafeFileAccess.OpenSequentialRead(file);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, bufferSize: chunkCharacters, leaveOpen: false);
            var buffer = new char[chunkCharacters];
            long offset = 0;
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    yield return new(offset, string.Empty, true);
                    yield break;
                }

                var text = new string(buffer, 0, count);
                yield return new(offset, text, false);
                offset += count;
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
        return ValueTask.CompletedTask;
    }
}
