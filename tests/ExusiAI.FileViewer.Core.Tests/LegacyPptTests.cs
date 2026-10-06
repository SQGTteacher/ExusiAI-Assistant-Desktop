using ExusiAI.FileViewer.Core;

namespace ExusiAI.FileViewer.Core.Tests;

public sealed class LegacyPptTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(32)]
    public async Task Invalid_container_is_rejected_without_conversion(int length)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ppt");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[length]);
            await Assert.ThrowsAsync<FileRejectedException>(() => new LegacyPptFileViewerProvider().OpenAsync(path, ViewerOpenOptions.Default, CancellationToken.None).AsTask());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Binary_input_budget_is_checked_before_parser()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ppt");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[20]);
            await Assert.ThrowsAsync<FileRejectedException>(() => new LegacyPptFileViewerProvider().OpenAsync(path, ViewerOpenOptions.Default with { MaximumLegacyPresentationBytes = 10 }, CancellationToken.None).AsTask());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Cancellation_before_open_does_not_create_conversion_work()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LegacyPptFileViewerProvider().OpenAsync("missing.ppt", ViewerOpenOptions.Default, new CancellationToken(true)).AsTask());
    }
}
