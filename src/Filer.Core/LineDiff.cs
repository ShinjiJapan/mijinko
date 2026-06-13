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
/// 行単位の差分を計算する(UI 非依存)。共通の先頭・末尾行をトリムしてから中央のみ LCS 差分し、
/// 削除直後の追加を行ごとに対にして <see cref="DiffRowKind.Modified"/> へ畳む(side-by-side で左右が揃う)。
/// 中央の行数の積が <see cref="MaxProduct"/> を超える場合は O(n*m) のメモリ・時間爆発を避けるため
/// 全置換(全行を変更扱い)へフォールバックする。
/// </summary>
public static class LineDiff
{
    // 中央差分で LCS 表(int[n+1,m+1])を作る上限(左中央長×右中央長)。約2000×2000=16MBに相当。
    private const long MaxProduct = 4_000_000;

    /// <summary>左右の行列から side-by-side 用の行列を生成する。</summary>
    public static IReadOnlyList<DiffRow> Compute(
        IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var n = left.Count;
        var m = right.Count;

        // 共通の先頭行をトリムする。
        var prefix = 0;
        while (prefix < n && prefix < m && left[prefix] == right[prefix])
            prefix++;

        // 共通の末尾行をトリムする(prefix と重ならない範囲で)。
        var suffix = 0;
        while (suffix < n - prefix && suffix < m - prefix &&
               left[n - 1 - suffix] == right[m - 1 - suffix])
            suffix++;

        var raw = new List<DiffRow>(n + m);

        for (var i = 0; i < prefix; i++)
            raw.Add(new DiffRow(DiffRowKind.Equal, i + 1, left[i], i + 1, right[i]));

        AppendMiddle(raw, left, right, prefix, n - suffix - prefix, m - suffix - prefix);

        for (var k = 0; k < suffix; k++)
        {
            var li = n - suffix + k;
            var ri = m - suffix + k;
            raw.Add(new DiffRow(DiffRowKind.Equal, li + 1, left[li], ri + 1, right[ri]));
        }

        return FoldModified(raw);
    }

    /// <summary>中央(トリム後)の差分行を <paramref name="raw"/> へ追加する。行番号はオフセット込み。</summary>
    private static void AppendMiddle(
        List<DiffRow> raw, IReadOnlyList<string> left, IReadOnlyList<string> right,
        int offset, int leftLen, int rightLen)
    {
        if (leftLen == 0 && rightLen == 0) return;

        if ((long)leftLen * rightLen <= MaxProduct)
            AppendLcsMiddle(raw, left, right, offset, leftLen, rightLen);
        else
            AppendReplaceMiddle(raw, left, right, offset, leftLen, rightLen);
    }

    /// <summary>中央を LCS で Equal/Deleted/Inserted へ分解して追加する。</summary>
    private static void AppendLcsMiddle(
        List<DiffRow> raw, IReadOnlyList<string> left, IReadOnlyList<string> right,
        int offset, int leftLen, int rightLen)
    {
        var lcs = LcsTable(left, right, offset, leftLen, rightLen);

        int i = 0, j = 0;
        while (i < leftLen && j < rightLen)
        {
            if (left[offset + i] == right[offset + j])
            {
                raw.Add(new DiffRow(DiffRowKind.Equal,
                    offset + i + 1, left[offset + i], offset + j + 1, right[offset + j]));
                i++; j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                raw.Add(new DiffRow(DiffRowKind.Deleted, offset + i + 1, left[offset + i], null, null));
                i++;
            }
            else
            {
                raw.Add(new DiffRow(DiffRowKind.Inserted, null, null, offset + j + 1, right[offset + j]));
                j++;
            }
        }
        while (i < leftLen)
        {
            raw.Add(new DiffRow(DiffRowKind.Deleted, offset + i + 1, left[offset + i], null, null));
            i++;
        }
        while (j < rightLen)
        {
            raw.Add(new DiffRow(DiffRowKind.Inserted, null, null, offset + j + 1, right[offset + j]));
            j++;
        }
    }

    /// <summary>中央を全置換扱いにする(左中央を全削除→右中央を全追加。後段で Modified へ畳まれる)。</summary>
    private static void AppendReplaceMiddle(
        List<DiffRow> raw, IReadOnlyList<string> left, IReadOnlyList<string> right,
        int offset, int leftLen, int rightLen)
    {
        for (var i = 0; i < leftLen; i++)
            raw.Add(new DiffRow(DiffRowKind.Deleted, offset + i + 1, left[offset + i], null, null));
        for (var j = 0; j < rightLen; j++)
            raw.Add(new DiffRow(DiffRowKind.Inserted, null, null, offset + j + 1, right[offset + j]));
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

    /// <summary>
    /// 中央部分の LCS 長 DP 表(末尾基準)。lcs[i,j] = left[offset+i..], right[offset+j..](各中央長まで)の
    /// 最長共通部分列長。添字 i/j は中央内の相対位置。
    /// </summary>
    private static int[,] LcsTable(
        IReadOnlyList<string> left, IReadOnlyList<string> right,
        int offset, int leftLen, int rightLen)
    {
        var lcs = new int[leftLen + 1, rightLen + 1];
        for (var a = leftLen - 1; a >= 0; a--)
            for (var b = rightLen - 1; b >= 0; b--)
                lcs[a, b] = left[offset + a] == right[offset + b]
                    ? lcs[a + 1, b + 1] + 1
                    : Math.Max(lcs[a + 1, b], lcs[a, b + 1]);
        return lcs;
    }
}
