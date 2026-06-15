namespace Filer.Core;

/// <summary>転送対象の片側(左/右ペイン)。</summary>
public enum FolderCompareSide { Left, Right }

/// <summary>転送する1ファイル(基準フォルダーからの相対パスと実パス)。</summary>
public sealed record FolderCompareTransferItem(string RelativePath, string FullPath);

/// <summary>
/// フォルダー比較結果から、指定した側(左/右)へ転送するファイルを集める(UI 非依存)。
/// 「差分」=変更/その側のみ、「重複」=同一。ディレクトリは辿るだけで、出力はファイルのみ(相対パス付き)。
/// </summary>
public static class FolderCompareTransfer
{
    /// <summary>
    /// 転送対象ファイルを収集する。
    /// <paramref name="includeDifferences"/> で差分(変更/その側のみ)、<paramref name="includeSame"/> で同一を含める。
    /// </summary>
    public static IReadOnlyList<FolderCompareTransferItem> Collect(
        IReadOnlyList<FolderCompareNode> nodes, FolderCompareSide side,
        bool includeDifferences, bool includeSame)
    {
        var result = new List<FolderCompareTransferItem>();
        Walk(nodes, "", side, includeDifferences, includeSame, result);
        return result;
    }

    private static void Walk(
        IReadOnlyList<FolderCompareNode> nodes, string prefix, FolderCompareSide side,
        bool includeDifferences, bool includeSame, List<FolderCompareTransferItem> result)
    {
        foreach (var node in nodes)
        {
            var rel = prefix.Length == 0 ? node.Name : prefix + "\\" + node.Name;
            if (node.IsDirectory)
            {
                Walk(node.Children, rel, side, includeDifferences, includeSame, result);
                continue;
            }

            // その側に実体が無ければ対象外(LeftOnly は右に、RightOnly は左に存在しない)。
            var path = side == FolderCompareSide.Left ? node.LeftPath : node.RightPath;
            if (path is null) continue;

            // 同一は「重複」、それ以外(変更/その側のみ)は「差分」。
            var include = node.Kind == FolderCompareKind.Same ? includeSame : includeDifferences;
            if (include) result.Add(new FolderCompareTransferItem(rel, path));
        }
    }
}
