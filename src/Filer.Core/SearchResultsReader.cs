namespace Filer.Core;

/// <summary>
/// ファイル検索の結果一覧を「仮想フォルダー」としてペインに表示するための
/// <see cref="IDirectoryReader"/> ラッパー。通常パスは内部リーダーへ委譲し、
/// <c>search://{id}/{ラベル}</c> 形式の仮想パスは登録済みの検索結果スナップショットを返す。
/// 先頭の ".." は検索の基準ディレクトリへ戻る。仮想パスは実在しないため
/// セッション保存(Directory.Exists 判定)や履歴には残らない。
/// </summary>
public sealed class SearchResultsReader : IDirectoryReader
{
    private const string Prefix = "search://";

    private readonly IDirectoryReader _inner;
    private readonly Dictionary<string, Listing> _listings = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _nextId;

    private sealed record Listing(string BaseDirectory, IReadOnlyList<FileEntry> Entries);

    public SearchResultsReader(IDirectoryReader inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>検索結果の仮想一覧かどうか。</summary>
    public static bool IsVirtual(string path) =>
        path.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>仮想パスから表示用ラベル(パンくず・タブ見出し)を取り出す。</summary>
    public static bool TryGetLabel(string path, out string label)
    {
        label = string.Empty;
        if (!IsVirtual(path)) return false;
        var slash = path.IndexOf('/', Prefix.Length);
        if (slash < 0) return false;
        label = path[(slash + 1)..];
        return true;
    }

    /// <summary>検索結果を登録し、表示用の仮想パスを返す。</summary>
    public string Register(string label, string baseDirectory, IReadOnlyList<FileEntry> entries)
    {
        lock (_gate)
        {
            var path = $"{Prefix}{++_nextId}/{label}";
            _listings[path] = new Listing(baseDirectory, entries);
            return path;
        }
    }

    public IReadOnlyList<FileEntry> Read(string path)
    {
        if (!IsVirtual(path))
            return _inner.Read(path);

        Listing? listing;
        lock (_gate)
            _listings.TryGetValue(path, out listing);
        if (listing is null)
            throw new DirectoryNotFoundException($"検索結果が見つかりません: {path}");

        // 削除・移動で消えた項目は除いて返す(再読込 F5 で一覧から消える)。
        var list = new List<FileEntry>(listing.Entries.Count + 1)
        {
            FileEntry.Parent(listing.BaseDirectory),
        };
        foreach (var entry in listing.Entries)
            if (StillExists(entry))
                list.Add(entry);
        return list;
    }

    private static bool StillExists(FileEntry entry)
    {
        // 書庫内の仮想パスは TrySplit が書庫の実在を確認する。
        if (ArchivePath.TrySplit(entry.FullPath, out _, out _)) return true;
        return entry.IsDirectory ? Directory.Exists(entry.FullPath) : File.Exists(entry.FullPath);
    }
}
