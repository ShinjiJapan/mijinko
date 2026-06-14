namespace Filer.Core;

/// <summary>ペインの一覧表示形式。</summary>
public enum PaneViewMode
{
    /// <summary>詳細表示(名前/拡張子/サイズ/更新日時の列)。既定。</summary>
    Details,

    /// <summary>サムネイル(グリッド)表示。</summary>
    Grid,
}
