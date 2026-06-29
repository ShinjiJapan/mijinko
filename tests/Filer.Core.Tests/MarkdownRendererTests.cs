using Filer.Core;

namespace Filer.Core.Tests;

public sealed class MarkdownRendererTests
{
    [Fact]
    public void ToHtmlDocument_Heading_RendersHeadingTag()
    {
        var html = MarkdownRenderer.ToHtmlDocument("# タイトル");
        Assert.Contains("<h1", html);
        Assert.Contains("タイトル", html);
    }

    [Fact]
    public void ToHtmlDocument_Table_RendersTable()
    {
        var md = "| A | B |\n|---|---|\n| 1 | 2 |";
        var html = MarkdownRenderer.ToHtmlDocument(md);
        Assert.Contains("<table", html);
    }

    [Fact]
    public void ToHtmlDocument_MermaidFence_RendersAsMermaidPre()
    {
        var md = "```mermaid\nflowchart TD\n  A --> B\n```";
        var html = MarkdownRenderer.ToHtmlDocument(md);
        // mermaid.js が走査する <pre class="mermaid"> に変換される
        Assert.Contains("<pre class=\"mermaid\">", html);
        // 通常のコードブロック表現にはならない
        Assert.DoesNotContain("language-mermaid", html);
        // 図の本文は保持される
        Assert.Contains("flowchart TD", html);
    }

    [Fact]
    public void ToHtmlDocument_MermaidFence_AddsZoomToolbar()
    {
        var md = "```mermaid\nflowchart TD\n  A --> B\n```";
        var html = MarkdownRenderer.ToHtmlDocument(md);
        // 図ごとに拡大/縮小/全画面のツールバーが付く
        Assert.Contains("mermaid-toolbar", html);
        Assert.Contains("data-act=\"in\"", html);
        Assert.Contains("data-act=\"out\"", html);
        Assert.Contains("data-act=\"full\"", html);
        // 図は折り返し容器に包まれる
        Assert.Contains("class=\"mermaid-fig\"", html);
    }

    [Fact]
    public void ToHtmlDocument_NoMermaid_HasNoToolbar()
    {
        // CSS セレクタや JS の querySelector 文字列は常駐するため、ツールバーの実体マークアップで判定する
        var html = MarkdownRenderer.ToHtmlDocument("# x\n\n本文のみ");
        Assert.DoesNotContain("class=\"mermaid-fig\"", html);
        Assert.DoesNotContain("data-act=\"full\"", html);
    }

    [Fact]
    public void ToHtmlDocument_NormalCodeFence_StaysAsCodeBlock()
    {
        var md = "```csharp\nvar x = 1;\n```";
        var html = MarkdownRenderer.ToHtmlDocument(md);
        Assert.Contains("<code", html);
        Assert.DoesNotContain("<pre class=\"mermaid\">", html);
    }

    [Fact]
    public void ToHtmlDocument_IncludesMermaidScriptAndInit()
    {
        var html = MarkdownRenderer.ToHtmlDocument("# x");
        // ローカル同梱の mermaid.js を参照
        Assert.Contains("mermaid.min.js", html);
        // 初期化が含まれる
        Assert.Contains("mermaid.initialize", html);
    }

    [Fact]
    public void ToHtmlDocument_IsFullHtmlDocument()
    {
        var html = MarkdownRenderer.ToHtmlDocument("# x");
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<body", html);
    }

    [Fact]
    public void ToHtmlDocument_EmbedsConfiguredFullscreenKey_NotHardcodedF1()
    {
        var html = MarkdownRenderer.ToHtmlDocument("# x", ThemeColors.Dark, new[] { "F2" });
        Assert.Contains("e.key === 'F2'", html);
        Assert.DoesNotContain("e.key === 'F1'", html);
    }

    [Fact]
    public void ToHtmlDocument_EmbedsConfiguredEditKey_PostsRequestEdit()
    {
        var html = MarkdownRenderer.ToHtmlDocument("# x", ThemeColors.Dark, new[] { "F1" }, new[] { "I" });
        Assert.Contains("request-edit", html);
        Assert.Contains("key.toLowerCase() === 'i'", html);
    }

    [Fact]
    public void ToHtmlDocument_NoEditGestures_EditConditionIsFalse()
    {
        var html = MarkdownRenderer.ToHtmlDocument("# x", ThemeColors.Dark, new[] { "F1" });
        // 編集キー未指定なら編集分岐は常に false(発火しない)。
        Assert.Contains("if (false)", html);
    }

