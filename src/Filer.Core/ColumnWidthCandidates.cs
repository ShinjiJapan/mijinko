namespace Filer.Core;

/// <summary>
/// 一覧の Auto 幅カラム調整で「実測すべき候補文字列」を選ぶ。
/// 全行を FormattedText で実測すると数万件で数百 ms かかるため、
/// 表示幅スコア(半角=1・全角等=2)の上位だけを候補として返し、実測をその数件に抑える。
/// </summary>
public static class ColumnWidthCandidates
{
    /// <summary>表示幅スコア上位 count 件の文字列(重複・null・空は除く)を返す。</summary>
    public static IReadOnlyList<string> Select(IEnumerable<string?> values, int count = 3)
    {
        // (score, string) の上位 count 件を保持する小さな挿入ソートリスト。
        var top = new List<(int Score, string Value)>(count + 1);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value)) continue;
            var score = ScoreOf(value);
            if (top.Count == count && score <= top[^1].Score) continue;
            if (!seen.Add(value)) continue;

            var index = top.FindIndex(t => score > t.Score);
            top.Insert(index < 0 ? top.Count : index, (score, value));
            if (top.Count > count)
            {
                seen.Remove(top[^1].Value);
                top.RemoveAt(top.Count - 1);
            }
        }
        return top.Select(t => t.Value).ToList();
    }

    /// <summary>表示幅の近似スコア。ASCII=1、それ以外(全角含む)=2。</summary>
    private static int ScoreOf(string value)
    {
        var score = 0;
        foreach (var c in value)
            score += c <= 0x7F ? 1 : 2;
        return score;
    }
}
