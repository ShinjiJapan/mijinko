namespace Filer.Core;

/// <summary>
/// インクリメンタルサーチ。一覧のエントリ名をクエリで検索し、一致した位置(インデックス)を返す。
/// 大文字小文字は区別せず、親 ".." は一致対象にしない。UI 非依存でテスト可能。
/// </summary>
public static class IncrementalSearch
{
    /// <summary>
    /// startIndex から下方向(startIndex 自身を含む)へ最初の一致を探す。
    /// 末尾まで一致が無ければ先頭へ回り込む。一致なし・空クエリは -1。
    /// クエリ変更のたびに「検索開始時のカーソル位置」を起点に呼ぶ。
    /// </summary>
    public static int FindFrom(IReadOnlyList<FileEntry> entries, string query, bool prefixOnly, int startIndex)
    {
        if (entries.Count == 0 || string.IsNullOrWhiteSpace(query))
            return -1;

        var start = Math.Clamp(startIndex, 0, entries.Count - 1);
        for (var offset = 0; offset < entries.Count; offset++)
        {
            var i = (start + offset) % entries.Count;
            if (Matches(entries[i], query, prefixOnly))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// currentIndex の次(backward なら前)から一致を探す。currentIndex 自身は対象に含めず、
    /// 端で回り込む(一致が1件だけなら一周して同じ位置を返す)。一致なし・空クエリは -1。
    /// </summary>
    public static int FindNext(IReadOnlyList<FileEntry> entries, string query, bool prefixOnly, int currentIndex, bool backward)
    {
        if (entries.Count == 0 || string.IsNullOrWhiteSpace(query))
            return -1;

        var n = entries.Count;
        var step = backward ? -1 : 1;
        var current = Math.Clamp(currentIndex, 0, n - 1);
        for (var offset = 1; offset <= n; offset++)
        {
            var i = ((current + step * offset) % n + n) % n;
            if (Matches(entries[i], query, prefixOnly))
                return i;
        }
        return -1;
    }

    private static bool Matches(FileEntry entry, string query, bool prefixOnly)
    {
        if (entry.IsParent)
            return false;
        return prefixOnly
            ? entry.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
            : entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
