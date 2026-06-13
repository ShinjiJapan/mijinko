using System.IO;

namespace Filer.Core;

/// <summary>差分対象ファイルの種別。</summary>
public enum DiffContentKind
{
    /// <summary>テキスト(行差分の対象)。</summary>
    Text,

    /// <summary>バイナリ(NUL を含む。行差分は不可)。</summary>
    Binary,

    /// <summary>サイズ上限を超過(読み込まない)。</summary>
    TooLarge,
}

/// <summary>差分対象として読み込んだファイルの内容。<see cref="Lines"/> はテキスト時のみ非空。</summary>
public sealed record DiffFileContent(DiffContentKind Kind, string[] Lines);

/// <summary>
/// 差分対象ファイルをテキスト行へ読み込む(UI 非依存)。文字コードは <see cref="TextEncodingDetector"/>
/// で判定(BOM 自動判定、BOM 無しは UTF-8 妥当なら UTF-8・でなければ Shift-JIS)。NUL を含むものは
/// バイナリ、上限超過のものは読み込まずに種別だけ返す。
/// </summary>
public static class DiffSource
{
    /// <summary>差分対象として読み込むファイルサイズの既定上限(10MB)。</summary>
    public const long DefaultMaxBytes = 10L * 1024 * 1024;

    /// <summary>
    /// ファイルを読み、種別と行配列を返す。<paramref name="maxBytes"/> を超えるファイルは
    /// <see cref="DiffContentKind.TooLarge"/>、NUL を含むファイルは <see cref="DiffContentKind.Binary"/>。
    /// 改行は CRLF / LF / CR で分割し、末尾改行による空行は付けない。
    /// </summary>
    public static DiffFileContent Read(string path, long maxBytes = DefaultMaxBytes)
    {
        if (new FileInfo(path).Length > maxBytes)
            return new DiffFileContent(DiffContentKind.TooLarge, Array.Empty<string>());

        var bytes = File.ReadAllBytes(path);
        ReadOnlySpan<byte> sample = bytes.Length <= TextEncodingDetector.SampleSize
            ? bytes
            : bytes.AsSpan(0, TextEncodingDetector.SampleSize);

        if (TextEncodingDetector.IsBinary(sample))
            return new DiffFileContent(DiffContentKind.Binary, Array.Empty<string>());

        var encoding = TextEncodingDetector.Detect(sample);
        string text;
        using (var stream = new MemoryStream(bytes))
        using (var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true))
            text = reader.ReadToEnd();

        return new DiffFileContent(DiffContentKind.Text, SplitLines(text));
    }

    /// <summary>CRLF / LF / CR で分割する。末尾の改行による空要素は除く。空文字列は 0 行。</summary>
    private static string[] SplitLines(string text)
    {
        if (text.Length == 0) return Array.Empty<string>();

        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                lines.Add(text.Substring(start, i - start));
                start = i + 1;
            }
            else if (c == '\r')
            {
                lines.Add(text.Substring(start, i - start));
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;   // CRLF をまとめて1改行扱い
                start = i + 1;
            }
        }
        if (start < text.Length)
            lines.Add(text.Substring(start));   // 末尾に改行が無い最終行
        return lines.ToArray();
    }
}
