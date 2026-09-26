namespace ExusiAI.FileViewer.Core;

internal static class SafeFileAccess
{
    public static FileInfo Inspect(string filePath, ViewerOpenOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        options.Validate();
        var fullPath = Path.GetFullPath(filePath);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("The selected file does not exist.", fullPath);
        if ((info.Attributes & FileAttributes.Directory) != 0) throw new FileRejectedException("Directories cannot be opened as documents.");
        if (info.Length > options.MaximumFileBytes)
            throw new FileRejectedException($"The file is {info.Length:N0} bytes and exceeds the configured {options.MaximumFileBytes:N0}-byte safety limit.");
        return info;
    }

    public static FileStream OpenSequentialRead(FileInfo info) => new(
        info.FullName,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 64 * 1024,
        options: FileOptions.Asynchronous | FileOptions.SequentialScan);

    public static FileStream OpenPackageRead(FileInfo info) => new(
        info.FullName,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 64 * 1024,
        options: FileOptions.RandomAccess);
}
