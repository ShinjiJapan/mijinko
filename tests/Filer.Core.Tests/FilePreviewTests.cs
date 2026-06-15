using Filer.Core;

namespace Filer.Core.Tests;

public sealed class FilePreviewTests
{
    [Theory]
    [InlineData("a.png")]
    [InlineData("photo.JPG")]
    [InlineData("img.jpeg")]
    [InlineData("icon.ico")]
    [InlineData("anim.gif")]
    [InlineData(@"C:\dir\pic.bmp")]
    public void ClassifyByExtension_ImageExtensions_ReturnsImage(string path)
    {
        Assert.Equal(PreviewKind.Image, FilePreview.ClassifyByExtension(path));
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("run.log")]
    [InlineData("data.csv")]
    [InlineData("table.tsv")]
    [InlineData(".gitignore")]
    [InlineData(".editorconfig")]
    [InlineData("App.sln")]
    public void ClassifyByExtension_PlainTextExtensions_ReturnsText(string path)
    {
        Assert.Equal(PreviewKind.Text, FilePreview.ClassifyByExtension(path));
    }

    [Theory]
    [InlineData("data.json")]
    [InlineData("config.yaml")]
    [InlineData("config.YML")]
    [InlineData("settings.ini")]
    [InlineData("Cargo.toml")]
    [InlineData("Program.cs")]
    [InlineData("App.xaml")]
    [InlineData("build.csproj")]
    [InlineData("doc.xml")]
    [InlineData("style.css")]
    [InlineData("main.ts")]
    [InlineData("app.py")]
    [InlineData("query.sql")]
    [InlineData("deploy.ps1")]
    [InlineData("run.bat")]
    [InlineData("MyController.cls")]
    [InlineData("anon.apex")]
    public void ClassifyByExtension_CodeExtensions_ReturnsCode(string path)
    {
        Assert.Equal(PreviewKind.Code, FilePreview.ClassifyByExtension(path));
    }

    [Theory]
    [InlineData(@"C:\proj\index.html")]
    [InlineData("page.HTM")]
    [InlineData("doc.xhtml")]
    [InlineData("saved.mht")]
    [InlineData("saved.MHTML")]
    [InlineData("icon.svg")]
    public void ClassifyByExtension_HtmlExtensions_ReturnsHtml(string path)
    {
        Assert.Equal(PreviewKind.Html, FilePreview.ClassifyByExtension(path));
    }

    [Theory]
    [InlineData("notes.MD")]
    [InlineData("readme.md")]
    [InlineData(@"C:\doc\design.markdown")]
    public void ClassifyByExtension_MarkdownExtensions_ReturnsMarkdown(string path)
    {
        Assert.Equal(PreviewKind.Markdown, FilePreview.ClassifyByExtension(path));
    }

    [Theory]
    [InlineData("manual.pdf")]
    [InlineData("REPORT.PDF")]
    [InlineData(@"C:\docs\spec.Pdf")]
    public void ClassifyByExtension_PdfExtension_ReturnsPdf(string path)
    {
        Assert.Equal(PreviewKind.Pdf, FilePreview.ClassifyByExtension(path));
    }

    [Theory]
    [InlineData("app.exe")]
    [InlineData("archive.zip")]
    [InlineData("movie.mp4")]
    [InlineData("noext")]
    [InlineData("")]
    public void ClassifyByExtension_Unsupported_ReturnsNone(string path)
    {
        Assert.Equal(PreviewKind.None, FilePreview.ClassifyByExtension(path));
    }

    // Markdown / HTML はデフォルトでソース表示(S でレンダリングへ切替)。
    [Theory]
    [InlineData(PreviewKind.Markdown)]
    [InlineData(PreviewKind.Html)]
    public void InitialSourceMode_MarkdownAndHtml_DefaultsToSource(PreviewKind kind)
    {
        Assert.True(FilePreview.InitialSourceMode(kind));
    }

    // Code はデフォルトでハイライト表示(レンダリング)。その他もレンダリング側を初期表示とする。
    [Theory]
    [InlineData(PreviewKind.Code)]
    [InlineData(PreviewKind.Text)]
    [InlineData(PreviewKind.Image)]
    [InlineData(PreviewKind.Pdf)]
    [InlineData(PreviewKind.None)]
    public void InitialSourceMode_Others_DefaultsToRendered(PreviewKind kind)
    {
        Assert.False(FilePreview.InitialSourceMode(kind));
    }

    // テキスト系(Text/Markdown/Code/Html)はエディターで編集可能。
    [Theory]
    [InlineData(PreviewKind.Text)]
    [InlineData(PreviewKind.Markdown)]
    [InlineData(PreviewKind.Code)]
    [InlineData(PreviewKind.Html)]
    public void IsEditable_TextKinds_ReturnsTrue(PreviewKind kind)
    {
        Assert.True(FilePreview.IsEditable(kind));
    }

    // 画像・PDF・非対応は編集不可。
    [Theory]
    [InlineData(PreviewKind.Image)]
    [InlineData(PreviewKind.Pdf)]
    [InlineData(PreviewKind.None)]
    public void IsEditable_NonTextKinds_ReturnsFalse(PreviewKind kind)
    {
        Assert.False(FilePreview.IsEditable(kind));
    }

    // レンダリング表示を持つ(編集中に逆ペインでプレビューできる)のは Markdown/Html/Code。
    [Theory]
    [InlineData(PreviewKind.Markdown)]
    [InlineData(PreviewKind.Html)]
    [InlineData(PreviewKind.Code)]
    public void HasRenderedPreview_RenderableKinds_ReturnsTrue(PreviewKind kind)
    {
        Assert.True(FilePreview.HasRenderedPreview(kind));
    }

    [Theory]
    [InlineData(PreviewKind.Text)]
    [InlineData(PreviewKind.Image)]
    [InlineData(PreviewKind.Pdf)]
    [InlineData(PreviewKind.None)]
    public void HasRenderedPreview_OtherKinds_ReturnsFalse(PreviewKind kind)
    {
        Assert.False(FilePreview.HasRenderedPreview(kind));
    }
}
