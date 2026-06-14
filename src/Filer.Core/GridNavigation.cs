namespace Filer.Core;

/// <summary>グリッド(サムネイル)表示でのカーソル移動方向。</summary>
public enum GridDirection { Left, Right, Up, Down }

/// <summary>
/// グリッド(サムネイル)表示でのカーソル移動計算。UI 非依存でテスト可能。
/// 列数は表示幅から算出されるため呼び出し側が渡す。
/// </summary>
public static class GridNavigation
{
    /// <summary>
    /// 現在位置 <paramref name="index"/> から <paramref name="dir"/> 方向へ動いた先のインデックスを返す。
    /// 左右は端で反対側へ回り込む(一覧の ↑↓ と同じ感覚)。上下は行単位で移動し、行が無ければその場に留まる。
    /// </summary>
    /// <param name="count">項目数(".." を含む全件)。</param>
    /// <param name="columns">1行あたりの列数(1未満は1として扱う)。</param>
    /// <param name="index">現在のカーソル位置。</param>
    public static int Move(int count, int columns, int index, GridDirection dir)
    {
        if (count <= 0) return 0;
        if (columns < 1) columns = 1;
        index = Math.Clamp(index, 0, count - 1);

        return dir switch
        {
            // 左右は端で回り込む。
            GridDirection.Left => index > 0 ? index - 1 : count - 1,
            GridDirection.Right => index < count - 1 ? index + 1 : 0,
            // 上下は1行ぶん。移動先が範囲外なら留まる(グリッドの自然な挙動)。
            GridDirection.Up => index - columns >= 0 ? index - columns : index,
            GridDirection.Down => index + columns < count ? index + columns : index,
            _ => index,
        };
    }
}
