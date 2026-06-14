namespace Filer.Core;

/// <summary>コマンドパレットに表示する1項目(UI 非依存)。GestureText は割り当てキーの表示文字列。</summary>
public sealed record CommandPaletteItem(string Id, string Title, string Category, string GestureText);

/// <summary>コマンドパレットの絞り込み・並べ替えロジック(UI 非依存)。</summary>
public static class CommandPaletteFilter
{
    /// <summary>
    /// クエリで項目を絞り込む。空クエリは全件を元の順序で返す。
    /// 空白区切りの各トークンがすべて Title/Category/Id のいずれかに含まれる項目だけを残し、
    /// Title への一致が良い順(前方一致 &gt; 部分一致 &gt; その他)に並べる。同点は元の順序を保つ。
    /// 大文字小文字は区別しない。
    /// </summary>
    public static IReadOnlyList<CommandPaletteItem> Filter(
        IEnumerable<CommandPaletteItem> items, string query)
    {
        var list = items as IReadOnlyList<CommandPaletteItem> ?? items.ToList();
        var tokens = (query ?? "").Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return list;

        var matched = new List<(CommandPaletteItem Item, int Score, int Order)>();
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (TryScore(item, tokens, out var score))
                matched.Add((item, score, i));
        }

        matched.Sort((a, b) => a.Score != b.Score ? a.Score - b.Score : a.Order - b.Order);
        return matched.Select(m => m.Item).ToList();
    }

    /// <summary>全トークンが一致すれば true(score 小=上位)。1つでも外れたら false。</summary>
    private static bool TryScore(CommandPaletteItem item, string[] tokens, out int score)
    {
        const StringComparison Cmp = StringComparison.OrdinalIgnoreCase;
        var haystack = item.Title + "\n" + item.Category + "\n" + item.Id;
        score = int.MaxValue;
        var best = 3;
        foreach (var token in tokens)
        {
            if (haystack.IndexOf(token, Cmp) < 0)
                return false;
            // Title への当たり方で順位付け(トークン内の最良を採用)。
            var rank =
                item.Title.StartsWith(token, Cmp) ? 0 :
                item.Title.IndexOf(token, Cmp) >= 0 ? 1 :
                2;
            if (rank < best) best = rank;
        }
        score = best;
        return true;
    }
}
