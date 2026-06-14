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

    /// <summary>次のサイズ(通常 ⇔ 拡大のトグル)。</summary>
    public static GridTileSize Next(GridTileSize size) =>
        size == GridTileSize.Large ? GridTileSize.Normal : GridTileSize.Large;
}
