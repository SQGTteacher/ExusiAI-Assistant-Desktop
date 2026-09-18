using System.Text;
using System.IO.Compression;
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

    [Fact]
    public async Task Docx_provider_streams_body_text_without_rendering_external_content()
    {
        var path = Path.Combine(directory, "lesson.docx");
        CreateDocx(path, "第一段", "第二段");
        var registry = CreateRegistry();

        await using var opened = await registry.OpenAsync(path);
        var document = Assert.IsAssignableFrom<ITextPreviewDocument>(opened);
        var text = new StringBuilder();
        await foreach (var chunk in document.ReadChunksAsync()) text.Append(chunk.Text);

        Assert.Contains("第一段", text.ToString());
        Assert.Contains("第二段", text.ToString());
        Assert.True(opened.Info.IsReadOnly);
        Assert.Contains(opened.Info.Warnings, warning => warning.Contains("不承诺", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Docx_provider_rejects_excessive_compression_ratio()
    {
        var path = Path.Combine(directory, "compressed.docx");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("word/document.xml", CompressionLevel.SmallestSize);
            await using var stream = entry.Open();
            await stream.WriteAsync(new byte[1024 * 1024]);
        }

        var registry = CreateRegistry();
        await Assert.ThrowsAsync<FileRejectedException>(async () =>
            await registry.OpenAsync(path, new ViewerOpenOptions { MaximumArchiveCompressionRatio = 10 }));
    }

    private static FileViewerProviderRegistry CreateRegistry() => new(new IFileViewerProvider[]
    {
        new TextFileViewerProvider(),
        new CsvFileViewerProvider(),
        new DocxFileViewerProvider()
    });

    private static void CreateDocx(string path, params string[] paragraphs)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("word/document.xml", CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write("<?xml version=\"1.0\" encoding=\"utf-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
        foreach (var paragraph in paragraphs)
            writer.Write($"<w:p><w:r><w:t>{System.Security.SecurityElement.Escape(paragraph)}</w:t></w:r></w:p>");
        writer.Write("</w:body></w:document>");
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
