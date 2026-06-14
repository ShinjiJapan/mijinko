namespace Filer.Core;

/// <summary>ファイル検索ダイアログでEnterを押したときの動作。</summary>
public enum FileSearchEnterAction
{
    /// <summary>検索を開始する(未検索状態)。</summary>
    StartSearch,

    /// <summary>結果を転送してダイアログを閉じる(検索中・検索後)。</summary>
    Transfer,
}

/// <summary>ファイル検索ダイアログのキー操作に関する判定。</summary>
public static class FileSearchInteraction
{
    /// <summary>
    /// Enter押下時の動作を決める。未検索なら検索開始、いったん検索を始めた後
    /// (検索中・検索後)は「転送して閉じる」とする。
    /// </summary>
    public static FileSearchEnterAction DecideEnterAction(bool searchStarted) =>
        searchStarted ? FileSearchEnterAction.Transfer : FileSearchEnterAction.StartSearch;
}
