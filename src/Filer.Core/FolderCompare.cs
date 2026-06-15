namespace Filer.Core;

/// <summary>フォルダー比較での各ノードの状態。</summary>
public enum FolderCompareKind
{
    /// <summary>左右に存在し、内容も一致(ディレクトリは配下に差異なし)。</summary>
    Same,
    /// <summary>左右に存在するが内容が異なる(ディレクトリは配下に差異あり)。</summary>
    Modified,
    /// <summary>左にのみ存在する。</summary>
    LeftOnly,
    /// <summary>右にのみ存在する。</summary>
    RightOnly,
}

/// <summary>
/// フォルダー比較のオプション。<see cref="CompareSize"/>/<see cref="CompareDate"/>/<see cref="CompareContent"/>
/// のいずれかが真の基準で「変更」を判定する(全て偽なら自動でサイズ比較にフォールバック)。
/// <see cref="ShowSame"/> は表示専用(比較ロジックは参照しない=レンダラーが使用)。
/// </summary>
public sealed record FolderCompareOptions(
    bool CompareSize = true,
    bool CompareDate = false,
    bool CompareContent = false,
    bool Recursive = true,
    bool ShowSame = true);

/// <summary>ディレクトリ列挙の1項目(名前・種別・サイズ・更新日時)。</summary>
public sealed record CompareDirEntry(string Name, bool IsDirectory, long Size, DateTime LastModifiedUtc);

/// <summary>
/// 比較結果のツリーノード。ファイルはサイズを、ディレクトリは配下ノードを <see cref="Children"/> に持つ。
/// 片側のみ存在する場合、存在しない側のパス/サイズは null。
/// </summary>
public sealed record FolderCompareNode(
    string Name,
    bool IsDirectory,
    FolderCompareKind Kind,
    string? LeftPath,
    string? RightPath,
    long? LeftSize,
    long? RightSize,
    IReadOnlyList<FolderCompareNode> Children);

/// <summary>状態ごとの件数(ファイルのみを数える)。</summary>
public sealed record FolderCompareSummary(int Same, int Modified, int LeftOnly, int RightOnly);

/// <summary>
/// フォルダー(ツリー)比較のためにファイルシステムへアクセスする抽象。
/// 実装は <see cref="FileSystemFolderCompareSource"/>。テストではインメモリの差し替えが可能。
/// </summary>
public interface IFolderCompareSource
{
    /// <summary>ディレクトリ直下の項目(".." は含めない)を列挙する。</summary>
    IReadOnlyList<CompareDirEntry> List(string directoryPath);

    /// <summary>2ファイルの内容(バイト列)が一致するか。</summary>
    bool ContentEquals(string leftFilePath, string rightFilePath);
}

/// <summary>
/// 2つのフォルダーを名前単位で対応付け、左右で「同一/変更/左のみ/右のみ」を判定する(UI 非依存)。
/// 大文字小文字を無視して対応付け、ディレクトリ群→ファイル群の順に名前で安定整列する。
/// </summary>
public static class FolderComparer
{
    /// <summary>左右ルートフォルダーを比較し、トップレベルのノード一覧を返す。</summary>
    public static IReadOnlyList<FolderCompareNode> Compare(
        string leftRoot, string rightRoot, FolderCompareOptions options,
        IFolderCompareSource source, CancellationToken token = default)
    {
        // 比較基準が1つも無ければサイズ比較を既定にする(差異が常に「同一」になる事故防止)。
        if (!options.CompareSize && !options.CompareDate && !options.CompareContent)
            options = options with { CompareSize = true };
        return CompareDir(leftRoot, rightRoot, options, source, token);
    }

    /// <summary>
    /// 同一(Same)のノードを取り除き、差異(変更/左のみ/右のみ)だけのツリーを返す。
    /// 「同一の項目も表示する」が OFF のときに使う。配下全て同一のディレクトリ(Kind=Same)は丸ごと除外される。
    /// </summary>
    public static IReadOnlyList<FolderCompareNode> FilterDifferencesOnly(IReadOnlyList<FolderCompareNode> nodes)
    {
        var result = new List<FolderCompareNode>();
        foreach (var n in nodes)
        {
            if (n.Kind == FolderCompareKind.Same) continue;
            var children = n.Children.Count == 0 ? n.Children : FilterDifferencesOnly(n.Children);
            result.Add(n with { Children = children });
        }
        return result;
    }

    /// <summary>ノード木からファイルの状態別件数を集計する。</summary>
    public static FolderCompareSummary Summarize(IReadOnlyList<FolderCompareNode> nodes)
    {
        int same = 0, modified = 0, leftOnly = 0, rightOnly = 0;
        void Walk(IReadOnlyList<FolderCompareNode> ns)
        {
            foreach (var n in ns)
            {
                if (!n.IsDirectory)
                {
                    switch (n.Kind)
                    {
                        case FolderCompareKind.Same: same++; break;
                        case FolderCompareKind.Modified: modified++; break;
                        case FolderCompareKind.LeftOnly: leftOnly++; break;
                        case FolderCompareKind.RightOnly: rightOnly++; break;
                    }
                }
                Walk(n.Children);
            }
        }
        Walk(nodes);
        return new FolderCompareSummary(same, modified, leftOnly, rightOnly);
    }

