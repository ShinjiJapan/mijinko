using Filer.Core;

namespace Filer.Core.Tests;

public class FolderCompareTransferTests
{
    private static FolderCompareNode File(string name, FolderCompareKind kind, string? left, string? right) =>
        new(name, false, kind, left, right, null, null, Array.Empty<FolderCompareNode>());

    private static FolderCompareNode Dir(string name, FolderCompareKind kind, params FolderCompareNode[] children) =>
        new(name, true, kind, "L\\" + name, "R\\" + name, null, null, children);

    private static readonly IReadOnlyList<FolderCompareNode> Tree = new[]
    {
        File("same.txt", FolderCompareKind.Same, "L\\same.txt", "R\\same.txt"),
        File("mod.txt", FolderCompareKind.Modified, "L\\mod.txt", "R\\mod.txt"),
        File("left.txt", FolderCompareKind.LeftOnly, "L\\left.txt", null),
        File("right.txt", FolderCompareKind.RightOnly, null, "R\\right.txt"),
        Dir("sub", FolderCompareKind.Modified,
            File("child.txt", FolderCompareKind.Modified, "L\\sub\\child.txt", "R\\sub\\child.txt")),
    };

    [Fact]
    public void LeftDifferences_IncludesModifiedAndLeftOnly()
    {
        var items = FolderCompareTransfer.Collect(Tree, FolderCompareSide.Left,
            includeDifferences: true, includeSame: false);

        var rels = items.Select(i => i.RelativePath).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "left.txt", "mod.txt", "sub\\child.txt" }, rels);
        Assert.All(items, i => Assert.StartsWith("L\\", i.FullPath));
    }

    [Fact]
    public void LeftSame_IncludesSameOnly()
    {
        var items = FolderCompareTransfer.Collect(Tree, FolderCompareSide.Left,
            includeDifferences: false, includeSame: true);

        Assert.Equal(new[] { "same.txt" }, items.Select(i => i.RelativePath).ToArray());
    }

    [Fact]
    public void RightDifferences_IncludesModifiedAndRightOnly()
    {
        var items = FolderCompareTransfer.Collect(Tree, FolderCompareSide.Right,
            includeDifferences: true, includeSame: false);

        var rels = items.Select(i => i.RelativePath).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "mod.txt", "right.txt", "sub\\child.txt" }, rels);
        Assert.All(items, i => Assert.StartsWith("R\\", i.FullPath));
    }

    [Fact]
    public void BothOptions_IncludeSameAndDifferences()
    {
        var items = FolderCompareTransfer.Collect(Tree, FolderCompareSide.Left,
            includeDifferences: true, includeSame: true);

        Assert.Equal(4, items.Count);   // same + mod + left + sub/child
    }

    [Fact]
    public void NothingSelected_ReturnsEmpty()
    {
        var items = FolderCompareTransfer.Collect(Tree, FolderCompareSide.Left,
            includeDifferences: false, includeSame: false);

        Assert.Empty(items);
    }
}
