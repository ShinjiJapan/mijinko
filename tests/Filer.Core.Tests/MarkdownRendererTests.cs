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
    public void ToHtmlDocument_EscapesRawHtmlInText()
    {
        // 本文中の生 HTML/スクリプトはそのまま実行されないことの最低限の確認
        var html = MarkdownRenderer.ToHtmlDocument("a < b & c");
        Assert.Contains("&lt; b &amp; c", html);
    }
}
