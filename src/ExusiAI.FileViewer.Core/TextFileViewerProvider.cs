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

public sealed class StreamingTextDocument : ViewerDocument, IEditableTextDocument
{
    private static readonly IReadOnlySet<string> EditableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown"
    };

    private readonly FileInfo file;
    private readonly int chunkCharacters;
    private readonly TextEncodingProfile encodingProfile;
    private int reading;
    private bool disposed;

    internal StreamingTextDocument(FileInfo file, string format, int chunkCharacters)
        : base(new(
            file.FullName,
            file.Name,
            format,
            file.Length,
            ViewerCapabilities.Search | ViewerCapabilities.IncrementalRead | ViewerCapabilities.Edit | ViewerCapabilities.Save,
            false,
            ImmutableArray.Create(
                "TXT/Markdown 支持基础文本编辑与保存；保存采用同目录临时文件和原子替换，不执行 Markdown 脚本或加载外部内容。")))
    {
        this.file = file;
        this.chunkCharacters = chunkCharacters;
        encodingProfile = DetectEncoding(file);
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

    public async ValueTask SaveTextAsync(
        string text,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullPath = Path.GetFullPath(destinationPath);
        var extension = Path.GetExtension(fullPath);
        if (!EditableExtensions.Contains(extension))
            throw new FileRejectedException("Text editing can only save .txt, .md, or .markdown files.");

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException($"The destination directory does not exist: '{directory}'.");

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                if (encodingProfile.Preamble.Length > 0)
                    await stream.WriteAsync(encodingProfile.Preamble, cancellationToken).ConfigureAwait(false);

                using (var writer = new StreamWriter(
                    stream,
                    encodingProfile.Encoding,
                    bufferSize: 64 * 1024,
                    leaveOpen: true))
                {
                    await writer.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullPath))
                File.Replace(tempPath, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, fullPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static TextEncodingProfile DetectEncoding(FileInfo file)
    {
        Span<byte> prefix = stackalloc byte[4];
        using var stream = SafeFileAccess.OpenSequentialRead(file);
        var count = stream.Read(prefix);

        if (count >= 4 && prefix[0] == 0x00 && prefix[1] == 0x00 && prefix[2] == 0xFE && prefix[3] == 0xFF)
            return new(new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true), [0x00, 0x00, 0xFE, 0xFF]);
        if (count >= 4 && prefix[0] == 0xFF && prefix[1] == 0xFE && prefix[2] == 0x00 && prefix[3] == 0x00)
            return new(new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true), [0xFF, 0xFE, 0x00, 0x00]);
        if (count >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
            return new(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), [0xEF, 0xBB, 0xBF]);
        if (count >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF)
            return new(new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), [0xFE, 0xFF]);
        if (count >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE)
            return new(new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), [0xFF, 0xFE]);

        return new(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), []);
    }

    public override ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }

    private sealed record TextEncodingProfile(Encoding Encoding, byte[] Preamble);
}
