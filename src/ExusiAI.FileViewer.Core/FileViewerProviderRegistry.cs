namespace ExusiAI.FileViewer.Core;

public sealed class FileViewerProviderRegistry(IEnumerable<IFileViewerProvider> providers)
{
    private readonly IFileViewerProvider[] providers = providers
        .OrderByDescending(provider => provider.Priority)
        .ThenBy(provider => provider.Id, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyList<IFileViewerProvider> Providers => providers;

    public IFileViewerProvider Resolve(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var extension = Path.GetExtension(filePath);
        return providers.FirstOrDefault(provider => provider.SupportedExtensions.Contains(extension))
            ?? throw new UnsupportedFileFormatException(string.IsNullOrEmpty(extension) ? "(no extension)" : extension);
    }

    public ValueTask<ViewerDocument> OpenAsync(
        string filePath,
        ViewerOpenOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? ViewerOpenOptions.Default;
        effectiveOptions.Validate();
        return Resolve(filePath).OpenAsync(filePath, effectiveOptions, cancellationToken);
    }
}
