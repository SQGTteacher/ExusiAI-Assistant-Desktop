using System.Text;
using ExusiAI.FileViewer.Core;

namespace ExusiAI.FileViewer.Core.Tests;

public sealed class FileViewerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"exusiai-viewer-{Guid.NewGuid():N}");

    public FileViewerTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task Text_provider_reads_incrementally_without_edit_capability()
    {
        var path = Path.Combine(directory, "lesson.md");
        await File.WriteAllTextAsync(path, new string('课', 5000), new UTF8Encoding(false));
        var registry = CreateRegistry();

        await using var opened = await registry.OpenAsync(path, new ViewerOpenOptions { TextChunkCharacters = 1024 });
        var document = Assert.IsType<StreamingTextDocument>(opened);
        var chunks = new List<TextChunk>();
        await foreach (var chunk in document.ReadChunksAsync()) chunks.Add(chunk);

        Assert.True(chunks.Count >= 5);
        Assert.True(chunks[^1].IsFinal);
        Assert.True(document.Info.IsReadOnly);
        Assert.False(document.Info.Capabilities.HasFlag(ViewerCapabilities.Edit));
    }

    [Fact]
    public async Task Csv_provider_pages_quoted_multiline_records()
    {
        var path = Path.Combine(directory, "class.csv");
        await File.WriteAllTextAsync(path, "name,note\r\n\"Alice\",\"line 1\r\nline 2\"\r\nBob,\"a,b\"", new UTF8Encoding(false));
        var registry = CreateRegistry();

        await using var opened = await registry.OpenAsync(path, new ViewerOpenOptions { CsvRowsPerPage = 2 });
        var document = Assert.IsType<StreamingCsvDocument>(opened);
        var pages = new List<CsvPage>();
        await foreach (var page in document.ReadPagesAsync()) pages.Add(page);

        Assert.Equal(2, pages.Count);
        Assert.Equal("line 1\r\nline 2", pages[0].Rows[1][1]);
        Assert.Equal("a,b", pages[1].Rows[0][1]);
        Assert.True(pages[1].IsFinal);
    }

    [Fact]
    public async Task Oversized_file_is_rejected_before_opening()
    {
        var path = Path.Combine(directory, "large.txt");
        await File.WriteAllBytesAsync(path, new byte[32]);
        var registry = CreateRegistry();

        await Assert.ThrowsAsync<FileRejectedException>(async () =>
            await registry.OpenAsync(path, new ViewerOpenOptions { MaximumFileBytes = 16 }));
    }

    [Fact]
    public void Office_extension_is_not_misrepresented_as_supported()
    {
        var registry = CreateRegistry();
        Assert.Throws<UnsupportedFileFormatException>(() => registry.Resolve("lesson.pptx"));
    }

    private static FileViewerProviderRegistry CreateRegistry() => new(new IFileViewerProvider[]
    {
        new TextFileViewerProvider(),
        new CsvFileViewerProvider()
    });

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
