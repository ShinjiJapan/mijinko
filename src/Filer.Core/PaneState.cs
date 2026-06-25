namespace Filer.Core;

/// <summary>
/// 1ペイン分の状態。カレントパス・エントリ一覧・カーソル位置・マーク状態と、
/// それらに対するナビゲーション操作を持つ。UI 非依存でテスト可能。
/// </summary>
public sealed class PaneState
{
    private readonly IDirectoryReader _reader;
    private readonly HashSet<string> _marked = new(StringComparer.OrdinalIgnoreCase);
    // フォルダーパス → 最後にカーソルがあった項目名。セッション中のみ保持し永続化しない。
    private readonly Dictionary<string, string> _cursorMemory = new(StringComparer.OrdinalIgnoreCase);

    public string CurrentPath { get; private set; } = string.Empty;

    /// <summary>表示中のエントリ一覧(フィルター適用後)。</summary>
    public IReadOnlyList<FileEntry> Entries { get; private set; } = Array.Empty<FileEntry>();

    /// <summary>フィルター適用前のエントリ全件(ソート済み)。</summary>
    private IReadOnlyList<FileEntry> _allEntries = Array.Empty<FileEntry>();

    public int CursorIndex { get; private set; }

    /// <summary>
    /// 表示の絞り込みパターン(空=フィルターなし)。ワイルドカード *? は名前全体に照合、
    /// それ以外は部分一致(大小無視)。<see cref="FileSearcher.TryCreateMatcher"/> と同じ解釈。
    /// </summary>
    public string Filter { get; private set; } = string.Empty;

    /// <summary>フィルターが有効か。</summary>
    public bool HasFilter => !string.IsNullOrEmpty(Filter);

    /// <summary>現在のソート方法(既定: 名前順)。</summary>
    public SortKey SortKey { get; private set; } = SortKey.Name;

    /// <summary>降順かどうか(既定: 昇順)。</summary>
    public bool SortDescending { get; private set; }

