namespace Filer.Core;

/// <summary>サムネイル(グリッド)表示のタイルサイズ。</summary>
public enum GridTileSize
{
    /// <summary>通常サイズ。既定。</summary>
    Normal,

    /// <summary>拡大サイズ(通常の約2倍)。</summary>
    Large,
}

/// <summary>
/// グリッド表示のタイル寸法。XAML のバインドと MainWindow の列数計算で同じ値を使うため一元化する。
/// </summary>
public static class GridTileMetrics
{
    /// <summary>タイル(画像+名前を載せる箱)の幅(px)。Large は Normal の2倍。</summary>
    public static double TileWidth(GridTileSize size) => size == GridTileSize.Large ? 192 : 96;

    /// <summary>タイル内の画像の一辺(px)。Large は Normal の2倍。</summary>
    public static double ImageSize(GridTileSize size) => size == GridTileSize.Large ? 160 : 80;

    // タイル外形に加わる余白。XAML(GridTileTemplate / GridItemStyle)の実装と一致させること。
    /// <summary>幅の chrome(GridItemStyle 余白3*2 + 枠1*2 + パディング4*2 = 16)。</summary>
    public const double CellChromeWidth = 16;
    /// <summary>高さの chrome(画像上下余白6 + 名前欄32 + 余白/枠/パディング16 = 54)。</summary>
    public const double CellChromeHeight = 6 + 32 + 16;

    /// <summary>タイル外形(コンテナ1個ぶん)の幅(px)。仮想化グリッドの列計算・配置に使う。</summary>
    public static double CellWidth(GridTileSize size) => TileWidth(size) + CellChromeWidth;

    /// <summary>タイル外形(コンテナ1個ぶん)の高さ(px)。仮想化グリッドの行計算・配置に使う。</summary>
    public static double CellHeight(GridTileSize size) => ImageSize(size) + CellChromeHeight;

    /// <summary>次のサイズ(通常 ⇔ 拡大のトグル)。</summary>
    public static GridTileSize Next(GridTileSize size) =>
        size == GridTileSize.Large ? GridTileSize.Normal : GridTileSize.Large;
}
