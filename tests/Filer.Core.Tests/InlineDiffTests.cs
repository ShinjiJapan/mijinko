using Filer.Core;

namespace Filer.Core.Tests;

public class InlineDiffTests
{
    [Fact]
    public void Identical_NoChangedSegments()
    {
        var (left, right) = InlineDiff.Compute("hello", "hello");

        Assert.DoesNotContain(left, s => s.Changed);
        Assert.DoesNotContain(right, s => s.Changed);
        Assert.Equal("hello", string.Concat(left.Select(s => s.Text)));
        Assert.Equal("hello", string.Concat(right.Select(s => s.Text)));
    }

    [Fact]
    public void SingleCharSubstitution_HighlightsOnlyThatChar()
    {
        var (left, right) = InlineDiff.Compute("abc", "axc");

        // 左: a(eq) b(chg) c(eq) / 右: a(eq) x(chg) c(eq)
        Assert.Equal(new[] { ("a", false), ("b", true), ("c", false) },
            left.Select(s => (s.Text, s.Changed)));
        Assert.Equal(new[] { ("a", false), ("x", true), ("c", false) },
            right.Select(s => (s.Text, s.Changed)));
    }

    [Fact]
    public void SuffixChange_HighlightsChangedTailOnly()
    {
        var (left, right) = InlineDiff.Compute("int x = 1;", "int x = 2;");

        // 共通の "int x = " は非強調、変わった "1" / "2" のみ強調(";" は共通)
        Assert.Equal("int x = ", string.Concat(left.Where(s => !s.Changed).Select(s => s.Text)).Replace(";", ""));
        Assert.Contains(left, s => s.Changed && s.Text == "1");
        Assert.Contains(right, s => s.Changed && s.Text == "2");
    }

    [Fact]
    public void Insertion_OnlyRightHasChangedSegment()
    {
        var (left, right) = InlineDiff.Compute("abc", "abXc");

        Assert.DoesNotContain(left, s => s.Changed);
        Assert.Contains(right, s => s.Changed && s.Text == "X");
    }

    [Fact]
    public void CompletelyDifferent_AllChanged()
    {
        var (left, right) = InlineDiff.Compute("abc", "xyz");

        Assert.All(left, s => Assert.True(s.Changed));
        Assert.All(right, s => Assert.True(s.Changed));
    }

    [Fact]
    public void EmptyLeft_RightFullyChanged()
    {
        var (left, right) = InlineDiff.Compute("", "abc");

        Assert.Empty(left);
        Assert.Equal(new[] { ("abc", true) }, right.Select(s => (s.Text, s.Changed)));
    }

    [Fact]
    public void VeryLongLines_FallBackToWholeChanged()
    {
        var a = new string('a', 3000);
        var b = new string('b', 3000);

        var (left, right) = InlineDiff.Compute(a, b);

        // O(n*m) を避けるため丸ごと変更扱いになる(全体が1つの changed セグメント)。
        Assert.Single(left);
        Assert.True(left[0].Changed);
        Assert.Single(right);
        Assert.True(right[0].Changed);
    }
}
