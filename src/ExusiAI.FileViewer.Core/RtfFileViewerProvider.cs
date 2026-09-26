using System.Collections.Immutable;

namespace ExusiAI.FileViewer.Core;

public sealed class RtfFileViewerProvider : IFileViewerProvider
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".rtf" };

    public string Id => "exusiai.viewer.rtf";
    public int Priority => 100;
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    public ValueTask<ViewerDocument> OpenAsync(string filePath, ViewerOpenOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = SafeFileAccess.Inspect(filePath, options);
        if (info.Length > options.MaximumRichTextBytes)
            throw new FileRejectedException($"RTF exceeds the {options.MaximumRichTextBytes / 1024 / 1024:N0} MiB rendering limit.");

        ViewerDocument document = new RtfDocument(info, options.MaximumRichTextBytes);
        return ValueTask.FromResult(document);
    }

    private sealed class RtfDocument(FileInfo file, int maximumBytes)
        : ViewerDocument(new(
            file.FullName,
            file.Name,
            "RTF 分页文档",
            file.Length,
            ViewerCapabilities.Search,
            true,
            ImmutableArray.Create("RTF 仅由本地 WPF 文档解析器呈现；不会打开外部链接或执行嵌入对象。"))), IRichTextPreviewDocument
    {
        public async ValueTask<RichTextContent> ReadAsync(CancellationToken cancellationToken = default)
        {
            await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > maximumBytes) throw new FileRejectedException("RTF grew beyond the configured rendering limit while opening.");
            var data = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(data, cancellationToken);
            if (data.Length < 5 || data[0] != (byte)'{' || data[1] != (byte)'\\' || data[2] != (byte)'r' || data[3] != (byte)'t' || data[4] != (byte)'f')
                throw new FileRejectedException("The file does not contain a valid RTF header.");
            return new(data.ToImmutableArray());
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
