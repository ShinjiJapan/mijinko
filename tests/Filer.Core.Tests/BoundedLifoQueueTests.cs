using Filer.Core;
using Xunit;

namespace Filer.Core.Tests;

public class BoundedLifoQueueTests
{
    [Fact]
    public void TryPop_ReturnsNewestFirst_Lifo()
    {
        var q = new BoundedLifoQueue<string, int>(10);
        q.Push("a", 1, out _);
        q.Push("b", 2, out _);
        q.Push("c", 3, out _);

        Assert.True(q.TryPop(out var v1)); Assert.Equal(3, v1);
        Assert.True(q.TryPop(out var v2)); Assert.Equal(2, v2);
        Assert.True(q.TryPop(out var v3)); Assert.Equal(1, v3);
        Assert.False(q.TryPop(out _));
    }

    [Fact]
    public void TryPop_OnEmpty_ReturnsFalse()
    {
        var q = new BoundedLifoQueue<string, int>(4);
        Assert.False(q.TryPop(out var v));
        Assert.Equal(0, v);
    }

    [Fact]
    public void Push_SameKey_UpdatesValueAndMovesToFront_WithoutGrowing()
    {
        var q = new BoundedLifoQueue<string, int>(10);
        q.Push("a", 1, out _);
        q.Push("b", 2, out _);
        // 既存キー a を再投入(値更新・最前面化)。件数は増えない。
        Assert.False(q.Push("a", 99, out _));
        Assert.Equal(2, q.Count);

        Assert.True(q.TryPop(out var first)); Assert.Equal(99, first);  // 繰り上げた a
        Assert.True(q.TryPop(out var second)); Assert.Equal(2, second); // b
    }

    [Fact]
    public void Push_OverCapacity_DropsOldest()
    {
        var q = new BoundedLifoQueue<string, int>(2);
        Assert.False(q.Push("a", 1, out _));
        Assert.False(q.Push("b", 2, out _));
        // 容量超過: 最古(a)が捨てられる。
        Assert.True(q.Push("c", 3, out var dropped));
        Assert.Equal(1, dropped);
        Assert.Equal(2, q.Count);
        Assert.False(q.ContainsKey("a"));

        Assert.True(q.TryPop(out var v1)); Assert.Equal(3, v1); // c
        Assert.True(q.TryPop(out var v2)); Assert.Equal(2, v2); // b
    }

    [Fact]
    public void Push_RefreshSameKey_PreventsItFromBeingDropped()
    {
        var q = new BoundedLifoQueue<string, int>(2);
        q.Push("a", 1, out _);
        q.Push("b", 2, out _);
        // a を再投入して最新化 → 次の超過で捨てられるのは最古になった b。
        q.Push("a", 1, out _);
        Assert.True(q.Push("c", 3, out var dropped));
        Assert.Equal(2, dropped);
        Assert.False(q.ContainsKey("b"));
        Assert.True(q.ContainsKey("a"));
    }

    [Fact]
    public void Capacity_BelowOne_TreatedAsOne()
    {
        var q = new BoundedLifoQueue<string, int>(0);
        Assert.False(q.Push("a", 1, out _));
        Assert.True(q.Push("b", 2, out var dropped));
        Assert.Equal(1, dropped);
        Assert.Equal(1, q.Count);
    }
}
