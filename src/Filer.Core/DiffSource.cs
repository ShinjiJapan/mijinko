using System.IO;
using System.Text;

namespace Filer.Core;

/// <summary>
/// 差分対象ファイルをテキスト行へ読み込む(UI 非依存)。BOM 検出付きで文字コードを判定し、
/// NUL バイトを含むファイルはバイナリとみなして行分割しない。
/// </summary>
public static class DiffSource
{
    // バイナリ判定のために先読みするバイト数。
    private const int SniffBytes = 8192;

    /// <summary>
    /// ファイルを読み、(バイナリか, 行配列) を返す。バイナリなら行は空配列。
    /// 改行は CRLF / LF / CR のいずれでも分割し、末尾改行による空行は付けない。
    /// </summary>
    public static (bool IsBinary, string[] Lines) ReadLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (IsBinary(bytes))
            return (true, Array.Empty<string>());

        string text;
        using (var stream = new MemoryStream(bytes))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = reader.ReadToEnd();

        return (false, SplitLines(text));
    }

    /// <summary>先頭の一定範囲に NUL バイトがあればバイナリとみなす。</summary>
    private static bool IsBinary(byte[] bytes)
    {
        var n = Math.Min(bytes.Length, SniffBytes);
        for (var i = 0; i < n; i++)
            if (bytes[i] == 0)
                return true;
        return false;
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
