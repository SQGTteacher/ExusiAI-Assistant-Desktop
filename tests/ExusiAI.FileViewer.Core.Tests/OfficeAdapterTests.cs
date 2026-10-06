using ExusiAI.FileViewer.Core;
using ExusiAI.FileViewer.Office;

namespace ExusiAI.FileViewer.Core.Tests;

public sealed class OfficeAdapterTests
{
    [Fact]
    public async Task Missing_engine_explains_required_dependency()
    {
        var provider = new OfficeLayoutProvider(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "soffice.exe"));
        Assert.False(provider.IsAvailable);
        var exception = await Assert.ThrowsAsync<FileRejectedException>(async () =>
            await provider.OpenAsync("sample.ppt", ViewerOpenOptions.Default, CancellationToken.None));
        Assert.Contains("LibreOffice", exception.Message);
    }

    [Fact]
    public async Task Binary_office_header_is_checked_before_engine_launch()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ppt");
        try
        {
            await File.WriteAllTextAsync(path, "not OLE");
            var provider = new OfficeLayoutProvider(typeof(OfficeAdapterTests).Assembly.Location);
            await Assert.ThrowsAsync<FileRejectedException>(async () =>
                await provider.OpenAsync(path, ViewerOpenOptions.Default, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }
}