    [Fact]
    public void ToHtmlDocument_EmbedsConfiguredSourceToggleKey_PostsToggleSource()
    {
        var html = MarkdownRenderer.ToHtmlDocument("# x", ThemeColors.Dark,
            new[] { "F1" }, new[] { "I" }, new[] { "Tab" }, new[] { "Escape", "Enter" });
        Assert.Contains("toggle-source", html);
        Assert.Contains("e.key === 'Tab'", html);
        // 既定 S にハードコードしていない。
        Assert.DoesNotContain("key.toLowerCase() === 's'", html);
    }

    [Fact]
    public void ToHtmlDocument_EmbedsConfiguredCloseKey()
    {
        var html = MarkdownRenderer.ToHtmlDocument("# x", ThemeColors.Dark,
            new[] { "F1" }, new[] { "I" }, new[] { "S" }, new[] { "Q" });
        Assert.Contains("'close'", html);
        Assert.Contains("key.toLowerCase() === 'q'", html);
    }

    [Fact]
    public void ToHtmlDocument_EscapesRawHtmlInText()
    {
        // 本文中の生 HTML/スクリプトはそのまま実行されないことの最低限の確認
        var html = MarkdownRenderer.ToHtmlDocument("a < b & c");
        Assert.Contains("&lt; b &amp; c", html);
    }

    [Fact]
    public void RebaseImages_RelativeMarkdownImage_RebasedAndRootIsMdDir()
    {
        var html = MarkdownRenderer.ToHtmlDocument("![x](pic.png)", ThemeColors.Dark);
        var r = MarkdownRenderer.RebaseImages(html, @"C:\docs\sub", "https://filer.doc/");
        Assert.Contains("src=\"https://filer.doc/pic.png\"", r.Html);
        Assert.Equal(@"C:\docs\sub", r.MappedRoot);
    }

    [Fact]
    public void RebaseImages_RawHtmlImage_AlsoRebased()
    {
        var html = MarkdownRenderer.ToHtmlDocument("<img src=\"img/pic.png\">", ThemeColors.Dark);
        var r = MarkdownRenderer.RebaseImages(html, @"C:\docs\sub", "https://filer.doc/");
        Assert.Contains("src=\"https://filer.doc/img/pic.png\"", r.Html);
        Assert.Equal(@"C:\docs\sub", r.MappedRoot);
    }

    [Fact]
    public void RebaseImages_ParentReference_RaisesRootToAncestor()
    {
        // ../img/pic.png は md フォルダーの親 (C:\docs) を共通祖先として解決する
        var html = MarkdownRenderer.ToHtmlDocument("![x](../img/pic.png)", ThemeColors.Dark);
        var r = MarkdownRenderer.RebaseImages(html, @"C:\docs\sub", "https://filer.doc/");
        Assert.Contains("src=\"https://filer.doc/img/pic.png\"", r.Html);
        Assert.Equal(@"C:\docs", r.MappedRoot);
    }

    [Fact]
    public void RebaseImages_MixedSameDirAndParent_RootCoversBoth()
    {
        // 同階層画像 + 上位画像が混在 → ルートは親、同階層は sub/ 配下として表現される
        var md = "![a](pic.png)\n\n![b](../img/q.png)";
        var html = MarkdownRenderer.ToHtmlDocument(md, ThemeColors.Dark);
        var r = MarkdownRenderer.RebaseImages(html, @"C:\docs\sub", "https://filer.doc/");
        Assert.Equal(@"C:\docs", r.MappedRoot);
        Assert.Contains("src=\"https://filer.doc/sub/pic.png\"", r.Html);
        Assert.Contains("src=\"https://filer.doc/img/q.png\"", r.Html);
    }

    [Fact]
    public void RebaseImages_LeavesAbsoluteUrlsAndDataUri()
    {
        var md = "![a](https://example.com/a.png)\n\n![b](data:image/png;base64,AAAA)";
        var html = MarkdownRenderer.ToHtmlDocument(md, ThemeColors.Dark);
        var r = MarkdownRenderer.RebaseImages(html, @"C:\docs\sub", "https://filer.doc/");
        Assert.Contains("src=\"https://example.com/a.png\"", r.Html);
        Assert.Contains("src=\"data:image/png;base64,AAAA\"", r.Html);
        Assert.Null(r.MappedRoot);   // ローカル相対画像が無いのでマップ不要
    }

    [Fact]
    public void RebaseImages_EncodesSpacesInPath()
    {
        var html = MarkdownRenderer.ToHtmlDocument("<img src=\"my pic.png\">", ThemeColors.Dark);
        var r = MarkdownRenderer.RebaseImages(html, @"C:\docs", "https://filer.doc/");
        Assert.Contains("src=\"https://filer.doc/my%20pic.png\"", r.Html);
    }
}
