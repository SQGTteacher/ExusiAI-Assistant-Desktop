using System.Text;
using System.IO.Compression;
using ExusiAI.FileViewer.Core;

namespace ExusiAI.FileViewer.Core.Tests;

public sealed class FileViewerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"exusiai-viewer-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("", 0, 0, 0)]
    [InlineData("hello", 1, 1, 5)]
    [InlineData("hello world\n第二行", 2, 3, 15)]
    [InlineData("one\r\ntwo\n", 3, 2, 9)]
    public void TextStatisticsCountLinesWordsAndCharacters(
        string text,
        int expectedLines,
        int expectedWords,
        int expectedCharacters)
    {
        var result = TextDocumentStatistics.Calculate(text);

        Assert.Equal(expectedLines, result.Lines);
        Assert.Equal(expectedWords, result.Words);
        Assert.Equal(expectedCharacters, result.Characters);
    }

    [Fact]
    public void TextStatisticsLocateLogicalLineIndependentOfVisualWrapping()
    {
        const string text = "first line\nsecond line\nthird";

        Assert.Equal(new TextDocumentPosition(2, 4), TextDocumentStatistics.Locate(text, 14));
        Assert.Equal(23, TextDocumentStatistics.GetLineStart(text, 3));
        Assert.Equal(text.Length, TextDocumentStatistics.GetLineStart(text, 99));
    }

    [Fact]
    public void Markdown_parser_builds_safe_structured_blocks_and_outline_lines()
    {
        const string markdown = """
            # Lesson title

            Intro with **bold** and `code`.

            - first
            2. second
            > remember this

            <script>alert('display only')</script>

            ```csharp
            Console.WriteLine("safe text");
            ```
            """;

        var result = SafeMarkdownParser.Parse(markdown);

        Assert.False(result.IsTruncated);
        Assert.Contains(result.Blocks, block => block.Kind == MarkdownBlockKind.Heading && block.Level == 1 && block.Text == "Lesson title");
        Assert.Contains(result.Blocks, block => block.Kind == MarkdownBlockKind.UnorderedListItem && block.Text == "first");
        Assert.Contains(result.Blocks, block => block.Kind == MarkdownBlockKind.OrderedListItem && block.Level == 2);
        Assert.Contains(result.Blocks, block => block.Kind == MarkdownBlockKind.Quote && block.Text == "remember this");
        Assert.Contains(result.Blocks, block => block.Kind == MarkdownBlockKind.Paragraph && block.Text.Contains("<script>", StringComparison.Ordinal));
        Assert.Contains(result.Blocks, block => block.Kind == MarkdownBlockKind.Code && block.Text.Contains("Console.WriteLine", StringComparison.Ordinal));
    }

    [Fact]
    public void Markdown_parser_enforces_budgets_and_parses_safe_inline_emphasis()
    {
        var limited = SafeMarkdownParser.Parse("# one\n\n# two\n\n# three", maximumCharacters: 14, maximumBlocks: 2);
        var inlines = SafeMarkdownParser.ParseInlines("plain **bold** *italic* `code`");

        Assert.True(limited.IsTruncated);
        Assert.Equal(2, limited.Blocks.Length);
        Assert.Contains(inlines, item => item.Kind == MarkdownInlineKind.Bold && item.Text == "bold");
        Assert.Contains(inlines, item => item.Kind == MarkdownInlineKind.Italic && item.Text == "italic");
        Assert.Contains(inlines, item => item.Kind == MarkdownInlineKind.Code && item.Text == "code");
    }

    public FileViewerTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task Text_provider_reads_incrementally_and_exposes_safe_edit_capability()
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
        Assert.False(document.Info.IsReadOnly);
        Assert.True(document.Info.Capabilities.HasFlag(ViewerCapabilities.Edit));
        Assert.True(document.Info.Capabilities.HasFlag(ViewerCapabilities.Save));
        Assert.IsAssignableFrom<IEditableTextDocument>(document);
    }

    [Fact]
    public async Task Text_editor_atomically_saves_and_preserves_utf8_bom()
    {
        var path = Path.Combine(directory, "lesson.txt");
        var original = Encoding.UTF8.GetBytes("旧内容");
        await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, .. original]);

        await using var opened = await CreateRegistry().OpenAsync(path);
        var editable = Assert.IsAssignableFrom<IEditableTextDocument>(opened);
        await editable.SaveTextAsync("新内容\r\n第二行", path);

        var saved = await File.ReadAllBytesAsync(path);
        Assert.True(saved.Length >= 3);
        Assert.Equal((byte)0xEF, saved[0]);
        Assert.Equal((byte)0xBB, saved[1]);
        Assert.Equal((byte)0xBF, saved[2]);
        Assert.Equal("新内容\r\n第二行", Encoding.UTF8.GetString(saved, 3, saved.Length - 3));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Exporter_streams_csv_with_standard_escaping()
    {
        var source = Path.Combine(directory, "marks.csv");
        var destination = Path.Combine(directory, "export.csv");
        await File.WriteAllTextAsync(source, "name,note\r\nAlice,\"good, steady\"\r\nBob,\"said \"\"hi\"\"\"");

        await using var document = await CreateRegistry().OpenAsync(source);
        await ViewerDocumentExporter.ExportAsync(document, destination);

        Assert.Equal(
            "name,note" + Environment.NewLine +
            "Alice,\"good, steady\"" + Environment.NewLine +
            "Bob,\"said \"\"hi\"\"\"" + Environment.NewLine,
            await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task Exporter_writes_all_slides_as_text_without_modifying_source()
    {
        var source = Path.Combine(directory, "lesson.pptx");
        var destination = Path.Combine(directory, "lesson.txt");
        CreatePptx(source);
        var sourceBytes = await File.ReadAllBytesAsync(source);

        await using var document = await CreateRegistry().OpenAsync(source);
        await ViewerDocumentExporter.ExportAsync(document, destination);

        var exported = await File.ReadAllTextAsync(destination);
        Assert.Contains("## 幻灯片 1", exported);
        Assert.Contains("课堂标题", exported);
        Assert.Contains("## 幻灯片 2", exported);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task Csv_provider_pages_quoted_multiline_records()
    {
        var path = Path.Combine(directory, "class.csv");
        await File.WriteAllTextAsync(path, "name,note\r\n\"Alice\",\"line 1\r\nline 2\"\r\nBob,\"a,b\"", new UTF8Encoding(false));
        var registry = CreateRegistry();

        await using var opened = await registry.OpenAsync(path, new ViewerOpenOptions { CsvRowsPerPage = 2 });
        var document = Assert.IsType<StreamingCsvDocument>(opened);
        var pages = new List<TabularPage>();
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
    public void Legacy_office_extension_is_not_misrepresented_as_supported()
    {
        var registry = CreateRegistry();
        Assert.Throws<UnsupportedFileFormatException>(() => registry.Resolve("lesson.ppt"));
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
    public async Task Docx_provider_preserves_headings_lists_and_table_rows()
    {
        var path = Path.Combine(directory, "structured.docx");
        CreateStructuredDocx(path);

        await using var opened = await CreateRegistry().OpenAsync(path);
        var document = Assert.IsAssignableFrom<ITextPreviewDocument>(opened);
        var text = new StringBuilder();
        await foreach (var chunk in document.ReadChunksAsync()) text.Append(chunk.Text);

        Assert.Contains("# 课堂计划", text.ToString());
        Assert.Contains("## 教学目标", text.ToString());
        Assert.Contains("• 复习旧知识", text.ToString());
        Assert.Contains("| 姓名 | 分数 |", text.ToString());
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

    [Fact]
    public async Task Xlsx_provider_pages_first_worksheet_and_resolves_shared_strings()
    {
        var path = Path.Combine(directory, "lesson.xlsx");
        CreateXlsx(path);
        var registry = CreateRegistry();

        await using var opened = await registry.OpenAsync(path, new ViewerOpenOptions { SpreadsheetRowsPerPage = 1 });
        var document = Assert.IsAssignableFrom<ITabularPreviewDocument>(opened);
        var pages = new List<TabularPage>();
        await foreach (var page in document.ReadPagesAsync()) pages.Add(page);

        Assert.Equal("XLSX · 2 个工作表", opened.Info.FormatName);
        Assert.Equal(3, pages.Count);
        Assert.Equal("姓名", pages[0].Rows[0][0]);
        Assert.Equal("分数", pages[0].Rows[0][1]);
        Assert.Equal("Alice", pages[1].Rows[0][0]);
        Assert.Equal("95", pages[1].Rows[0][1]);
        Assert.True(pages[^1].IsFinal);
        Assert.True(opened.Info.IsReadOnly);
    }

    [Fact]
    public async Task Xlsx_provider_switches_worksheets_without_loading_entire_workbook()
    {
        var path = Path.Combine(directory, "multi-sheet.xlsx");
        CreateXlsx(path);

        await using var opened = await CreateRegistry().OpenAsync(path, new ViewerOpenOptions { SpreadsheetRowsPerPage = 8 });
        var workbook = Assert.IsAssignableFrom<IWorkbookPreviewDocument>(opened);
        Assert.Equal(new[] { "成绩", "备注" }, workbook.WorksheetNames);

        workbook.SelectWorksheet(1);
        var pages = new List<TabularPage>();
        await foreach (var page in workbook.ReadPagesAsync()) pages.Add(page);

        Assert.Equal(1, workbook.ActiveWorksheetIndex);
        Assert.Equal("课堂表现良好", pages[0].Rows[0][0]);
        Assert.True(pages[^1].IsFinal);
    }

    [Fact]
    public async Task Xlsx_provider_rejects_external_worksheet_relationship()
    {
        var path = Path.Combine(directory, "external.xlsx");
        CreateXlsx(path, externalWorksheet: true);
        var registry = CreateRegistry();

        await Assert.ThrowsAsync<FileRejectedException>(async () => await registry.OpenAsync(path));
    }

    [Fact]
    public async Task Pptx_provider_preserves_slide_order_and_extracts_text()
    {
        var path = Path.Combine(directory, "lesson.pptx");
        CreatePptx(path);
        var registry = CreateRegistry();

        await using var opened = await registry.OpenAsync(path);
        var document = Assert.IsAssignableFrom<ISlidePreviewDocument>(opened);
        var slides = new List<SlidePreview>();
        await foreach (var slide in document.ReadSlidesAsync()) slides.Add(slide);

        Assert.Equal(2, document.SlideCount);
        Assert.Equal(2, slides.Count);
        Assert.Equal(1, slides[0].SlideNumber);
        Assert.Contains("课堂标题", slides[0].Text);
        Assert.Contains("第一点", slides[0].Text);
        var visual = Assert.IsType<SlideVisualPreview>(slides[0].Visual);
        Assert.Equal(3, visual.Elements.Length);
        Assert.Contains("课堂标题", visual.Elements[0].Text);
        Assert.Equal(SlideShapeKind.Rectangle, visual.Elements[0].ShapeKind);
        Assert.Equal(0, visual.Elements[0].ZIndex);
        Assert.Equal(SlideShapeKind.Ellipse, visual.Elements[1].ShapeKind);
        Assert.Equal("#5B9BD5", visual.Elements[1].FillColor);
        Assert.Equal("#2F5597", visual.Elements[1].StrokeColor);
        Assert.Equal(2, visual.Elements[1].ZIndex);
        Assert.Equal(SlideShapeKind.Line, visual.Elements[2].ShapeKind);
        Assert.Equal(15, visual.Elements[2].Rotation);
        var image = Assert.Single(visual.Images);
        Assert.Equal("image/png", image.ContentType);
        Assert.NotEmpty(image.Data);
        Assert.Equal(1, image.ZIndex);
        Assert.Equal(2, slides[1].SlideNumber);
        Assert.Contains("第二页", slides[1].Text);
        Assert.True(slides[1].IsFinal);
        Assert.True(opened.Info.IsReadOnly);
    }

    [Fact]
    public async Task Pptx_provider_supports_random_slide_access()
    {
        var path = Path.Combine(directory, "jump.pptx");
        CreatePptx(path);
        var registry = CreateRegistry();

        await using var opened = await registry.OpenAsync(path, new ViewerOpenOptions { MaximumCachedSlides = 1 });
        var document = Assert.IsAssignableFrom<ISlidePreviewDocument>(opened);

        var second = await document.ReadSlideAsync(2);
        var first = await document.ReadSlideAsync(1);
        var secondAgain = await document.ReadSlideAsync(2);

        Assert.Equal(2, second.SlideNumber);
        Assert.Contains("第二页", second.Text);
        Assert.Equal(1, first.SlideNumber);
        Assert.Contains("课堂标题", first.Text);
        Assert.Equal(second.Text, secondAgain.Text);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await document.ReadSlideAsync(3));
    }

    [Fact]
    public async Task Search_finds_text_across_chunk_boundary()
    {
        var path = Path.Combine(directory, "boundary.txt");
        await File.WriteAllTextAsync(path, new string('a', 1023) + "课堂" + new string('b', 32), new UTF8Encoding(false));
        var registry = CreateRegistry();

        await using var opened = await registry.OpenAsync(path, new ViewerOpenOptions { TextChunkCharacters = 1024 });
        var hits = await ViewerSearchService.SearchAsync(opened, "课堂");

        var hit = Assert.Single(hits);
        Assert.Equal(ViewerSearchLocationKind.Text, hit.Kind);
        Assert.Equal(1023, hit.PrimaryIndex);
    }

    [Fact]
    public async Task Search_finds_spreadsheet_cell_and_pptx_slide()
    {
        var xlsxPath = Path.Combine(directory, "search.xlsx");
        CreateXlsx(xlsxPath);
        var pptxPath = Path.Combine(directory, "search.pptx");
        CreatePptx(pptxPath);
        var registry = CreateRegistry();

        await using var xlsx = await registry.OpenAsync(xlsxPath);
        var tableHits = await ViewerSearchService.SearchAsync(xlsx, "Alice");
        var tableHit = Assert.Single(tableHits);
        Assert.Equal(ViewerSearchLocationKind.Row, tableHit.Kind);
        Assert.Equal(2, tableHit.PrimaryIndex);
        Assert.Equal(1, tableHit.SecondaryIndex);

        await using var pptx = await registry.OpenAsync(pptxPath);
        var slideHits = await ViewerSearchService.SearchAsync(pptx, "第二页");
        var slideHit = Assert.Single(slideHits);
        Assert.Equal(ViewerSearchLocationKind.Slide, slideHit.Kind);
        Assert.Equal(2, slideHit.PrimaryIndex);
    }

    [Fact]
    public async Task Pptx_provider_rejects_external_slide_relationship()
    {
        var path = Path.Combine(directory, "external.pptx");
        CreatePptx(path, externalSlide: true);
        var registry = CreateRegistry();

        await Assert.ThrowsAsync<FileRejectedException>(async () => await registry.OpenAsync(path));
    }

    private static FileViewerProviderRegistry CreateRegistry() => new(new IFileViewerProvider[]
    {
        new TextFileViewerProvider(),
        new CsvFileViewerProvider(),
        new DocxFileViewerProvider(),
        new XlsxFileViewerProvider(),
        new PptxFileViewerProvider()
    });

    private static void CreatePptx(string path, bool externalSlide = false)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "ppt/presentation.xml",
            "<?xml version=\"1.0\"?><p:presentation xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><p:sldIdLst><p:sldId id=\"256\" r:id=\"rId1\"/><p:sldId id=\"257\" r:id=\"rId2\"/></p:sldIdLst><p:sldSz cx=\"12192000\" cy=\"6858000\"/></p:presentation>");
        WriteEntry(archive, "ppt/_rels/presentation.xml.rels",
            "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"" +
            (externalSlide ? "https://example.invalid/slide1.xml\" TargetMode=\"External" : "slides/slide1.xml") +
            "\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide2.xml\"/></Relationships>");
        WriteEntry(archive, "ppt/slides/slide1.xml",
            "<?xml version=\"1.0\"?><p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><p:cSld><p:spTree><p:sp><p:spPr><a:xfrm><a:off x=\"914400\" y=\"685800\"/><a:ext cx=\"5486400\" cy=\"1828800\"/></a:xfrm><a:solidFill><a:srgbClr val=\"FFF2CC\"/></a:solidFill></p:spPr><p:txBody><a:p><a:r><a:rPr sz=\"2400\" b=\"1\"><a:solidFill><a:srgbClr val=\"1F1F1F\"/></a:solidFill></a:rPr><a:t>课堂标题</a:t></a:r></a:p><a:p><a:r><a:t>第一点</a:t></a:r></a:p></p:txBody></p:sp><p:pic><p:blipFill><a:blip r:embed=\"rIdImage1\"/></p:blipFill><p:spPr><a:xfrm><a:off x=\"7315200\" y=\"914400\"/><a:ext cx=\"3657600\" cy=\"2743200\"/></a:xfrm></p:spPr></p:pic><p:sp><p:spPr><a:xfrm><a:off x=\"1000000\" y=\"3000000\"/><a:ext cx=\"1000000\" cy=\"1000000\"/></a:xfrm><a:prstGeom prst=\"ellipse\"/><a:solidFill><a:srgbClr val=\"5B9BD5\"/></a:solidFill><a:ln w=\"25400\"><a:solidFill><a:srgbClr val=\"2F5597\"/></a:solidFill></a:ln></p:spPr></p:sp><p:sp><p:spPr><a:xfrm rot=\"900000\"><a:off x=\"2500000\" y=\"3500000\"/><a:ext cx=\"2000000\" cy=\"10000\"/></a:xfrm><a:prstGeom prst=\"line\"/><a:ln w=\"12700\"><a:solidFill><a:srgbClr val=\"C00000\"/></a:solidFill></a:ln></p:spPr></p:sp></p:spTree></p:cSld></p:sld>");
        WriteEntry(archive, "ppt/slides/_rels/slide1.xml.rels",
            "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdImage1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image1.png\"/></Relationships>");
        WriteBinaryEntry(archive, "ppt/media/image1.png", Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        WriteEntry(archive, "ppt/slides/slide2.xml",
            "<?xml version=\"1.0\"?><p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>第二页</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>");
    }

    private static void CreateXlsx(string path, bool externalWorksheet = false)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteEntry(archive, "xl/workbook.xml",
            "<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"成绩\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"备注\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");

        WriteEntry(archive, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"" +
            (externalWorksheet ? "https://example.invalid/sheet.xml\" TargetMode=\"External" : "worksheets/sheet1.xml") +
            "\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>");

        WriteEntry(archive, "xl/sharedStrings.xml",
            "<?xml version=\"1.0\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>姓名</t></si><si><t>分数</t></si><si><r><t>Ali</t></r><r><t>ce</t></r></si></sst>");

        WriteEntry(archive, "xl/worksheets/sheet1.xml",
            "<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
            "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c></row>" +
            "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>2</v></c><c r=\"B2\"><v>95</v></c></row>" +
            "</sheetData></worksheet>");
        WriteEntry(archive, "xl/worksheets/sheet2.xml",
            "<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
            "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>课堂表现良好</t></is></c></row>" +
            "</sheetData></worksheet>");
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteBinaryEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(content);
    }

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

    private static void CreateStructuredDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "word/document.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
              <w:p><w:pPr><w:pStyle w:val="Title"/></w:pPr><w:r><w:t>课堂计划</w:t></w:r></w:p>
              <w:p><w:pPr><w:pStyle w:val="Heading2"/></w:pPr><w:r><w:t>教学目标</w:t></w:r></w:p>
              <w:p><w:pPr><w:numPr><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>复习旧知识</w:t></w:r></w:p>
              <w:tbl><w:tr><w:tc><w:p><w:r><w:t>姓名</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>分数</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            </w:body></w:document>
            """);
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
