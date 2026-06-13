using Filer.Core;

namespace Filer.Core.Tests;

public class ColumnWidthCandidatesTests
{
    [Fact]
    public void Select_Empty_ReturnsEmpty()
    {
        Assert.Empty(ColumnWidthCandidates.Select(Array.Empty<string>()));
    }

    [Fact]
    public void Select_ReturnsLongestByDisplayWidth()
    {
        var result = ColumnWidthCandidates.Select(new[] { "ab", "abcde", "abc" }, 1);

        Assert.Equal(new[] { "abcde" }, result);
    }

    [Fact]
    public void Select_FullWidthCharsCountDouble()
    {
        // "ああ"=スコア4 > "abc"=スコア3
        var result = ColumnWidthCandidates.Select(new[] { "abc", "ああ" }, 1);

        Assert.Equal(new[] { "ああ" }, result);
    }

    [Fact]
    public void Select_ReturnsTopNDistinctStrings()
    {
        var result = ColumnWidthCandidates.Select(
            new[] { "a", "bb", "ccc", "dddd", "ccc", "dddd" }, 3);

        Assert.Equal(3, result.Count);
        Assert.Contains("dddd", result);
        Assert.Contains("ccc", result);
        Assert.Contains("bb", result);
    }

    [Fact]
    public void Select_IgnoresNullAndEmpty()
    {
        var result = ColumnWidthCandidates.Select(new[] { null, "", "a" }, 3);

        Assert.Equal(new[] { "a" }, result);
    }

    [Fact]
    public void Select_FewerValuesThanCount_ReturnsAll()
    {
        var result = ColumnWidthCandidates.Select(new[] { "a", "bb" }, 5);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Select_ManyValues_IsFast()
    {
        // 数十万件でも文字列スキャンのみで完了する(FormattedText 実測を呼び出し側で数件に抑えるための前段)。
        var values = Enumerable.Range(0, 200_000).Select(i => $"file{i}.manifest").ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = ColumnWidthCandidates.Select(values, 3);

        sw.Stop();
        Assert.Equal(3, result.Count);
        Assert.True(sw.ElapsedMilliseconds < 500, $"too slow: {sw.ElapsedMilliseconds}ms");
    }
}
