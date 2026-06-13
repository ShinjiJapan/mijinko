namespace Filer.Core;

/// <summary>side-by-side 差分の行種別。</summary>
public enum DiffRowKind
{
    /// <summary>左右で一致する行。</summary>
    Equal,

    /// <summary>左右が対応する変更行(左=旧/右=新)。</summary>
    Modified,

    /// <summary>左にのみ存在(削除された行)。</summary>
    Deleted,

    /// <summary>右にのみ存在(追加された行)。</summary>
    Inserted,
}

/// <summary>
/// side-by-side 表示用の1行。番号は1始まり。存在しない側は番号・本文とも null。
/// </summary>
/// <param name="Kind">行種別。</param>
/// <param name="LeftNo">左の行番号(無ければ null)。</param>
/// <param name="LeftText">左の本文(無ければ null)。</param>
/// <param name="RightNo">右の行番号(無ければ null)。</param>
/// <param name="RightText">右の本文(無ければ null)。</param>
public sealed record DiffRow(
    DiffRowKind Kind, int? LeftNo, string? LeftText, int? RightNo, string? RightText);

/// <summary>
/// 行単位の差分を計算する(UI 非依存)。LCS で一致列を求め、削除直後の追加を行ごとに対にして
/// <see cref="DiffRowKind.Modified"/> へ畳むことで、side-by-side 表示で左右の変更行が揃う。
/// </summary>
public static class LineDiff
{
    /// <summary>左右の行列から side-by-side 用の行列を生成する。</summary>
    public static IReadOnlyList<DiffRow> Compute(
        IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var lcs = LcsTable(left, right);

        // まず Equal / Deleted / Inserted の素の列を作る(行番号付き)。
        var raw = new List<DiffRow>(left.Count + right.Count);
        int i = 0, j = 0;
        while (i < left.Count && j < right.Count)
        {
            if (left[i] == right[j])
            {
                raw.Add(new DiffRow(DiffRowKind.Equal, i + 1, left[i], j + 1, right[j]));
                i++; j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                raw.Add(new DiffRow(DiffRowKind.Deleted, i + 1, left[i], null, null));
                i++;
            }
            else
            {
                raw.Add(new DiffRow(DiffRowKind.Inserted, null, null, j + 1, right[j]));
                j++;
            }
        }
        while (i < left.Count) { raw.Add(new DiffRow(DiffRowKind.Deleted, i + 1, left[i], null, null)); i++; }
        while (j < right.Count) { raw.Add(new DiffRow(DiffRowKind.Inserted, null, null, j + 1, right[j])); j++; }

        return FoldModified(raw);
    }

    /// <summary>連続する Deleted の直後に連続する Inserted があれば、行ごとに対にして Modified へ畳む。</summary>
    private static List<DiffRow> FoldModified(List<DiffRow> raw)
    {
        var result = new List<DiffRow>(raw.Count);
        var k = 0;
        while (k < raw.Count)
        {
            if (raw[k].Kind != DiffRowKind.Deleted)
            {
                result.Add(raw[k]);
                k++;
                continue;
            }

            // 連続する Deleted 群と、それに続く連続する Inserted 群の範囲を取る。
            var delStart = k;
            while (k < raw.Count && raw[k].Kind == DiffRowKind.Deleted) k++;
            var insStart = k;
            while (k < raw.Count && raw[k].Kind == DiffRowKind.Inserted) k++;

            var dels = raw.GetRange(delStart, insStart - delStart);
            var inss = raw.GetRange(insStart, k - insStart);
            var pairs = Math.Min(dels.Count, inss.Count);

            for (var p = 0; p < pairs; p++)
                result.Add(new DiffRow(DiffRowKind.Modified,
                    dels[p].LeftNo, dels[p].LeftText, inss[p].RightNo, inss[p].RightText));
            for (var p = pairs; p < dels.Count; p++) result.Add(dels[p]);
            for (var p = pairs; p < inss.Count; p++) result.Add(inss[p]);
        }
        return result;
    }

    /// <summary>LCS 長の DP 表(末尾基準)。lcs[i,j] = left[i..], right[j..] の最長共通部分列長。</summary>
    private static int[,] LcsTable(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var n = left.Count;
        var m = right.Count;
        var lcs = new int[n + 1, m + 1];
        for (var a = n - 1; a >= 0; a--)
            for (var b = m - 1; b >= 0; b--)
                lcs[a, b] = left[a] == right[b]
                    ? lcs[a + 1, b + 1] + 1
                    : Math.Max(lcs[a + 1, b], lcs[a, b + 1]);
        return lcs;
    }
}
