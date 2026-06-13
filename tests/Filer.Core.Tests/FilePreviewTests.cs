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
    [InlineData("data.json")]
    [InlineData("config.yaml")]
    [InlineData("Program.cs")]
    public void ClassifyByExtension_TextExtensions_ReturnsText(string path)
    {
        Assert.Equal(PreviewKind.Text, FilePreview.ClassifyByExtension(path));
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
}
