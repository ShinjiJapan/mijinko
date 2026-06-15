using Filer.Core;
using Xunit;

namespace Filer.Core.Tests;

public class GridVirtualizationTests
{
    [Theory]
    [InlineData(400, 100, 4)]
    [InlineData(399, 100, 3)]   // 端数は切り捨て
    [InlineData(50, 100, 1)]    // 1個も入らなくても最低1列
    [InlineData(100, 0, 1)]     // 不正なタイル幅は1列
    public void Columns_FitsViewportWidth(double viewportWidth, double itemWidth, int expected) =>
        Assert.Equal(expected, GridVirtualization.Columns(viewportWidth, itemWidth));

    [Theory]
    [InlineData(0, 4, 0)]
    [InlineData(4, 4, 1)]
    [InlineData(5, 4, 2)]   // 端数は1行繰り上げ
    [InlineData(8, 4, 2)]
    [InlineData(9, 4, 3)]
    [InlineData(3, 0, 3)]   // columns<1 は1扱い
    public void RowCount_WrapsItems(int itemCount, int columns, int expected) =>
        Assert.Equal(expected, GridVirtualization.RowCount(itemCount, columns));

    [Fact]
    public void ExtentHeight_IsRowsTimesItemHeight()
    {
        // 10件・4列 → 3行 × 高さ50 = 150
        Assert.Equal(150, GridVirtualization.ExtentHeight(10, 4, 50));
        Assert.Equal(0, GridVirtualization.ExtentHeight(0, 4, 50));
    }

    [Fact]
    public void VisibleRange_Empty_WhenNoItems()
    {
        var (first, last) = GridVirtualization.VisibleRange(0, 200, 50, 4, 0, bufferRows: 0);
        Assert.True(first > last);   // 空(0,-1)
    }

    [Fact]
    public void VisibleRange_TopOfList_NoBuffer()
    {
        // 高さ50のタイル・4列・viewport高さ100 → 行0,1 が見える = index 0..7
        var (first, last) = GridVirtualization.VisibleRange(0, 100, 50, 4, 100, bufferRows: 0);
        Assert.Equal(0, first);
        Assert.Equal(7, last);
    }

    [Fact]
    public void VisibleRange_ScrolledDown_NoBuffer()
    {
        // offsetY=100 → 行2,3 が見える = index 8..15
        var (first, last) = GridVirtualization.VisibleRange(100, 100, 50, 4, 100, bufferRows: 0);
        Assert.Equal(8, first);
        Assert.Equal(15, last);
    }

    [Fact]
    public void VisibleRange_AppliesBufferRows()
    {
        // offsetY=100(行2,3)+前後1行バッファ → 行1..4 = index 4..19
        var (first, last) = GridVirtualization.VisibleRange(100, 100, 50, 4, 100, bufferRows: 1);
        Assert.Equal(4, first);
        Assert.Equal(19, last);
    }

    [Fact]
    public void VisibleRange_ClampsToItemCount()
    {
        // 末尾付近: 10件・4列(=3行)。最終行までスクロール。バッファで行外を要求しても件数内にクランプ。
        var (first, last) = GridVirtualization.VisibleRange(100, 100, 50, 4, 10, bufferRows: 1);
        Assert.True(last <= 9);
        Assert.Equal(9, last);
    }

    [Fact]
    public void VisibleRange_BufferDoesNotGoNegative()
    {
        var (first, _) = GridVirtualization.VisibleRange(0, 100, 50, 4, 100, bufferRows: 2);
        Assert.Equal(0, first);
    }
}