    public PaneState(IDirectoryReader reader, string initialPath)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        Load(initialPath, cursorOnName: null);
    }

    /// <summary>表示中の項目があるか(フィルターで全件隠れた・空フォルダーは false)。</summary>
    public bool HasItems => Entries.Count > 0;

    /// <summary>カーソル位置のエントリ。表示項目が無い場合は <see cref="HasItems"/> で事前に確認すること。</summary>
    public FileEntry Current => Entries[CursorIndex];

    /// <summary>マークされているエントリ(親 ".." は含まない)。</summary>
    public IReadOnlyList<FileEntry> MarkedEntries =>
        Entries.Where(e => !e.IsParent && _marked.Contains(e.FullPath)).ToList();

    /// <summary>マークがあればマーク群、なければカーソル位置のエントリ。操作対象の決定に使う。</summary>
    public IReadOnlyList<FileEntry> MarkedOrCurrent
    {
        get
        {
            var marks = MarkedEntries;
            if (marks.Count > 0) return marks;
            if (!HasItems || Current.IsParent) return Array.Empty<FileEntry>();
            return new[] { Current };
        }
    }

    public bool IsMarked(FileEntry entry) => _marked.Contains(entry.FullPath);

    /// <summary>マーク集計の母数(".." を除いた全項目数)。</summary>
    public int ItemCount => Entries.Count(e => !e.IsParent);

    /// <summary>マークされたファイルの合計バイト数(フォルダーは無視する)。</summary>
    public long MarkedSize => MarkedEntries.Where(e => !e.IsDirectory).Sum(e => e.Size);

    /// <summary>
    /// フォルダー単位操作(お気に入り登録など)の対象パス。
    /// カーソルがサブフォルダー上ならそのフォルダー、ファイルや ".." 上なら現在のディレクトリ。
    /// </summary>
    public string TargetFolderPath =>
        HasItems && Current is { IsDirectory: true, IsParent: false } ? Current.FullPath : CurrentPath;

    /// <summary>
    /// 外部ツールで開く対象の項目パス。".." 上・表示項目なしなら現在のディレクトリ、それ以外はカーソル位置の項目。
    /// </summary>
    public string SelectedItemPath => !HasItems || Current.IsParent ? CurrentPath : Current.FullPath;

    /// <summary>カーソルを delta だけ移動する。範囲はクランプする。</summary>
    public void MoveCursor(int delta) => MoveCursorTo(CursorIndex + delta);

    /// <summary>カーソルを指定インデックスへ移動する。範囲はクランプする。</summary>
    public void MoveCursorTo(int index)
    {
        if (Entries.Count == 0) { CursorIndex = 0; return; }
        CursorIndex = Math.Clamp(index, 0, Entries.Count - 1);
    }

    /// <summary>
    /// カーソルを delta だけ移動する。末尾を超えたら先頭へ、先頭より前なら末尾へ回り込む(ループ)。
    /// ↑↓ の1行移動で一覧の端を跨ぐ移動に使う。
    /// </summary>
    public void MoveCursorWrap(int delta)
    {
        if (Entries.Count == 0) { CursorIndex = 0; return; }
        var n = Entries.Count;
        CursorIndex = ((CursorIndex + delta) % n + n) % n;
    }

    /// <summary>カーソル位置のエントリを開く。ディレクトリ("..")・書庫なら移動、通常ファイルなら何もしない。</summary>
    public void Open()
    {
        if (!HasItems) return;
        var current = Current;
        if (!current.IsDirectory && !current.IsArchive) return;
        // ".." も通常ディレクトリも FullPath が移動先絶対パスなので同じ扱い。
        var fromName = current.IsParent ? DirName(CurrentPath) : null;
        Load(current.FullPath, cursorOnName: fromName);
    }

    /// <summary>親ディレクトリへ移動する。移動後は元いたディレクトリ名にカーソルを合わせる。</summary>
    public void GoToParent()
    {
        var parent = Entries.FirstOrDefault(e => e.IsParent);
        if (parent is null) return;
        Load(parent.FullPath, cursorOnName: DirName(CurrentPath));
    }

    /// <summary>
    /// 全選択 ⇔ 全選択解除を切り替える。対象はファイルのみ(フォルダーと親 ".." は対象外)。
    /// 全ファイルがマーク済みなら全ファイルを解除、そうでなければ全ファイルをマークする。
    /// </summary>
    public void ToggleMarkAll()
    {
        var files = Entries.Where(e => !e.IsParent && !e.IsDirectory).ToList();
        if (files.Count == 0) return;

        var allMarked = files.All(e => _marked.Contains(e.FullPath));
        foreach (var entry in files)
        {
            if (allMarked)
                _marked.Remove(entry.FullPath);
            else
                _marked.Add(entry.FullPath);
        }
    }

    /// <summary>カーソル位置のマークを切り替える(親 ".." は対象外)。</summary>
    public void ToggleMark()
    {
        if (!HasItems) return;
        var current = Current;
        if (current.IsParent) return;
        if (!_marked.Remove(current.FullPath))
            _marked.Add(current.FullPath);
    }

    /// <summary>fromIndex から toIndex までの範囲(両端含む)をマークする(親 ".." は対象外)。Shift+クリックの範囲選択用。</summary>
    public void MarkRange(int fromIndex, int toIndex)
    {
        if (!HasItems) return;
        var lo = Math.Clamp(Math.Min(fromIndex, toIndex), 0, Entries.Count - 1);
        var hi = Math.Clamp(Math.Max(fromIndex, toIndex), 0, Entries.Count - 1);
        for (var i = lo; i <= hi; i++)
        {
            var entry = Entries[i];
            if (entry.IsParent) continue;
            _marked.Add(entry.FullPath);
        }
    }

    /// <summary>任意の絶対パスへ移動する。カーソルは先頭に置く。</summary>
    public void NavigateTo(string path) => Load(path, cursorOnName: null);

    /// <summary>ソート方法・昇降順を変更し、現在のカーソル項目を保ったまま並べ替える。</summary>
    public void SetSort(SortKey key, bool descending)
    {
        SortKey = key;
        SortDescending = descending;
        var keepPath = Entries.Count > 0 ? Current.FullPath : null;
        _allEntries = EntrySorter.Sort(_allEntries, key, descending);
        ApplyFilter();
        var index = keepPath is null ? 0 : IndexOfPath(keepPath);
        CursorIndex = index < 0 ? 0 : index;
        MoveCursorTo(CursorIndex);
    }

    /// <summary>
    /// 表示の絞り込みパターンを設定する(空ならフィルター解除)。一致した項目だけを表示し、
    /// 親 ".." は常に残す。可能ならカーソルを同じ項目に保つ(消えたら先頭)。
    /// </summary>
    public void SetFilter(string pattern)
    {
        var keepPath = Entries.Count > 0 && !Current.IsParent ? Current.FullPath : null;
        Filter = (pattern ?? string.Empty).Trim();
        ApplyFilter();
        var index = keepPath is null ? -1 : IndexOfPath(keepPath);
        CursorIndex = index < 0 ? 0 : index;
        MoveCursorTo(CursorIndex);
    }

    /// <summary>現在のフィルターを全件に適用して表示一覧(<see cref="Entries"/>)を作る(".." は常に残す)。</summary>
    private void ApplyFilter()
    {
        if (string.IsNullOrEmpty(Filter) ||
            !FileSearcher.TryCreateMatcher(Filter, useRegex: false, out var matcher, out _))
        {
            Entries = _allEntries;
        }
        else
        {
            var list = new List<FileEntry>(_allEntries.Count);
            foreach (var e in _allEntries)
                if (e.IsParent || matcher(e.Name))
                    list.Add(e);
            Entries = list;
        }
        MoveCursorTo(CursorIndex);
    }

    /// <summary>
    /// 現在のパスを再読込する。カーソルは同じ項目(パス)に追従させる。
    /// 項目が消えていれば元のインデックスをクランプして近い位置に置く。
    /// </summary>
    public void Reload()
    {
        var keepPath = Entries.Count > 0 && !Current.IsParent ? Current.FullPath : null;
        var keepIndex = CursorIndex;
        _allEntries = EntrySorter.Sort(_reader.Read(CurrentPath), SortKey, SortDescending);
        ApplyFilter();   // 再読込でもフィルターは維持する
        var index = keepPath is null ? keepIndex : IndexOfPath(keepPath);
        MoveCursorTo(index < 0 ? keepIndex : index);
    }

    private void Load(string path, string? cursorOnName)
    {
        // 移動前に、いま居るフォルダーのカーソル位置を記憶する(再訪時に再現するため)。
        RememberCursor();

        _allEntries = EntrySorter.Sort(_reader.Read(path), SortKey, SortDescending);
        CurrentPath = path;
        _marked.Clear();
        Filter = string.Empty;          // フォルダー移動でフィルターは解除する
        Entries = _allEntries;

        // 移動先フォルダーの記憶があれば優先して復元。無ければ明示指定(親へ戻る等)、それも無ければ先頭。
        var targetName = _cursorMemory.TryGetValue(path, out var remembered)
            ? remembered
            : cursorOnName;
        var index = targetName is null ? 0 : IndexOfName(targetName);
        CursorIndex = index < 0 ? 0 : index;
        MoveCursorTo(CursorIndex);
    }

    /// <summary>現在のフォルダーのカーソル項目名を記憶する(".." と空一覧は対象外)。</summary>
    private void RememberCursor()
    {
        if (string.IsNullOrEmpty(CurrentPath) || Entries.Count == 0)
            return;
        var current = Current;
        if (current.IsParent)
            return;
        _cursorMemory[CurrentPath] = current.Name;
    }

    private int IndexOfName(string name)
    {
        for (var i = 0; i < Entries.Count; i++)
            if (string.Equals(Entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private int IndexOfPath(string fullPath)
    {
        for (var i = 0; i < Entries.Count; i++)
            if (string.Equals(Entries[i].FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private static string DirName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var idx = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        return idx < 0 ? trimmed : trimmed[(idx + 1)..];
    }
}
