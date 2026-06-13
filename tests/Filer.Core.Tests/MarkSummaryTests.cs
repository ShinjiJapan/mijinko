using Filer.Core;

namespace Filer.Core.Tests;

public class MarkSummaryTests
{
    [Theory]
    [InlineData(0, "0B")]
    [InlineData(512, "512B")]
    [InlineData(1024, "1KB")]
    [InlineData(1536, "1.5KB")]
    [InlineData(4_718_592, "4.5MB")]
    public void FormatSize_UsesCompactUnitsWithoutSpace(long bytes, string expected)
    {
        Assert.Equal(expected, MarkSummary.FormatSize(bytes));
    }

    [Fact]
    public void Format_BuildsMarkedCountSlashTotalAndSize()
    {
        Assert.Equal("marked 12/560 4.5MB", MarkSummary.Format(12, 560, 4_718_592));
    }
}
