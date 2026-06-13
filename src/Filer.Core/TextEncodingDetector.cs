using System.Text;

namespace Filer.Core;

/// <summary>
/// 先頭バイト列からテキスト/バイナリ判定と文字コード判定を行う(UI 非依存)。
/// BOM があればそれに従い、無ければ UTF-8 として妥当なら UTF-8、そうでなければ Shift-JIS
/// (日本語 Windows の慣例)とみなす。内容検索(grep)と差分表示で共有する。
/// </summary>
public static class TextEncodingDetector
{
    /// <summary>バイナリ・エンコーディング判定に使う先頭サンプルの推奨バイト数。</summary>
    public const int SampleSize = 64 * 1024;

    /// <summary>BOM 無しの非 UTF-8 ファイルの既定エンコーディング(日本語 Windows の Shift-JIS)。</summary>
    private static readonly Encoding ShiftJisEncoding = CreateFallbackEncoding();

    private static Encoding CreateFallbackEncoding()
    {
        // .NET(Core 以降)は既定で Shift-JIS 等のコードページを持たないため明示登録する。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);   // Shift-JIS(Windows-31J)
    }

    /// <summary>Shift-JIS(CP932)エンコーディング。</summary>
    public static Encoding ShiftJis => ShiftJisEncoding;

    /// <summary>
    /// 先頭サンプルからバイナリ(NUL を含む)かどうかを判定する。
    /// テキストの BOM(UTF-8/UTF-16/UTF-32)がある場合はテキストとして扱う(ASCII でも NUL が出るため)。
    /// </summary>
    public static bool IsBinary(ReadOnlySpan<byte> sample)
    {
        if (HasTextBom(sample)) return false;
        return sample.IndexOf((byte)0) >= 0;
    }

    /// <summary>
    /// 先頭サンプルからエンコーディングを判定する。BOM があればそれに従い、無ければ
    /// UTF-8 として妥当なら UTF-8、そうでなければ Shift-JIS とみなす。
    /// </summary>
    public static Encoding Detect(ReadOnlySpan<byte> sample)
    {
        if (sample.Length >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF)
            return Encoding.UTF8;
        if (sample.Length >= 4 && sample[0] == 0xFF && sample[1] == 0xFE && sample[2] == 0x00 && sample[3] == 0x00)
            return Encoding.UTF32;
        if (sample.Length >= 2 && sample[0] == 0xFF && sample[1] == 0xFE)
            return Encoding.Unicode;            // UTF-16 LE
        if (sample.Length >= 2 && sample[0] == 0xFE && sample[1] == 0xFF)
            return Encoding.BigEndianUnicode;   // UTF-16 BE
        return IsValidUtf8(sample) ? Encoding.UTF8 : ShiftJisEncoding;
    }

    private static bool HasTextBom(ReadOnlySpan<byte> b) =>
        (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) ||   // UTF-8
        (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) ||                   // UTF-16/UTF-32 LE
        (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF);                     // UTF-16 BE

    /// <summary>
    /// バイト列が UTF-8 として妥当かを判定する。サンプル末尾でマルチバイト列が途中で切れている場合は、
    /// 切り詰めによる誤判定を避けるため妥当とみなす。純 ASCII は UTF-8 として妥当(true)。
    /// </summary>
    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        var i = 0;
        while (i < bytes.Length)
        {
            var b = bytes[i];
            int continuation;
            if (b <= 0x7F) { i++; continue; }            // ASCII
            else if (b is >= 0xC2 and <= 0xDF) continuation = 1;
            else if (b is >= 0xE0 and <= 0xEF) continuation = 2;
            else if (b is >= 0xF0 and <= 0xF4) continuation = 3;
            else return false;                            // 不正な先頭バイト(0x80..0xC1, 0xF5..0xFF)

            if (i + continuation >= bytes.Length) return true;   // サンプル末尾で途中切れ → 妥当扱い

            for (var k = 1; k <= continuation; k++)
                if ((bytes[i + k] & 0xC0) != 0x80) return false;  // 継続バイトは 10xxxxxx

            i += continuation + 1;
        }
        return true;
    }
}
