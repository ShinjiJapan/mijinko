using Filer.Core;

namespace Filer.Core.Tests;

public sealed class CodeRendererTests
{
    [Theory]
    [InlineData("a.json", "json")]
    [InlineData("a.xml", "xml")]
    [InlineData("a.xaml", "xml")]
    [InlineData("a.csproj", "xml")]
    [InlineData("a.yaml", "yaml")]
    [InlineData("a.yml", "yaml")]
    [InlineData("a.ini", "ini")]
    [InlineData("a.toml", "ini")]
    [InlineData("a.cs", "csharp")]
    [InlineData("a.ts", "typescript")]
    [InlineData("a.js", "javascript")]
    [InlineData("a.py", "python")]
    [InlineData("a.sql", "sql")]
    [InlineData("a.ps1", "powershell")]
    [InlineData("a.bat", "dos")]
    [InlineData("a.cmd", "dos")]
    [InlineData("a.cpp", "cpp")]
    [InlineData("a.c", "c")]
    [InlineData("MyClass.cls", "apex")]
    [InlineData("anon.apex", "apex")]
    public void LanguageId_KnownExtensions_MapsToHljsId(string path, string expected)
    {
        Assert.Equal(expected, CodeRenderer.LanguageId(path));
    }

    [Fact]
    public void FormatSource_MinifiedJson_PrettyPrints()
    {
        var formatted = CodeRenderer.FormatSource("a.json", "{\"a\":1,\"b\":[1,2]}");

        Assert.Contains("\n", formatted);
        Assert.Contains("  \"a\": 1", formatted);
    }

    [Fact]
    public void FormatSource_InvalidJson_ReturnsOriginalUnchanged()
    {
        const string broken = "{ this is not json";

        Assert.Equal(broken, CodeRenderer.FormatSource("a.json", broken));
    }

    [Fact]
    public void FormatSource_NonJson_ReturnsOriginalUnchanged()
    {
        const string xml = "<root><a/></root>";

        Assert.Equal(xml, CodeRenderer.FormatSource("a.xml", xml));
    }

    [Fact]
    public void ToHtmlDocument_WrapsCodeInLanguageClassAndEscapes()
    {
        var html = CodeRenderer.ToHtmlDocument("<a> & \"x\"", "xml", ThemeColors.Dark);

        Assert.Contains("class=\"language-xml\"", html);
        Assert.Contains("&lt;a&gt; &amp; &quot;x&quot;", html);
        Assert.Contains("highlight.min.js", html);
    }

    [Fact]
    public void ToHtmlDocument_EmbedsConfiguredEditKey_PostsRequestEdit()
    {
        var html = CodeRenderer.ToHtmlDocument("x", "csharp", ThemeColors.Dark, new[] { "F1" }, new[] { "I" });
        Assert.Contains("request-edit", html);
        Assert.Contains("key.toLowerCase() === 'i'", html);
    }

    [Fact]
    public void ToHtmlDocument_EmbedsConfiguredSourceToggleKey_PostsToggleSource()
    {
        var html = CodeRenderer.ToHtmlDocument("x", "csharp", ThemeColors.Dark,
            new[] { "F1" }, new[] { "I" }, new[] { "Tab" }, new[] { "Escape", "Enter" });
        Assert.Contains("toggle-source", html);
        Assert.Contains("e.key === 'Tab'", html);
        Assert.DoesNotContain("key.toLowerCase() === 's'", html);
    }

    [Fact]
    public void ToHtmlDocument_DarkTheme_ReferencesDarkStylesheet()
    {
        var dark = CodeRenderer.ToHtmlDocument("x", "", ThemeColors.Dark);

        Assert.Contains("hl-dark.css", dark);
        Assert.DoesNotContain("hl-light.css", dark);
    }
}
