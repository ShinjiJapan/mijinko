using Filer.Core;

namespace Filer.Core.Tests;

public class DiffHtmlRendererTests
{
    private static string Render(IReadOnlyList<DiffRow> rows, string left = "a.txt", string right = "b.txt") =>
        DiffHtmlRenderer.ToHtmlDocument(rows, left, right, ThemeColors.Dark);

    [Fact]
    public void EscapesHtmlSpecialCharsInContent()
    {
        var rows = LineDiff.Compute(new[] { "<b>&\"x" }, new[] { "<b>&\"x" });

        var html = Render(rows);

        Assert.Contains("&lt;b&gt;&amp;", html);
        Assert.DoesNotContain("<b>&\"x", html);
    }

    [Fact]
    public void EmbedsFileNames()
    {
        var html = Render(LineDiff.Compute(new[] { "a" }, new[] { "a" }), "left&.txt", "right.txt");

        Assert.Contains("left&amp;.txt", html);
        Assert.Contains("right.txt", html);
    }

    [Theory]
    [InlineData(DiffRowKind.Equal, "equal")]
    [InlineData(DiffRowKind.Modified, "modified")]
    [InlineData(DiffRowKind.Deleted, "deleted")]
    [InlineData(DiffRowKind.Inserted, "inserted")]
    public void AppliesCssClassPerKind(DiffRowKind kind, string cssClass)
    {
        var row = new DiffRow(kind, 1, "L", 1, "R");

        var html = Render(new[] { row });

        Assert.Contains(cssClass, html);
    }

    [Fact]
    public void ModifiedRow_HighlightsOnlyChangedCharsWithSpan()
    {
        var rows = LineDiff.Compute(new[] { "int x = 1;" }, new[] { "int x = 2;" });

        var html = Render(rows);

        // 変わった文字だけが span.chg で包まれ、共通部分は素のまま。
        Assert.Contains("<span class=\"chg\">1</span>", html);
        Assert.Contains("<span class=\"chg\">2</span>", html);
        Assert.DoesNotContain("<span class=\"chg\">int", html);
    }

    [Fact]
    public void ModifiedRow_EscapesInsideSpan()
    {
        var rows = LineDiff.Compute(new[] { "a=1" }, new[] { "a=<b>" });

        var html = Render(rows);

        Assert.Contains("<span class=\"chg\">&lt;b&gt;</span>", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Theory]
    [InlineData(true, "同一")]
    [InlineData(false, "異なり")]
    public void BinaryNotice_StatesVerdictAndEscapesNames(bool identical, string verdict)
    {
        var html = DiffHtmlRenderer.BinaryNoticeDocument("a&.bin", "b.bin", identical, ThemeColors.Dark);

        Assert.Contains("バイナリ", html);
        Assert.Contains(verdict, html);
        Assert.Contains("a&amp;.bin", html);
        Assert.Contains("postMessage('close')", html);
    }

    [Fact]
    public void ProducesFullHtmlDocumentWithCloseScript()
    {
        var html = Render(LineDiff.Compute(new[] { "a" }, new[] { "b" }));

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("postMessage('close')", html);
        Assert.Contains("postMessage('cycle-view')", html);
    }
}
