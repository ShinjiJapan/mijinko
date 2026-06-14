using Filer.Core;
using Xunit;

namespace Filer.Core.Tests;

public class GridNavigationTests
{
    // 10 項目 / 4 列のグリッド(行: 0-3 / 4-7 / 8-9)

    [Theory]
    [InlineData(0, 1)]   // 右へ1つ
    [InlineData(3, 4)]   // 行末から次行先頭へ
    [InlineData(9, 0)]   // 末尾から先頭へ回り込む
    public void Right_MovesNextWithWrap(int from, int expected) =>
        Assert.Equal(expected, GridNavigation.Move(10, 4, from, GridDirection.Right));

    [Theory]
    [InlineData(5, 4)]   // 左へ1つ
    [InlineData(0, 9)]   // 先頭から末尾へ回り込む
    public void Left_MovesPrevWithWrap(int from, int expected) =>
        Assert.Equal(expected, GridNavigation.Move(10, 4, from, GridDirection.Left));

    [Theory]
    [InlineData(0, 4)]   // 1行下へ
    [InlineData(5, 9)]   // 下に項目あり
    [InlineData(8, 8)]   // 最終行から下は無いので留まる
    [InlineData(6, 6)]   // 6+4=10 で範囲外、留まる
    public void Down_MovesByRowOrStays(int from, int expected) =>
        Assert.Equal(expected, GridNavigation.Move(10, 4, from, GridDirection.Down));

    [Theory]
    [InlineData(4, 0)]   // 1行上へ
    [InlineData(9, 5)]   // 上に項目あり
    [InlineData(2, 2)]   // 先頭行から上は無いので留まる
    public void Up_MovesByRowOrStays(int from, int expected) =>
        Assert.Equal(expected, GridNavigation.Move(10, 4, from, GridDirection.Up));

    [Fact]
    public void EmptyList_ReturnsZero()
    {
        Assert.Equal(0, GridNavigation.Move(0, 4, 0, GridDirection.Right));
        Assert.Equal(0, GridNavigation.Move(0, 4, 0, GridDirection.Down));
    }

    [Theory]
    [InlineData(GridDirection.Down, 3)]   // 列数0は1扱い=リストの上下移動と同じ
    [InlineData(GridDirection.Up, 1)]
    public void ColumnsLessThanOne_TreatedAsSingleColumn(GridDirection dir, int expected) =>
        Assert.Equal(expected, GridNavigation.Move(5, 0, 2, dir));

    [Theory]
    [InlineData(4, GridDirection.Down, 4)]   // 1列は上下クランプ(回り込まない)
    [InlineData(0, GridDirection.Up, 0)]
    public void SingleColumn_ClampsVertical(int from, GridDirection dir, int expected) =>
        Assert.Equal(expected, GridNavigation.Move(5, 1, from, dir));

    [Fact]
    public void OutOfRangeIndex_IsClamped()
    {
        Assert.Equal(0, GridNavigation.Move(5, 4, -3, GridDirection.Up));
        Assert.Equal(0, GridNavigation.Move(5, 4, 99, GridDirection.Right)); // 末尾扱い→右で先頭へ
    }
}
