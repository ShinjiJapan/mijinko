namespace Filer.Core;

/// <summary>
/// 高解像度画像を表示サイズへ縮小する際の段階数(1/2縮小を何回行うか)を求める。
/// スクリーントーン等の高周波パターンは、表示時の単純な間引き縮小だとパターン周期と干渉してモアレが出る。
/// 表示寸付近まで 2:1 の面積平均で段階的に縮めて(=ローパス)から最終縮小すると、モアレを抑えられる。
/// </summary>
public static class ImageDownscale
{
    /// <summary>
    /// 元寸 (srcW×srcH) を 表示寸 (dstW×dstH) へ収めるための 1/2 縮小回数を返す。
    /// 「半分にしても両辺が表示寸以上」である限り半分にし続け、表示寸を下回る手前で止める。
    /// 残りの端数縮小は描画側の高品質補間(Fant)に委ねる。
    /// 表示寸が 0 以下(レイアウト未確定)、または元が表示寸以下なら 0。
    /// </summary>
    public static int HalvingSteps(double srcW, double srcH, double dstW, double dstH)
    {
        if (dstW <= 0 || dstH <= 0) return 0;
        var steps = 0;
        var w = srcW;
        var h = srcH;
        while (w / 2 >= dstW && h / 2 >= dstH)
        {
            w /= 2;
            h /= 2;
            steps++;
        }
        return steps;
    }
}
