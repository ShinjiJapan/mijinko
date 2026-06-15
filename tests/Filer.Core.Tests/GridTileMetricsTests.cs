using Filer.Core;
using Xunit;

namespace Filer.Core.Tests;

public class GridTileMetricsTests
{
    [Fact]
    public void Sizes_AreSmallAndLarge()
    {
        Assert.Equal(80, GridTileMetrics.ImageSize(GridTileSize.Normal));
        Assert.Equal(256, GridTileMetrics.ImageSize(GridTileSize.Large));
        Assert.True(GridTileMetrics.ImageSize(GridTileSize.Large) > GridTileMetrics.ImageSize(GridTileSize.Normal));
    }

    [Theory]
    [InlineData(GridTileSize.Normal)]
    [InlineData(GridTileSize.Large)]
    public void Image_DoesNotExceedThumbnailSource(GridTileSize size) =>
        // 表示は取得サイズ(256)以下=拡大せず鮮明。
        Assert.True(GridTileMetrics.ImageSize(size) <= 256);

    [Theory]
    [InlineData(GridTileSize.Normal)]
    [InlineData(GridTileSize.Large)]
    public void ImageFitsInsideTile(GridTileSize size) =>
        Assert.True(GridTileMetrics.ImageSize(size) < GridTileMetrics.TileWidth(size));

    [Theory]
    [InlineData(GridTileSize.Normal, GridTileSize.Large)]
    [InlineData(GridTileSize.Large, GridTileSize.Normal)]
    public void Next_TogglesBetweenTwoSizes(GridTileSize from, GridTileSize expected) =>
        Assert.Equal(expected, GridTileMetrics.Next(from));

    [Theory]
    [InlineData(GridTileSize.Normal)]
    [InlineData(GridTileSize.Large)]
    public void Cell_IsTilePlusChrome(GridTileSize size)
    {
        Assert.Equal(GridTileMetrics.TileWidth(size) + GridTileMetrics.CellChromeWidth, GridTileMetrics.CellWidth(size));
        Assert.Equal(GridTileMetrics.ImageSize(size) + GridTileMetrics.CellChromeHeight, GridTileMetrics.CellHeight(size));
        // 外形はタイル本体より必ず大きい
        Assert.True(GridTileMetrics.CellWidth(size) > GridTileMetrics.TileWidth(size));
        Assert.True(GridTileMetrics.CellHeight(size) > GridTileMetrics.ImageSize(size));
    }
}
