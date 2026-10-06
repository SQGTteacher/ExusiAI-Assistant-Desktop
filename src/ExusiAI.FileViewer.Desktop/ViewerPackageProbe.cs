using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ExusiAI.FileViewer.Core;
using LibVLCSharp.Shared;

namespace ExusiAI.FileViewer.Desktop;

// Explicit diagnostic mode: exercises the installed program without opening a window.
internal static class ViewerPackageProbe
{
    public static async Task RunAsync(string fixtures, string report)
    {
        var root = Path.GetFullPath(AppContext.BaseDirectory);
        var assemblies = new[] { typeof(object).Assembly, typeof(System.Windows.Window).Assembly,
            typeof(CodePagesEncodingProvider).Assembly, Assembly.Load("System.Configuration.ConfigurationManager") };
        foreach (var assembly in assemblies)
            Require(Path.GetFullPath(assembly.Location).StartsWith(root, StringComparison.OrdinalIgnoreCase),
                "Runtime loaded outside the package: " + assembly.Location);
        using (var config = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "ExusiAI.FileViewer.Desktop.runtimeconfig.json"))))
        {
            var options = config.RootElement.GetProperty("runtimeOptions");
            Require(options.TryGetProperty("includedFrameworks", out _) && !options.TryGetProperty("framework", out _) &&
                !options.TryGetProperty("frameworks", out _), "The viewer is not self-contained.");
        }
        Require(!Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
            .Any(path => !path.Equals(Path.Combine(root, "ExusiAI.FileViewer.Desktop.exe"), StringComparison.OrdinalIgnoreCase)),
            "Third-party executable found in viewer package.");

        var directory = Path.Combine(Path.GetTempPath(), "exusiai-package-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        try
        {
            var ppt = await ExtractAsync(fixtures, directory, "sample.ppt");
            await using (var document = await new LegacyPptFileViewerProvider().OpenAsync(ppt, ViewerOpenOptions.Default, CancellationToken.None))
            {
                var slides = (ISlidePreviewDocument)document;
                Require(slides.SlideCount > 0, "PPT has no slides.");
                var slide = await slides.ReadSlideAsync(1);
                Require(slide.Visual != null && !string.IsNullOrWhiteSpace(slide.Text), "PPT render failed.");
                Require((await ViewerSearchService.SearchAsync(document, slide.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0])).Length > 0, "PPT search failed.");
                checks.Add("PPT conversion, slide visual and search");
            }
            var doc = await ExtractAsync(fixtures, directory, "sample.doc");
            await using (var document = await new LegacyDocFileViewerProvider().OpenAsync(doc, ViewerOpenOptions.Default, CancellationToken.None))
            {
                await CheckTextAsync(document, "simple");
                checks.Add("DOC text and search");
            }
            var docx = Path.Combine(directory, "sample.docx");
            using (var archive = ZipFile.Open(docx, ZipArchiveMode.Create))
            {
                WritePart(archive, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
                WritePart(archive, "word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>PackageBodyMarker</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>PackageTableMarker</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>");
            }
            await using (var document = await new DocxFileViewerProvider().OpenAsync(docx, ViewerOpenOptions.Default, CancellationToken.None))
            {
                await CheckTextAsync(document, "PackageBodyMarker");
                await CheckTextAsync(document, "PackageTableMarker");
                checks.Add("DOCX paragraphs, table text and search");
            }
            var pdf = Path.Combine(directory, "sample.pdf");
            await WritePdfAsync(pdf);
            await using (var document = await new PdfFileViewerProvider().OpenAsync(pdf, ViewerOpenOptions.Default, CancellationToken.None))
            {
                var page = await ((IPagedPreviewDocument)document).ReadPageAsync(1);
                Require(page.Width > 0 && page.Height > 0 && page.Pixels.Length == page.Width * page.Height * 4, "PDF render failed.");
                Require((await ViewerSearchService.SearchAsync(document, "PackagePdfMarker")).Length > 0, "PDF search failed.");
                checks.Add("PDF native rendering and search");
            }
            LibVLCSharp.Shared.Core.Initialize(Path.Combine(root, "libvlc", "win-x64"));
            foreach (var name in new[] { "video.mp4", "video.webm" })
            {
                var path = await ExtractAsync(fixtures, directory, name);
                await using var document = await new VideoFileViewerProvider().OpenAsync(path, ViewerOpenOptions.Default, CancellationToken.None);
                await DecodeAsync(path);
                checks.Add(name + " decoded at least two frames with packaged LibVLC");
            }
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
            {
                success = true, runtimeAssemblies = assemblies.Select(a => a.Location), checks
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task CheckTextAsync(ViewerDocument document, string marker)
    {
        var text = new StringBuilder();
        await foreach (var chunk in ((ITextPreviewDocument)document).ReadChunksAsync()) text.Append(chunk.Text);
        Require(text.ToString().Contains(marker, StringComparison.OrdinalIgnoreCase), "Missing text: " + marker);
        Require((await ViewerSearchService.SearchAsync(document, marker)).Length > 0, "Search failed: " + marker);
    }

    private static async Task DecodeAsync(string path)
    {
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
            player.EncounteredError += (_, _) => decoded.TrySetException(new IOException("Video decoding failed: " + path));
            using var media = new Media(vlc, path, FromType.FromPath);
            Require(player.Play(media), "Video playback failed to start.");
            try { await decoded.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
            finally { player.Stop(); }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static async Task<string> ExtractAsync(string fixtures, string directory, string name)
    {
        var source = Path.Combine(fixtures, name + ".gz.base64");
        Require(new FileInfo(source).Length <= 2 * 1024 * 1024, "Fixture exceeds encoded size limit.");
        using var compressed = new MemoryStream(Convert.FromBase64String(await File.ReadAllTextAsync(source)));
        using var input = new GZipStream(compressed, CompressionMode.Decompress);
        var path = Path.Combine(directory, name);
        await using var output = File.Create(path);
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            Require(output.Length + read <= 16 * 1024 * 1024, "Fixture exceeds expanded size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read));
        }
        return path;
    }

    private static void WritePart(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(text);
    }

    private static async Task WritePdfAsync(string path)
    {
        const string data = "BT /F1 18 Tf 20 150 Td (PackagePdfMarker) Tj ET";
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", $"<< /Length {data.Length} >>\nstream\n{data}\nendstream" };
        var content = "%PDF-1.4\n";
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(content.Length);
            content += $"{index + 1} 0 obj\n{objects[index]}\nendobj\n";
        }
        var xref = content.Length;
        content += "xref\n0 6\n0000000000 65535 f \n";
        foreach (var offset in offsets) content += offset.ToString("0000000000", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n";
        content += $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n";
        await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes(content));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
