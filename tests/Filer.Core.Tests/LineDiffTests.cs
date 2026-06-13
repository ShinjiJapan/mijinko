using Filer.Core;

namespace Filer.Core.Tests;

public class LineDiffTests
{
    private static string[] Lines(params string[] lines) => lines;

    [Fact]
    public void Compute_IdenticalFiles_AllRowsEqual()
    {
        var rows = LineDiff.Compute(Lines("a", "b", "c"), Lines("a", "b", "c"));

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(DiffRowKind.Equal, r.Kind));
        Assert.Equal(new int?[] { 1, 2, 3 }, rows.Select(r => r.LeftNo));
        Assert.Equal(new int?[] { 1, 2, 3 }, rows.Select(r => r.RightNo));
    }

    [Fact]
    public void Compute_PureInsert_RowsAreInserted()
    {
        var rows = LineDiff.Compute(Lines("a", "b"), Lines("a", "x", "b"));

        Assert.Equal(DiffRowKind.Equal, rows[0].Kind);
        Assert.Equal(DiffRowKind.Inserted, rows[1].Kind);
        Assert.Null(rows[1].LeftNo);
        Assert.Equal("x", rows[1].RightText);
        Assert.Equal(2, rows[1].RightNo);
        Assert.Equal(DiffRowKind.Equal, rows[2].Kind);
    }

    [Fact]
    public void Compute_PureDelete_RowsAreDeleted()
    {
        var rows = LineDiff.Compute(Lines("a", "x", "b"), Lines("a", "b"));

        Assert.Equal(DiffRowKind.Equal, rows[0].Kind);
        Assert.Equal(DiffRowKind.Deleted, rows[1].Kind);
        Assert.Equal("x", rows[1].LeftText);
        Assert.Equal(2, rows[1].LeftNo);
        Assert.Null(rows[1].RightNo);
        Assert.Equal(DiffRowKind.Equal, rows[2].Kind);
    }

    [Fact]
    public void Compute_SingleLineChange_BecomesModifiedRow()
    {
        var rows = LineDiff.Compute(Lines("a", "bar", "c"), Lines("a", "baz", "c"));

        Assert.Equal(3, rows.Count);
        Assert.Equal(DiffRowKind.Modified, rows[1].Kind);
        Assert.Equal("bar", rows[1].LeftText);
        Assert.Equal("baz", rows[1].RightText);
        Assert.Equal(2, rows[1].LeftNo);
        Assert.Equal(2, rows[1].RightNo);
    }

    [Fact]
    public void Compute_BothEmpty_NoRows()
    {
        Assert.Empty(LineDiff.Compute(Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public void Compute_LeftEmpty_AllInserted()
    {
        var rows = LineDiff.Compute(Array.Empty<string>(), Lines("a", "b"));

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(DiffRowKind.Inserted, r.Kind));
        Assert.All(rows, r => Assert.Null(r.LeftNo));
    }

    [Fact]
    public void Compute_UnevenChangeBlock_PairsThenRemainderInserted()
    {
        // 左1行 → 右3行に変化。1行は Modified、残り2行は Inserted。
        var rows = LineDiff.Compute(Lines("a", "old", "z"), Lines("a", "new1", "new2", "new3", "z"));

        Assert.Equal(DiffRowKind.Equal, rows[0].Kind);
        Assert.Equal(DiffRowKind.Modified, rows[1].Kind);
        Assert.Equal("old", rows[1].LeftText);
        Assert.Equal("new1", rows[1].RightText);
        Assert.Equal(DiffRowKind.Inserted, rows[2].Kind);
        Assert.Equal("new2", rows[2].RightText);
        Assert.Equal(DiffRowKind.Inserted, rows[3].Kind);
        Assert.Equal("new3", rows[3].RightText);
        Assert.Equal(DiffRowKind.Equal, rows[4].Kind);
    }

    [Fact]
    public void Compute_UnevenChangeBlock_RemainderDeletedWhenLeftLonger()
    {
        var rows = LineDiff.Compute(Lines("a", "old1", "old2", "old3", "z"), Lines("a", "new", "z"));

        Assert.Equal(DiffRowKind.Equal, rows[0].Kind);
        Assert.Equal(DiffRowKind.Modified, rows[1].Kind);
        Assert.Equal("old1", rows[1].LeftText);
        Assert.Equal("new", rows[1].RightText);
        Assert.Equal(DiffRowKind.Deleted, rows[2].Kind);
        Assert.Equal("old2", rows[2].LeftText);
        Assert.Equal(DiffRowKind.Deleted, rows[3].Kind);
        Assert.Equal("old3", rows[3].LeftText);
        Assert.Equal(DiffRowKind.Equal, rows[4].Kind);
    }
}
