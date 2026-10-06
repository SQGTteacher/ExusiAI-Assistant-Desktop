using System.IO.Compression;
using System.Runtime.InteropServices;
using ExusiAI.FileViewer.Core;
using ExusiAI.FileViewer.Office;
using LibVLCSharp.Shared;

namespace ExusiAI.FileViewer.Integration.Tests;

public sealed class NativeViewerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "exusiai-native-tests-" + Guid.NewGuid().ToString("N"));
    public NativeViewerTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData("sample.ppt")]
    [InlineData("sample.doc")]
    public async Task Legacy_office_files_render_searchable_pages_without_modifying_source(string name)
    {
        var path = await ExtractFixtureAsync(name);
        var original = await File.ReadAllBytesAsync(path);
        var provider = new OfficeLayoutProvider();
        Assert.True(provider.IsAvailable, "Install LibreOffice or set EXUSIAI_LIBREOFFICE_PATH before running integration tests.");
        var document = await provider.OpenAsync(path, ViewerOpenOptions.Default, CancellationToken.None);
        try
        {
            Assert.Equal(path, document.Info.FilePath);
            var pages = Assert.IsAssignableFrom<IPagedPreviewDocument>(document);
            Assert.True(pages.PageCount > 0);
            var page = await pages.ReadPageAsync(1);
            Assert.True(page.Width > 0 && page.Height > 0);
            Assert.Equal(page.Width * page.Height * 4, page.Pixels.Length);
            var text = await ((IPageTextDocument)document).ReadPageTextAsync(1);
            Assert.False(string.IsNullOrWhiteSpace(text));
            var query = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).First();
            var hits = await ViewerSearchService.SearchAsync(document, query);
            Assert.Contains(hits, hit => hit.Kind == ViewerSearchLocationKind.Page && hit.PrimaryIndex == 1);
        }
        finally { await document.DisposeAsync(); }
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        File.Delete(path); // No leaked source handle.
    }

    [Fact]
    public async Task Docx_layout_preserves_body_and_table_text()
    {
        var path = Path.Combine(directory, "layout.docx");
        using (var package = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WritePart(package, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>
                """);
            WritePart(package, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>
                """);
            WritePart(package, "word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:rPr><w:b/><w:sz w:val="36"/></w:rPr><w:t>OfficeLayoutMarker</w:t></w:r></w:p><w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="4000"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w="4000" w:type="dxa"/></w:tcPr><w:p><w:r><w:t>TableCellMarker</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:sectPr><w:pgSz w:w="11906" w:h="16838"/></w:sectPr></w:body></w:document>
                """);
        }
        await using var document = await new OfficeLayoutProvider().OpenAsync(path, ViewerOpenOptions.Default, CancellationToken.None);
        var text = await ((IPageTextDocument)document).ReadPageTextAsync(1);
        Assert.Contains("OfficeLayoutMarker", text);
        Assert.Contains("TableCellMarker", text);
        var page = await ((IPagedPreviewDocument)document).ReadPageAsync(1);
        Assert.True(page.Pixels.Length > 0);
    }

    [Theory]
    [InlineData("video.mp4")]
    [InlineData("video.webm")]
    public async Task Bundled_decoder_produces_video_frames(string fixture)
    {
        var path = await ExtractFixtureAsync(fixture);
        await using var document = await new VideoFileViewerProvider().OpenAsync(path, ViewerOpenOptions.Default, CancellationToken.None);
        LibVLCSharp.Shared.Core.Initialize();
        using var vlc = new LibVLC("--no-audio", "--no-video-title-show", "--avcodec-hw=none");
        var buffer = Marshal.AllocHGlobal(64 * 64 * 4);
        try
        {
            using var player = new MediaPlayer(vlc);
            var decoded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            player.SetVideoFormat("RV32", 64, 64, 256);
            player.SetVideoCallbacks((_, planes) => { Marshal.WriteIntPtr(planes, buffer); return IntPtr.Zero; }, null,
                (_, _) => { if (Interlocked.Increment(ref count) >= 2) decoded.TrySetResult(); });
            player.EncounteredError += (_, _) => decoded.TrySetException(new IOException("LibVLC failed to decode " + fixture));
            using var media = new Media(vlc, path, FromType.FromPath);
            Assert.True(player.Play(media));
            await decoded.Task.WaitAsync(TimeSpan.FromSeconds(15));
            player.Stop();
            Assert.True(count >= 2);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [Fact]
    public async Task Pdf_search_and_render_work_without_office_engine()
    {
        var path = Path.Combine(directory, "pages.pdf");
        var content = "%PDF-1.4\n";
        var offsets = new List<int> { 0 };
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            StreamObject("BT /F1 18 Tf 20 150 Td (First PdfMarker) Tj ET"),
            StreamObject("BT /F1 18 Tf 20 150 Td (Second PdfMarker) Tj ET")
        };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(content.Length);
            content += $"{index + 1} 0 obj\n{objects[index]}\nendobj\n";
        }
        var xref = content.Length;
        content += "xref\n0 8\n0000000000 65535 f \n";
        foreach (var offset in offsets.Skip(1)) content += $"{offset:0000000000} 00000 n \n";
        content += $"trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n";
        await File.WriteAllBytesAsync(path, System.Text.Encoding.ASCII.GetBytes(content));
        await using var document = await new PdfFileViewerProvider().OpenAsync(path, ViewerOpenOptions.Default, CancellationToken.None);
        Assert.Equal(2, ((IPagedPreviewDocument)document).PageCount);
        var hits = await ViewerSearchService.SearchAsync(document, "PdfMarker");
        Assert.Equal(new long[] { 1, 2 }, hits.Select(hit => hit.PrimaryIndex));
        var page = await ((IPagedPreviewDocument)document).ReadPageAsync(2);
        Assert.Contains("Second", page.Text);
        Assert.Equal(page.Width * page.Height * 4, page.Pixels.Length);
    }

    private static string StreamObject(string data) => $"<< /Length {data.Length} >>\nstream\n{data}\nendstream";

    private async Task<string> ExtractFixtureAsync(string name)
    {
        var encoded = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".gz.base64"));
        using var compressed = new MemoryStream(Convert.FromBase64String(encoded));
        using var input = new GZipStream(compressed, CompressionMode.Decompress);
        var path = Path.Combine(directory, name);
        await using var output = File.Create(path);
        await input.CopyToAsync(output);
        return path;
    }
    private static void WritePart(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(text);
    }
    public void Dispose() => Directory.Delete(directory, recursive: true);
}
