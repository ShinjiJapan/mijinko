using Filer.Core;

namespace Filer.Core.Tests;

public class FolderCompareHtmlRendererTests
{
    private static readonly ThemeColors Dark = ThemeColors.Dark;

    private static FolderCompareNode File(string name, FolderCompareKind kind,
        string? left = null, string? right = null, long? ls = null, long? rs = null) =>
        new(name, false, kind, left, right, ls, rs, Array.Empty<FolderCompareNode>());

    [Fact]
    public void RendersAllKindsWithClasses()
    {
        var nodes = new[]
        {
            File("same.txt", FolderCompareKind.Same, "L\\same.txt", "R\\same.txt", 1, 1),
            File("mod.txt", FolderCompareKind.Modified, "L\\mod.txt", "R\\mod.txt", 1, 2),
            File("left.txt", FolderCompareKind.LeftOnly, left: "L\\left.txt", ls: 1),
            File("right.txt", FolderCompareKind.RightOnly, right: "R\\right.txt", rs: 1),
        };

        var html = FolderCompareHtmlRenderer.ToHtmlDocument(nodes, "L", "R", Dark);

        Assert.Contains("class=\"same\"", html);
        Assert.Contains("class=\"modified clickable\"", html);
        Assert.Contains("class=\"leftonly\"", html);
        Assert.Contains("class=\"rightonly\"", html);
    }

    [Fact]
    public void ModifiedFile_HasClickableDataPaths()
    {
        var nodes = new[] { File("mod.txt", FolderCompareKind.Modified, "L\\mod.txt", "R\\mod.txt", 1, 2) };

        var html = FolderCompareHtmlRenderer.ToHtmlDocument(nodes, "L", "R", Dark);

        Assert.Contains("data-l=\"L\\mod.txt\"", html);
        Assert.Contains("data-r=\"R\\mod.txt\"", html);
    }

    [Fact]
    public void LeftOnlyFile_NotClickable()
    {
        var nodes = new[] { File("left.txt", FolderCompareKind.LeftOnly, left: "L\\left.txt", ls: 1) };

        var html = FolderCompareHtmlRenderer.ToHtmlDocument(nodes, "L", "R", Dark);

        Assert.Contains("<tr class=\"leftonly\">", html);
        Assert.DoesNotContain("data-l=", html);
    }

    [Fact]
    public void EscapesHtmlInNames()
    {
        var nodes = new[] { File("a<b>.txt", FolderCompareKind.LeftOnly, left: "L\\a<b>.txt", ls: 1) };

        var html = FolderCompareHtmlRenderer.ToHtmlDocument(nodes, "L", "R", Dark);

        Assert.Contains("a&lt;b&gt;.txt", html);
        Assert.DoesNotContain("<b>.txt", html);
    }

    [Fact]
    public void EmptyTree_ShowsNoDifferenceNotice()
    {
        var html = FolderCompareHtmlRenderer.ToHtmlDocument(Array.Empty<FolderCompareNode>(), "L", "R", Dark);

        Assert.Contains("差異はありません", html);
    }
}
