namespace Filer.Core;

/// <summary>
/// グリッド(サムネイル)表示の仮想化レイアウト計算。UI 非依存にして単体テストする。
/// タイルは全て同寸である前提・縦スクロール専用。VirtualizingWrapPanel(App 側)が利用する。
/// </summary>
public static class GridVirtualization
{
    /// <summary>ビューポート幅に収まる列数(最低1)。</summary>
    public static int Columns(double viewportWidth, double itemWidth)
    {
        if (itemWidth <= 0) return 1;
        return Math.Max(1, (int)(viewportWidth / itemWidth));
    }

    /// <summary>総行数(itemCount を columns で折り返したときの行数)。</summary>
    public static int RowCount(int itemCount, int columns)
    {
        if (itemCount <= 0) return 0;
        columns = Math.Max(1, columns);
        return (itemCount + columns - 1) / columns;
    }

    /// <summary>全体の高さ(行数 × タイル高さ)。</summary>
    public static double ExtentHeight(int itemCount, int columns, double itemHeight) =>
        RowCount(itemCount, columns) * itemHeight;

    /// <summary>
    /// 縦オフセット <paramref name="offsetY"/>・高さ <paramref name="viewportHeight"/> のビューポートに
    /// 見えるアイテムの添字範囲 [First, Last](両端含む)。前後に <paramref name="bufferRows"/> 行ぶん
    /// 先読みを付ける。見える物が無ければ First>Last(空)を返す。
    /// </summary>
    public static (int First, int Last) VisibleRange(
        double offsetY, double viewportHeight, double itemHeight,
        int columns, int itemCount, int bufferRows = 1)
    {
        if (itemCount <= 0 || itemHeight <= 0) return (0, -1);
        columns = Math.Max(1, columns);

        int firstRow = Math.Max(0, (int)(offsetY / itemHeight) - bufferRows);
        // ビューポート下端(排他)に掛かる最後の行。境界ちょうどでは次の行を含めない。
        int lastRow = (int)Math.Ceiling((offsetY + viewportHeight) / itemHeight) - 1 + bufferRows;

        int first = firstRow * columns;
        int last = Math.Min(itemCount - 1, (lastRow + 1) * columns - 1);
        if (first > last) return (0, -1);
        return (first, last);
    }
}
