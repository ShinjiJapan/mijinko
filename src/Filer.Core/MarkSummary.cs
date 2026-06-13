namespace Filer.Core;

/// <summary>パンくず右端に出すマーク集計の整形(「marked 12/560 4.5MB」形式)。</summary>
public static class MarkSummary
{
    /// <summary>マーク数/総項目数 と マークしたファイルの合計サイズを「marked m/t size」へ整形する。</summary>
    public static string Format(int markedCount, int totalCount, long markedBytes) =>
        $"marked {markedCount}/{totalCount} {FormatSize(markedBytes)}";

    /// <summary>バイト数を簡潔な単位付き文字列へ(例: 4.5MB)。単位の前に空白は入れない。</summary>
    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes}{units[unit]}" : $"{size:0.#}{units[unit]}";
    }
}
