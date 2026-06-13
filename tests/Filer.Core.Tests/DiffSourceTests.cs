using System.IO;
using System.Text;
using Filer.Core;

namespace Filer.Core.Tests;

public class DiffSourceTests : IDisposable
{
    private readonly string _dir;

    public DiffSourceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "DiffSourceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Read_SplitsMixedNewlines()
    {
        var path = Write("mixed.txt", Encoding.UTF8.GetBytes("a\r\nb\nc\rd"));

        var content = DiffSource.Read(path);

        Assert.Equal(DiffContentKind.Text, content.Kind);
        Assert.Equal(new[] { "a", "b", "c", "d" }, content.Lines);
    }

    [Fact]
    public void Read_TrailingNewline_DoesNotAddEmptyLine()
    {
        var path = Write("trail.txt", Encoding.UTF8.GetBytes("a\nb\n"));

        Assert.Equal(new[] { "a", "b" }, DiffSource.Read(path).Lines);
    }

    [Fact]
    public void Read_Utf8Bom_IsStripped()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("あ\nい")).ToArray();
        var path = Write("bom.txt", bytes);

        Assert.Equal(new[] { "あ", "い" }, DiffSource.Read(path).Lines);
    }

    [Fact]
    public void Read_ShiftJisWithoutBom_IsDecodedCorrectly()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding(932);
        var path = Write("sjis.txt", sjis.GetBytes("日本語\nテスト"));

        var content = DiffSource.Read(path);

        Assert.Equal(DiffContentKind.Text, content.Kind);
        Assert.Equal(new[] { "日本語", "テスト" }, content.Lines);
    }

    [Fact]
    public void Read_NulBytes_DetectedAsBinary()
    {
        var path = Write("bin.dat", new byte[] { 0x41, 0x00, 0x42, 0x00 });

        var content = DiffSource.Read(path);

        Assert.Equal(DiffContentKind.Binary, content.Kind);
        Assert.Empty(content.Lines);
    }

    [Fact]
    public void Read_EmptyFile_NoLines()
    {
        var path = Write("empty.txt", Array.Empty<byte>());

        var content = DiffSource.Read(path);

        Assert.Equal(DiffContentKind.Text, content.Kind);
        Assert.Empty(content.Lines);
    }

    [Fact]
    public void Read_OverSizeLimit_ReturnsTooLarge()
    {
        var path = Write("big.txt", Encoding.UTF8.GetBytes("0123456789"));   // 10 バイト

        var content = DiffSource.Read(path, maxBytes: 5);

        Assert.Equal(DiffContentKind.TooLarge, content.Kind);
        Assert.Empty(content.Lines);
    }

    [Fact]
    public void Read_AtSizeLimit_IsRead()
    {
        var path = Write("ok.txt", Encoding.UTF8.GetBytes("abcde"));   // 5 バイト

        Assert.Equal(DiffContentKind.Text, DiffSource.Read(path, maxBytes: 5).Kind);
    }
}