    private static List<FolderCompareNode> CompareDir(
        string left, string right, FolderCompareOptions options,
        IFolderCompareSource source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var leftItems = source.List(left).ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        var rightItems = source.List(right).ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        names.UnionWith(leftItems.Keys);
        names.UnionWith(rightItems.Keys);

        var nodes = new List<FolderCompareNode>();
        foreach (var name in names)
        {
            leftItems.TryGetValue(name, out var l);
            rightItems.TryGetValue(name, out var r);

            if (l is not null && r is null)
                nodes.Add(OneSided(name, Combine(left, name), l, FolderCompareKind.LeftOnly, isLeft: true, options, source, token));
            else if (l is null && r is not null)
                nodes.Add(OneSided(name, Combine(right, name), r, FolderCompareKind.RightOnly, isLeft: false, options, source, token));
            else
                nodes.Add(CompareBoth(name, left, right, l!, r!, options, source, token));
        }

        // ディレクトリ群→ファイル群の順、各群は名前で安定整列。
        return nodes
            .OrderBy(n => n.IsDirectory ? 0 : 1)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>左右どちらにも存在する項目を比較する。</summary>
    private static FolderCompareNode CompareBoth(
        string name, string leftDir, string rightDir, CompareDirEntry l, CompareDirEntry r,
        FolderCompareOptions options, IFolderCompareSource source, CancellationToken token)
    {
        var leftPath = Combine(leftDir, name);
        var rightPath = Combine(rightDir, name);

        // 片方がディレクトリで片方がファイル → 種別が変わった(変更扱い)。
        if (l.IsDirectory != r.IsDirectory)
            return new FolderCompareNode(name, l.IsDirectory, FolderCompareKind.Modified,
                leftPath, rightPath, l.IsDirectory ? null : l.Size, r.IsDirectory ? null : r.Size,
                Array.Empty<FolderCompareNode>());

        if (l.IsDirectory)
        {
            var children = options.Recursive
                ? CompareDir(leftPath, rightPath, options, source, token)
                : new List<FolderCompareNode>();
            // 配下に1つでも差異があればディレクトリは「変更」。
            var kind = children.All(c => c.Kind == FolderCompareKind.Same)
                ? FolderCompareKind.Same : FolderCompareKind.Modified;
            return new FolderCompareNode(name, true, kind, leftPath, rightPath, null, null, children);
        }

        var differs = FilesDiffer(l, r, leftPath, rightPath, options, source);
        return new FolderCompareNode(name, false,
            differs ? FolderCompareKind.Modified : FolderCompareKind.Same,
            leftPath, rightPath, l.Size, r.Size, Array.Empty<FolderCompareNode>());
    }

    /// <summary>有効な比較基準のいずれかで差があれば true(=変更)。</summary>
    private static bool FilesDiffer(
        CompareDirEntry l, CompareDirEntry r, string leftPath, string rightPath,
        FolderCompareOptions options, IFolderCompareSource source)
    {
        if (options.CompareSize && l.Size != r.Size) return true;
        if (options.CompareDate && l.LastModifiedUtc != r.LastModifiedUtc) return true;
        if (options.CompareContent)
        {
            if (l.Size != r.Size) return true;               // サイズが違えば内容も違う
            if (!source.ContentEquals(leftPath, rightPath)) return true;
        }
        return false;
    }

    /// <summary>片側のみ存在する項目(ディレクトリは配下を同じ状態で再帰展開する)。</summary>
    private static FolderCompareNode OneSided(
        string name, string path, CompareDirEntry entry, FolderCompareKind kind, bool isLeft,
        FolderCompareOptions options, IFolderCompareSource source, CancellationToken token)
    {
        if (!entry.IsDirectory)
            return new FolderCompareNode(name, false, kind,
                isLeft ? path : null, isLeft ? null : path,
                isLeft ? entry.Size : null, isLeft ? null : entry.Size,
                Array.Empty<FolderCompareNode>());

        var children = new List<FolderCompareNode>();
        if (options.Recursive)
        {
            token.ThrowIfCancellationRequested();
            foreach (var child in source.List(path))
                children.Add(OneSided(child.Name, Combine(path, child.Name), child, kind, isLeft, options, source, token));
            children = children
                .OrderBy(n => n.IsDirectory ? 0 : 1)
                .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        return new FolderCompareNode(name, true, kind,
            isLeft ? path : null, isLeft ? null : path, null, null, children);
    }

    /// <summary>区切り重複を避けてパスを連結する(末尾 \ の有無に依存しない)。</summary>
    private static string Combine(string dir, string name) =>
        dir.EndsWith('\\') || dir.EndsWith('/') ? dir + name : dir + "\\" + name;
}
