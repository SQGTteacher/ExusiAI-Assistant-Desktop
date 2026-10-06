using System.Text;
using ExusiAI.FileViewer.Core;

namespace ExusiAI.FileViewer.Core.Tests;

public sealed class LegacyDocEncodingTests
{
    [Fact]
    public void Provider_initializes_legacy_encodings_without_desktop_startup()
    {
        _ = new LegacyDocFileViewerProvider();
        Assert.Equal(1252, Encoding.GetEncoding("Windows-1252").CodePage);
        Assert.Equal("é", Encoding.GetEncoding(1252).GetString(new byte[] { 0xE9 }));
    }
}
