using System.Text;

namespace Filer.Core;

/// <summary>行内差分の区間1つ。<see cref="Changed"/> が true なら相手側に無い(変わった)文字列。</summary>
/// <param name="Text">区間の文字列。</param>
/// <param name="Changed">変更箇所か(true=強調対象)。</param>
public sealed record InlineSegment(string Text, bool Changed);

/// <summary>
/// 変更行の左右の文字列を文字単位で比較し、共通部分と変更部分の区間列に分ける(UI 非依存)。
/// side-by-side の変更行で「実際に変わった文字だけ」を強調するために使う。
/// 行が極端に長い場合は O(n*m) を避けて全体を変更扱いにフォールバックする。
/// </summary>
public static class InlineDiff
{
    // 文字単位 LCS の DP 表サイズ上限(左長×右長)。超過時は丸ごと変更扱い。
    private const int MaxProduct = 1_000_000;

    /// <summary>左右の文字列を文字単位で比較し、(左の区間列, 右の区間列) を返す。</summary>
    public static (IReadOnlyList<InlineSegment> Left, IReadOnlyList<InlineSegment> Right) Compute(
        string left, string right)
    {
        if ((long)left.Length * right.Length > MaxProduct)
            return (WholeChanged(left), WholeChanged(right));

        var lcs = LcsTable(left, right);
        var leftSegs = new SegmentBuilder();
        var rightSegs = new SegmentBuilder();

        int i = 0, j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (left[i] == right[j])
            {
                leftSegs.Append(left[i], changed: false);
                rightSegs.Append(right[j], changed: false);
                i++; j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                leftSegs.Append(left[i], changed: true);   // 左にのみ=削除された文字
                i++;
            }
            else
            {
                rightSegs.Append(right[j], changed: true);  // 右にのみ=追加された文字
                j++;
            }
        }
        while (i < left.Length) leftSegs.Append(left[i++], changed: true);
        while (j < right.Length) rightSegs.Append(right[j++], changed: true);

        return (leftSegs.Build(), rightSegs.Build());
    }

    /// <summary>文字列全体を1つの変更区間にする(空文字列は区間なし)。</summary>
    private static IReadOnlyList<InlineSegment> WholeChanged(string s) =>
        s.Length == 0 ? Array.Empty<InlineSegment>() : new[] { new InlineSegment(s, true) };

    /// <summary>LCS 長の DP 表(末尾基準)。lcs[i,j] = left[i..], right[j..] の最長共通部分列長。</summary>
    private static int[,] LcsTable(string left, string right)
    {
        var n = left.Length;
        var m = right.Length;
        var lcs = new int[n + 1, m + 1];
        for (var a = n - 1; a >= 0; a--)
            for (var b = m - 1; b >= 0; b--)
                lcs[a, b] = left[a] == right[b]
                    ? lcs[a + 1, b + 1] + 1
                    : Math.Max(lcs[a + 1, b], lcs[a, b + 1]);
        return lcs;
    }

    /// <summary>同じ変更フラグの連続文字を1区間にまとめる。</summary>
    private sealed class SegmentBuilder
    {
        private readonly List<InlineSegment> _segments = new();
        private readonly StringBuilder _buffer = new();
        private bool _changed;

        public void Append(char c, bool changed)
        {
            if (_buffer.Length > 0 && changed != _changed)
                Flush();
            _changed = changed;
            _buffer.Append(c);
        }

        public IReadOnlyList<InlineSegment> Build()
        {
            Flush();
            return _segments;
        }

        private void Flush()
        {
            if (_buffer.Length == 0) return;
            _segments.Add(new InlineSegment(_buffer.ToString(), _changed));
            _buffer.Clear();
        }
    }
}
