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
    public void Compute_LargeIdenticalWithSingleMiddleChange_IsTrimmedAndPrecise()
    {
        // 共通の前後を持つ大きなファイルでも、共通の prefix/suffix をトリムして中央だけ差分する。
        var left = Enumerable.Range(0, 5000).Select(i => $"line {i}").ToArray();
        var right = (string[])left.Clone();
        right[2500] = "CHANGED";

        var rows = LineDiff.Compute(left, right);

        Assert.Equal(5000, rows.Count);
        Assert.Single(rows, r => r.Kind != DiffRowKind.Equal);
        var changed = rows.Single(r => r.Kind != DiffRowKind.Equal);
        Assert.Equal(DiffRowKind.Modified, changed.Kind);
        Assert.Equal("line 2500", changed.LeftText);
        Assert.Equal("CHANGED", changed.RightText);
    }

    [Fact]
    public void Compute_HugeCompletelyDifferentMiddle_FallsBackToReplaceAll()
    {
        // 共通部分がなく積が上限を超える場合は、O(n*m) を避けて全置換(全行 Modified)へフォールバックする。
        var left = Enumerable.Range(0, 3000).Select(i => $"L{i}").ToArray();
        var right = Enumerable.Range(0, 3000).Select(i => $"R{i}").ToArray();

        var rows = LineDiff.Compute(left, right);

        Assert.Equal(3000, rows.Count);
        Assert.All(rows, r => Assert.Equal(DiffRowKind.Modified, r.Kind));
        Assert.Equal("L0", rows[0].LeftText);
        Assert.Equal("R0", rows[0].RightText);
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
