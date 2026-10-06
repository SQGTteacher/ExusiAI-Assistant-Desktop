using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using ExusiAI.FileViewer.Core;

namespace ExusiAI.FileViewer.Office;

/// <summary>Optional local Office layout engine. Does not automate Microsoft Office.</summary>
public sealed class OfficeLayoutProvider(string? executablePath = null) : IFileViewerProvider
{
    private static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".ppt", ".doc", ".docx" };
    private readonly string? executable = executablePath ?? FindExecutable();
    public bool IsAvailable => executable is not null && File.Exists(executable);
    public string Id => "exusiai.viewer.office-layout";
    public int Priority => 200;
    public IReadOnlySet<string> SupportedExtensions => Extensions;

    private static string? FindExecutable()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("EXUSIAI_LIBREOFFICE_PATH"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "libreoffice", "program", "soffice.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe"),
            "/usr/bin/libreoffice", "/usr/bin/soffice"
        };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    public async ValueTask<ViewerDocument> OpenAsync(string filePath, ViewerOpenOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable)
            throw new FileRejectedException("PPT/Word 原始分页需要本地 LibreOffice。请安装 LibreOffice，或配置 EXUSIAI_LIBREOFFICE_PATH 为 soffice.exe 的完整路径。");
        var file = new FileInfo(Path.GetFullPath(filePath));
        if (!file.Exists) throw new FileNotFoundException("Document does not exist.", file.FullName);
        if (file.Length > options.MaximumFileBytes) throw new FileRejectedException("Office 文件超过大小预算。");
        var extension = file.Extension.ToLowerInvariant();
        if (!Extensions.Contains(extension)) throw new UnsupportedFileFormatException(extension);
        // Keep the existing ZIP/XML guard before passing DOCX to the external engine.
        if (extension == ".docx")
            await (await new DocxFileViewerProvider().OpenAsync(file.FullName, options, cancellationToken)).DisposeAsync();
        else
        {
            using var headerStream = file.OpenRead();
            var header = new byte[8];
            if (headerStream.Read(header) != 8 || !header.AsSpan().SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }))
                throw new FileRejectedException("DOC/PPT 文件不是有效的 OLE 复合文档。");
        }

        var directory = Path.Combine(Path.GetTempPath(), "ExusiAI-Office", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "document" + extension);
            await CopyBoundedAsync(file.FullName, input, options.MaximumFileBytes, cancellationToken).ConfigureAwait(false);
            var profile = Path.Combine(directory, "profile");
            Directory.CreateDirectory(Path.Combine(profile, "user"));
            // An empty private profile has no trusted macro locations. Never lower macro security.
            await File.WriteAllTextAsync(Path.Combine(profile, "user", "registrymodifications.xcu"), """
                <?xml version="1.0" encoding="UTF-8"?>
                <oor:items xmlns:oor="http://openoffice.org/2001/registry">
                  <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop></item>
                  <item oor:path="/org.openoffice.Office.Writer/Content/Update"><prop oor:name="Link" oor:op="fuse"><value>2</value></prop></item>
                </oor:items>
                """, cancellationToken).ConfigureAwait(false);
            var start = new ProcessStartInfo(executable!)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = directory
            };
            foreach (var argument in new[] { "-env:UserInstallation=" + new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri,
                "--headless", "--nologo", "--nodefault", "--norestore", "--convert-to",
                extension == ".ppt" ? "pdf:impress_pdf_Export" : "pdf:writer_pdf_Export", "--outdir", directory, input })
                start.ArgumentList.Add(argument);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            using var process = Process.Start(start) ?? throw new IOException("无法启动 LibreOffice。");
            var output = DrainAsync(process.StandardOutput);
            var error = DrainAsync(process.StandardError);
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAll(output, error).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw new FileRejectedException("Office 排版转换超过 90 秒，已终止转换进程。");
            }
            await Task.WhenAll(output, error).ConfigureAwait(false);
            var pdf = Path.Combine(directory, "document.pdf");
            if (process.ExitCode != 0 || !File.Exists(pdf))
                throw new FileRejectedException("Office 排版转换失败：" + await error);
            var rendered = await new PdfFileViewerProvider().OpenAsync(pdf, options, cancellationToken);
            if (extension == ".ppt" && ((IPagedPreviewDocument)rendered).PageCount > options.MaximumPresentationSlides)
            {
                await rendered.DisposeAsync().ConfigureAwait(false);
                throw new FileRejectedException("PPT 幻灯片数量超过配置上限。");
            }
            if (cancellationToken.IsCancellationRequested)
            {
                await rendered.DisposeAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            return new OfficeLayoutDocument(file, rendered, directory, extension);
        }
        catch { DeleteDirectory(directory); throw; }
    }

    private static async Task CopyBoundedAsync(string source, string destination, long maximumBytes, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = File.Create(destination);
        var buffer = new byte[64 * 1024];
        long length = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            length = checked(length + count);
            if (length > maximumBytes) throw new FileRejectedException("Office 文件在读取时超过大小预算。");
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
    }

    private static async Task<string> DrainAsync(StreamReader reader)
    {
        var result = new StringBuilder();
        var buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            if (result.Length < 4096) result.Append(buffer, 0, Math.Min(count, 4096 - result.Length));
        return result.ToString().Trim();
    }

    internal static void DeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed class OfficeLayoutDocument : ViewerDocument, IPagedPreviewDocument, IPageTextDocument
{
    private readonly ViewerDocument rendered;
    private readonly string directory;
    public int PageCount => ((IPagedPreviewDocument)rendered).PageCount;
    internal OfficeLayoutDocument(FileInfo original, ViewerDocument rendered, string directory, string extension)
        : base(new(original.FullName, original.Name, extension[1..].ToUpperInvariant() + " 原始分页",
            original.Length, ViewerCapabilities.Pages | ViewerCapabilities.Search, true,
            ImmutableArray.Create("LibreOffice 本地排版，PDFium 分页显示；只读，不修改源文件。",
                "分页、图片、表格和字体由本地排版引擎处理；缺失字体可能替换，PPT 动画和嵌入视频不在静态分页中播放。")))
    { this.rendered = rendered; this.directory = directory; }
    public ValueTask<DocumentPagePreview> ReadPageAsync(int pageNumber, CancellationToken cancellationToken = default)
        => ((IPagedPreviewDocument)rendered).ReadPageAsync(pageNumber, cancellationToken);
    public ValueTask<string> ReadPageTextAsync(int pageNumber, CancellationToken cancellationToken = default)
        => ((IPageTextDocument)rendered).ReadPageTextAsync(pageNumber, cancellationToken);
    public override async ValueTask DisposeAsync()
    {
        try { await rendered.DisposeAsync().ConfigureAwait(false); }
        finally { OfficeLayoutProvider.DeleteDirectory(directory); }
    }
}
