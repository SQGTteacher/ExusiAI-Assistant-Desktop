using System.Collections.Immutable;

namespace ExusiAI.FileViewer.Core;

public interface ILocalVideoDocument
{
    string FilePath { get; }
}

public sealed class VideoFileViewerProvider : IFileViewerProvider
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".flv", ".mpg", ".mpeg" };
    public string Id => "exusiai.viewer.video";
    public int Priority => 100;
    public IReadOnlySet<string> SupportedExtensions => Extensions;
    public ValueTask<ViewerDocument> OpenAsync(string filePath, ViewerOpenOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = SafeFileAccess.Inspect(filePath, options);
        using var stream = file.OpenRead();
        Span<byte> header = stackalloc byte[16];
        var length = stream.Read(header);
        var valid = length >= 12 && (header[4..8].SequenceEqual("ftyp"u8) || header[4..8].SequenceEqual("moov"u8) ||
            header[4..8].SequenceEqual("mdat"u8) || header[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }) ||
            (header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("AVI "u8)) ||
            header[..4].SequenceEqual(new byte[] { 0x30, 0x26, 0xB2, 0x75 }) ||
            header[..3].SequenceEqual("FLV"u8) || header[..4].SequenceEqual(new byte[] { 0, 0, 1, 0xBA }));
        if (!valid) throw new FileRejectedException("文件没有可识别的视频容器头；不会作为播放列表或网络地址打开。");
        return ValueTask.FromResult<ViewerDocument>(new LocalVideoDocument(file));
    }
}

internal sealed class LocalVideoDocument(FileInfo file) : ViewerDocument(new(file.FullName, file.Name,
    "本地视频 · LibVLC", file.Length, ViewerCapabilities.None, true,
    ImmutableArray.Create("使用随程序提供的 LibVLC 本地解码；播放失败会显示错误。"))), ILocalVideoDocument
{
    public string FilePath => Info.FilePath;
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
