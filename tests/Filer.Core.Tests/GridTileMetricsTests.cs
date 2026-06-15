using Filer.Core;
using Xunit;

namespace Filer.Core.Tests;

public class GridTileMetricsTests
{
    [Fact]
    public void Large_IsDoubleOfNormal()
    {
        Assert.Equal(GridTileMetrics.TileWidth(GridTileSize.Normal) * 2, GridTileMetrics.TileWidth(GridTileSize.Large));
        Assert.Equal(GridTileMetrics.ImageSize(GridTileSize.Normal) * 2, GridTileMetrics.ImageSize(GridTileSize.Large));
    }

    [Fact]
    public void ImageFitsInsideTile()
    {
        Assert.True(GridTileMetrics.ImageSize(GridTileSize.Normal) < GridTileMetrics.TileWidth(GridTileSize.Normal));
        Assert.True(GridTileMetrics.ImageSize(GridTileSize.Large) < GridTileMetrics.TileWidth(GridTileSize.Large));
    }

    [Theory]
    [InlineData(GridTileSize.Normal, GridTileSize.Large)]
    [InlineData(GridTileSize.Large, GridTileSize.Normal)]
    public void Next_Toggles(GridTileSize from, GridTileSize expected) =>
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
