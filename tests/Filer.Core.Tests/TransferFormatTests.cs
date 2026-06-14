using Filer.Core;

namespace Filer.Core.Tests;

/// <summary>進捗ダイアログ表示用の人間可読フォーマット。</summary>
public sealed class TransferFormatTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1610612736, "1.5 GB")]
    public void Size_FormatsHumanReadable(long bytes, string expected)
    {
        Assert.Equal(expected, TransferFormat.Size(bytes));
    }

    [Fact]
    public void Rate_AppendsPerSecond()
    {
        Assert.Equal("1.0 MB/s", TransferFormat.Rate(1024 * 1024));
    }

    [Fact]
    public void Eta_UnderOneMinute_ShowsSeconds()
    {
        Assert.Equal("残り 12秒", TransferFormat.Eta(TimeSpan.FromSeconds(12)));
    }

    [Fact]
    public void Eta_OverOneMinute_ShowsMinutesAndSeconds()
    {
        Assert.Equal("残り 1分23秒", TransferFormat.Eta(TimeSpan.FromSeconds(83)));
    }
}
