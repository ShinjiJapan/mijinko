using System.IO;
using System.Text;
using Filer.Core;

namespace Filer.Core.Tests;

public sealed class TextFileIoTests
{
    private static string TempFile()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Path.Combine(Path.GetTempPath(), $"filer-textio-{Guid.NewGuid():N}.txt");
    }

    [Fact]
    public void Read_Utf8NoBom_DetectsUtf8WithoutBom()
    {
        var path = TempFile();
        try
        {
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes("あいう\nabc"));
            var content = TextFileIo.Read(path);

            Assert.Equal("あいう\nabc", content.Text);
            Assert.Equal(Encoding.UTF8.CodePage, content.Encoding.CodePage);
            Assert.False(content.HasBom);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_Utf8WithBom_StripsBomFromText()
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, "héllo", new UTF8Encoding(true));   // 先頭に BOM を出力する
            var content = TextFileIo.Read(path);

            Assert.Equal("héllo", content.Text);          // 先頭に U+FEFF を含まない
            Assert.True(content.HasBom);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_ShiftJis_DetectsCp932()
    {
        var path = TempFile();
        try
        {
            File.WriteAllBytes(path, TextEncodingDetector.ShiftJis.GetBytes("日本語テキスト"));
            var content = TextFileIo.Read(path);

            Assert.Equal("日本語テキスト", content.Text);
            Assert.Equal(932, content.Encoding.CodePage);
            Assert.False(content.HasBom);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Write_Utf8_RoundTripsAndPreservesBom(bool hasBom)
    {
        var path = TempFile();
        try
        {
            TextFileIo.Write(path, "あ\nb", Encoding.UTF8, hasBom);

            var bytes = File.ReadAllBytes(path);
            var startsWithBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Assert.Equal(hasBom, startsWithBom);

            var reread = TextFileIo.Read(path);
            Assert.Equal("あ\nb", reread.Text);
            Assert.Equal(hasBom, reread.HasBom);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Write_ShiftJis_PreservesEncoding()
    {
        var path = TempFile();
        try
        {
            TextFileIo.Write(path, "保存テスト", TextEncodingDetector.ShiftJis, hasBom: false);

            var reread = TextFileIo.Read(path);
            Assert.Equal("保存テスト", reread.Text);
            Assert.Equal(932, reread.Encoding.CodePage);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_EmptyFile_ReturnsEmptyText()
    {
        var path = TempFile();
        try
        {
            File.WriteAllBytes(path, Array.Empty<byte>());
            var content = TextFileIo.Read(path);

            Assert.Equal("", content.Text);
            Assert.False(content.HasBom);
        }
        finally { File.Delete(path); }
    }
}
