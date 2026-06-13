using System.Text;
using Filer.Core;

namespace Filer.Core.Tests;

public class TextEncodingDetectorTests
{
    [Fact]
    public void IsBinary_NulByte_True()
    {
        Assert.True(TextEncodingDetector.IsBinary(new byte[] { 0x41, 0x00, 0x42 }));
    }

    [Fact]
    public void IsBinary_PlainAscii_False()
    {
        Assert.False(TextEncodingDetector.IsBinary("hello"u8.ToArray()));
    }

    [Fact]
    public void IsBinary_Utf16BomWithNul_TreatedAsText()
    {
        // UTF-16 LE BOM + ASCII は NUL を含むがテキスト。
        var bytes = new byte[] { 0xFF, 0xFE, 0x41, 0x00 };
        Assert.False(TextEncodingDetector.IsBinary(bytes));
    }

    [Fact]
    public void Detect_Utf8Bom_ReturnsUtf8()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, 0x41 };
        Assert.Equal(Encoding.UTF8, TextEncodingDetector.Detect(bytes));
    }

    [Fact]
    public void Detect_ValidUtf8NoBom_ReturnsUtf8()
    {
        Assert.Equal(Encoding.UTF8, TextEncodingDetector.Detect("日本語"u8.ToArray()));
    }

    [Fact]
    public void Detect_ShiftJisBytes_ReturnsShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sjis = Encoding.GetEncoding(932).GetBytes("日本語");

        Assert.Equal(932, TextEncodingDetector.Detect(sjis).CodePage);
    }
}
