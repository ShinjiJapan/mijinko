using System.IO;
using System.Text;

namespace Filer.Core;

/// <summary>
/// テキストファイルの読み書き(UI 非依存)。読み込み時に <see cref="TextEncodingDetector"/> で
/// 文字コードと BOM の有無を判定し、書き込み時に同じ文字コード・BOM を維持する。
/// アプリ内テキストエディターの保存で「開いたときの文字コードを保ったまま上書き」するために使う。
/// </summary>
public static class TextFileIo
{
    /// <summary>読み込んだ本文と、その文字コード・BOM の有無(上書き時に再現するため保持する)。</summary>
    public readonly record struct TextContent(string Text, Encoding Encoding, bool HasBom);

    /// <summary>ファイルを読み込み、本文と判定した文字コード・BOM の有無を返す(BOM は本文へ含めない)。</summary>
    public static TextContent Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var sampleLength = Math.Min(bytes.Length, TextEncodingDetector.SampleSize);
        var encoding = TextEncodingDetector.Detect(bytes.AsSpan(0, sampleLength));

        var preamble = encoding.GetPreamble();
        var hasBom = preamble.Length > 0
            && bytes.Length >= preamble.Length
            && bytes.AsSpan(0, preamble.Length).SequenceEqual(preamble);

        var start = hasBom ? preamble.Length : 0;
        var text = encoding.GetString(bytes, start, bytes.Length - start);
        return new TextContent(text, encoding, hasBom);
    }

    /// <summary>本文を指定の文字コード・BOM 有無で上書き保存する(<see cref="Read"/> が返した値を渡す)。</summary>
    public static void Write(string path, string text, Encoding encoding, bool hasBom)
    {
        File.WriteAllBytes(path, Encode(text, encoding, hasBom));
    }

    /// <summary>本文をバイト列へ符号化する。UTF-8 のみ BOM の有無を指定で切り替える(他は文字コード固有の BOM 規則に従う)。</summary>
    private static byte[] Encode(string text, Encoding encoding, bool hasBom)
    {
        // Encoding.UTF8 は常に BOM を出力するため、BOM 無しを維持できるよう作り直す。
        var enc = encoding.CodePage == Encoding.UTF8.CodePage
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom)
            : encoding;

        var preamble = enc.GetPreamble();
        var body = enc.GetBytes(text);
        if (preamble.Length == 0) return body;

        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }
}
