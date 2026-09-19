using System.IO.Compression;
using System.Xml;

namespace ExusiAI.FileViewer.Core;

internal sealed class OpenXmlPackageGuard : IDisposable
{
    private readonly FileStream stream;
    private readonly ZipArchive archive;
    private readonly ViewerOpenOptions options;
    private bool disposed;

    private OpenXmlPackageGuard(FileStream stream, ZipArchive archive, ViewerOpenOptions options)
    {
        this.stream = stream;
        this.archive = archive;
        this.options = options;
    }

    public static OpenXmlPackageGuard Open(FileInfo file, ViewerOpenOptions options)
    {
        var stream = SafeFileAccess.OpenSequentialRead(file);
        try
        {
            var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            ValidateArchive(archive, options);
            return new(stream, archive, options);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public XmlReader OpenRequiredXml(string entryName)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var entry = archive.GetEntry(entryName)
            ?? throw new FileRejectedException($"Open XML package is missing required part '{entryName}'.");
        return OpenXml(entry, entryName);
    }

    public XmlReader? OpenOptionalXml(string entryName)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var entry = archive.GetEntry(entryName);
        return entry is null ? null : OpenXml(entry, entryName);
    }

    private XmlReader OpenXml(ZipArchiveEntry entry, string entryName)
    {
        if (entry.Length > options.MaximumArchiveEntryBytes)
            throw new FileRejectedException($"XML part '{entryName}' exceeds the configured per-entry safety limit.");

        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = options.MaximumXmlCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = true
        };
        return XmlReader.Create(entry.Open(), settings);
    }

    private static void ValidateArchive(ZipArchive archive, ViewerOpenOptions options)
    {
        if (archive.Entries.Count > options.MaximumArchiveEntries)
            throw new FileRejectedException($"Package contains more than {options.MaximumArchiveEntries:N0} entries.");

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > options.MaximumArchiveEntryBytes)
                throw new FileRejectedException($"Package part '{entry.FullName}' exceeds the per-entry safety limit.");
            try { expandedBytes = checked(expandedBytes + entry.Length); }
            catch (OverflowException) { throw new FileRejectedException("Package expanded size overflowed the safety counter."); }
            if (expandedBytes > options.MaximumArchiveExpandedBytes)
                throw new FileRejectedException("Package expanded size exceeds the configured safety limit.");

            if (entry.Length == 0) continue;
            if (entry.CompressedLength == 0 || (double)entry.Length / entry.CompressedLength > options.MaximumArchiveCompressionRatio)
                throw new FileRejectedException($"Package part '{entry.FullName}' exceeds the compression-ratio safety limit.");

            var normalized = entry.FullName.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Split('/').Any(segment => segment == ".."))
                throw new FileRejectedException("Package contains an unsafe part path.");
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        archive.Dispose();
        stream.Dispose();
    }
}
