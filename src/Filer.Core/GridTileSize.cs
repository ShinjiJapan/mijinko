namespace Filer.Core;

/// <summary>サムネイル(グリッド)表示のタイルサイズ。小(既定)と大の2段階。</summary>
public enum GridTileSize
{
    /// <summary>小サイズ。既定。</summary>
    Normal,

    /// <summary>大サイズ(画像256px)。サムネイル取得上限と同じ大きさで等倍表示=鮮明。</summary>
    Large,
}

/// <summary>
/// グリッド表示のタイル寸法。XAML のバインドと MainWindow の列数計算で同じ値を使うため一元化する。
/// 画像は小80px・大256pxで、いずれもサムネイル取得サイズ(256px)以下=拡大せず鮮明に表示できる。
/// </summary>
public static class GridTileMetrics
{
    /// <summary>タイル(画像+名前を載せる箱)の幅(px)。画像の一辺 + 横 chrome。</summary>
    public static double TileWidth(GridTileSize size) => size switch
    {
        GridTileSize.Large => 272,
        _ => 96,
    };

    /// <summary>タイル内の画像の一辺(px)。小80・大256(取得サイズ256以下で等倍以下=鮮明)。</summary>
    public static double ImageSize(GridTileSize size) => size switch
    {
        GridTileSize.Large => 256,
        _ => 80,
    };

    // タイル外形に加わる余白。XAML(GridTileTemplate / GridItemStyle)の実装と一致させること。
    /// <summary>幅の chrome(GridItemStyle 余白3*2 + 枠1*2 + パディング4*2 = 16)。</summary>
    public const double CellChromeWidth = 16;
    /// <summary>高さの chrome(画像上下余白6 + 名前欄32 + 余白/枠/パディング16 = 54)。</summary>
    public const double CellChromeHeight = 6 + 32 + 16;

    /// <summary>タイル外形(コンテナ1個ぶん)の幅(px)。仮想化グリッドの列計算・配置に使う。</summary>
    public static double CellWidth(GridTileSize size) => TileWidth(size) + CellChromeWidth;

    /// <summary>タイル外形(コンテナ1個ぶん)の高さ(px)。仮想化グリッドの行計算・配置に使う。</summary>
    public static double CellHeight(GridTileSize size) => ImageSize(size) + CellChromeHeight;

    /// <summary>次のサイズ(小 ⇔ 大 を交互に切替)。</summary>
    public static GridTileSize Next(GridTileSize size) => size switch
    {
        GridTileSize.Normal => GridTileSize.Large,
        _ => GridTileSize.Normal,
    };
}
