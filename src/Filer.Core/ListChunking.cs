namespace Filer.Core;

/// <summary>
/// 大量件数の一覧表示を「先頭チャンクを即表示 → 残りを直後に一括追加」の2段階に
/// 分けるかどうかの判定。最初の描画を一画面分に抑え、フォルダー移動の体感を速くする。
/// </summary>
public static class ListChunking
{
    /// <summary>
    /// 最初に表示する件数を返す。戻り値が totalCount と等しければ分割しない。
    /// 件数がチャンクの2倍以下(分割の利得なし)またはカーソル復元位置がチャンク外
    /// (部分表示中に選択できない)の場合は全件を返す。
    /// </summary>
    public static int FirstChunkCount(int totalCount, int cursorIndex, int chunkSize)
    {
        if (totalCount <= chunkSize * 2) return totalCount;
        if (cursorIndex >= chunkSize) return totalCount;
        return chunkSize;
    }
}
