using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Filer.Core;

namespace Filer.App.ViewModels;

/// <summary>
/// 1ペイン分の表示状態。<see cref="PaneState"/>(モデル)を ObservableCollection に投影する。
/// </summary>
public sealed partial class PaneViewModel : ObservableObject
{
    private readonly PaneTabs _tabs;

    /// <summary>タブ切り替えに伴うインデックス同期中、UI バインディングからの再入を抑止する。</summary>
    private bool _suppressTabSync;

    private PaneState _state => _tabs.Active;

    /// <summary>一覧の表示項目。大量件数でも固まらないよう、入れ替えは Reset 一括通知で行う。</summary>
    public BulkObservableCollection<EntryViewModel> Entries { get; } = new();

    /// <summary>現在パスを階層ごとに分割したパンくず(各区切りクリックでそのフォルダーへ移動)。</summary>
    public ObservableCollection<BreadcrumbSegment> Breadcrumb { get; } = new();

    /// <summary>このペインが持つタブ見出し一覧。</summary>
    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>アクティブなタブのインデックス(タブ一覧の選択と双方向バインド)。</summary>
    [ObservableProperty]
    private int _activeTabIndex;

    /// <summary>パンくず右端に出す Git ブランチ表示(例 "main ↑1")。リポジトリ外は空。</summary>
    [ObservableProperty]
    private string _gitBranchText = string.Empty;

    /// <summary>現在フォルダーが Git リポジトリ配下かどうか。非Gitではバッジ列を畳む。</summary>
    [ObservableProperty]
    private bool _hasGitRepository;

    /// <summary>一覧の表示形式(詳細 / サムネイルグリッド)。</summary>
    [ObservableProperty]
    private PaneViewMode _viewMode = PaneViewMode.Details;

    partial void OnViewModeChanged(PaneViewMode value)
    {
        OnPropertyChanged(nameof(DetailsVisibility));
        OnPropertyChanged(nameof(GridVisibility));
    }

    /// <summary>詳細表示(リスト)の可視性。</summary>
    public Visibility DetailsVisibility => ViewMode == PaneViewMode.Grid ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>サムネイルグリッドの可視性。</summary>
    public Visibility GridVisibility => ViewMode == PaneViewMode.Grid ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>詳細 ⇔ サムネイルグリッドを切り替える。</summary>
    public void ToggleViewMode() =>
        ViewMode = ViewMode == PaneViewMode.Grid ? PaneViewMode.Details : PaneViewMode.Grid;

    /// <summary>サムネイルグリッドのタイルサイズ(通常 / 拡大)。</summary>
    [ObservableProperty]
    private GridTileSize _gridSize = GridTileSize.Normal;

    partial void OnGridSizeChanged(GridTileSize value)
    {
        OnPropertyChanged(nameof(GridTileWidth));
        OnPropertyChanged(nameof(GridImageSize));
        OnPropertyChanged(nameof(GridCellWidth));
        OnPropertyChanged(nameof(GridCellHeight));
    }

    /// <summary>グリッド1タイルの幅(px。XAML バインド用)。</summary>
    public double GridTileWidth => GridTileMetrics.TileWidth(GridSize);

    /// <summary>グリッド1タイル内の画像の一辺(px。XAML バインド用)。</summary>
    public double GridImageSize => GridTileMetrics.ImageSize(GridSize);

    /// <summary>グリッドのタイル外形(コンテナ1個ぶん)の幅(px。仮想化パネルへのバインド用)。</summary>
    public double GridCellWidth => GridTileMetrics.CellWidth(GridSize);

    /// <summary>グリッドのタイル外形(コンテナ1個ぶん)の高さ(px。仮想化パネルへのバインド用)。</summary>
    public double GridCellHeight => GridTileMetrics.CellHeight(GridSize);

    /// <summary>グリッドのタイルサイズを 通常 ⇔ 拡大 で切り替える。</summary>
    public void ToggleGridSize() => GridSize = GridTileMetrics.Next(GridSize);

    /// <summary>Git 管理下だけバッジ列の固定幅を確保する。</summary>
    public GridLength GitBadgeColumnWidth => HasGitRepository ? new GridLength(19) : new GridLength(0);

    /// <summary>非Gitでは名前を左に詰め、Git 管理下では従来の余白を保つ。</summary>
    public Thickness GitNameMargin => HasGitRepository ? new Thickness(2, 0, 0, 0) : new Thickness(0);

    public PaneViewModel(IDirectoryReader reader, string initialPath)
    {
        _tabs = new PaneTabs(reader, initialPath);
        RebuildTabs();
        Refresh();
    }

    /// <summary>複数タブを復元して構築する(セッション復元用)。</summary>
    public PaneViewModel(IDirectoryReader reader, IReadOnlyList<string> tabPaths, int activeTabIndex)
    {
        _tabs = new PaneTabs(reader, tabPaths, activeTabIndex);
        RebuildTabs();
        Refresh();
    }

    /// <summary>開いている全タブのパス(セッション保存用)。</summary>
    public IReadOnlyList<string> TabPaths => _tabs.TabPaths;

    /// <summary>操作対象のディレクトリ(現在パス)。</summary>
    public string DirectoryPath => _state.CurrentPath;

    /// <summary>カーソル位置 or マーク群(コピー/移動/削除の対象)。</summary>
    public IReadOnlyList<FileEntry> Targets => _state.MarkedOrCurrent;

    /// <summary>マークされたエントリのみ(カーソルへのフォールバックなし。".." は含まない)。</summary>
    public IReadOnlyList<FileEntry> Marked => _state.MarkedEntries;

    /// <summary>フォルダー単位操作の対象パス(選択フォルダー優先、なければ現在ディレクトリ)。</summary>
    public string TargetFolderPath => _state.TargetFolderPath;

    /// <summary>外部ツールで開く対象の項目パス(カーソル項目。".." なら現在ディレクトリ)。</summary>
    public string SelectedItemPath => _state.SelectedItemPath;

    public FileEntry Current => _state.Current;

    /// <summary>表示中の項目があるか(フィルターで全件隠れた・空フォルダーは false)。</summary>
    public bool HasItems => _state.HasItems;

    /// <summary>".." を含む全件数(モデル側の件数。2段階表示中の部分集合に左右されない)。</summary>
    public int EntryCount => _state.Entries.Count;

    /// <summary>パンくず右端のマーク集計(「marked 12/560 4.5MB」)。マーク・一覧の変化で更新する。</summary>
    public string MarkSummaryText =>
        MarkSummary.Format(_state.MarkedEntries.Count, _state.ItemCount, _state.MarkedSize);

    /// <summary>現在のソート方法。</summary>
    public SortKey SortKey => _state.SortKey;

    /// <summary>降順かどうか。</summary>
    public bool SortDescending => _state.SortDescending;

    /// <summary>表示の絞り込みパターン(空=フィルターなし)。</summary>
    public string Filter => _state.Filter;

    /// <summary>フィルターが有効か。</summary>
    public bool HasFilter => _state.HasFilter;

    /// <summary>フィルター入力ダイアログを開いている間 true。空入力でも「絞り込み:」を表示するために使う。</summary>
    private bool _filterEditing;

    /// <summary>フィルター編集中フラグ。設定でパンくず横の「絞り込み:」表示が即時に切り替わる。</summary>
    public bool FilterEditing
    {
        get => _filterEditing;
        set
        {
            if (_filterEditing == value) return;
            _filterEditing = value;
            OnPropertyChanged(nameof(FilterText));
        }
    }

    /// <summary>
    /// パンくず横に出すフィルター表示(例 "絞り込み: *.jpg  (ESCで解除)")。編集中は空入力でも「絞り込み:」を出す
    /// (このときの Esc はダイアログのキャンセルなので解除ヒントは出さない)。確定済みは Esc で解除できる旨を添える。
    /// フィルターも編集中でもない場合は空(表示を畳む)。
    /// </summary>
    public string FilterText
    {
        get
        {
            if (_filterEditing) return $"絞り込み: {_state.Filter}";
            if (_state.HasFilter) return $"絞り込み: {_state.Filter}  (ESCで解除)";
            return string.Empty;
        }
    }

    /// <summary>表示の絞り込みパターンを設定する(空ならフィルター解除)。</summary>
    public void SetFilter(string pattern)
    {
        _state.SetFilter(pattern);
        Refresh();
    }

    partial void OnHasGitRepositoryChanged(bool value)
    {
        OnPropertyChanged(nameof(GitBadgeColumnWidth));
        OnPropertyChanged(nameof(GitNameMargin));
    }

    /// <summary>ソート方法・昇降順を変更して並べ替える(カーソル項目は維持)。</summary>
    public void SetSort(SortKey key, bool descending)
    {
        _state.SetSort(key, descending);
        Refresh();
    }

    public void MoveCursor(int delta)
    {
        _state.MoveCursor(delta);
        SelectedIndex = _state.CursorIndex;
    }

    /// <summary>カーソルを delta だけ移動する(一覧の端で反対側へループ)。↑↓の1行移動用。</summary>
    public void MoveCursorWrap(int delta)
    {
        _state.MoveCursorWrap(delta);
        SelectedIndex = _state.CursorIndex;
    }

    /// <summary>カーソルを指定インデックスへ(範囲はクランプ)。</summary>
    public void MoveCursorTo(int index)
    {
        _state.MoveCursorTo(index);
        SelectedIndex = _state.CursorIndex;
    }

    public void MoveToTop() => MoveCursorTo(0);
    public void MoveToBottom() => MoveCursorTo(int.MaxValue);   // クランプで末尾へ

    public void Open() => RunTimed("Open", () => _state.Open(), chunked: true);

    /// <summary>任意の絶対パスへ移動する(ドライブ/お気に入りからの移動に使用)。</summary>
    public void NavigateTo(string path) => RunTimed("NavigateTo", () => _state.NavigateTo(path), chunked: true);

    public void GoToParent() => RunTimed("GoToParent", () => _state.GoToParent(), chunked: true);

    public void ToggleMarkAndAdvance()
    {
        _state.ToggleMark();
        SyncMarks();
        _state.MoveCursor(1);
        SelectedIndex = _state.CursorIndex;
    }

    /// <summary>全選択 ⇔ 全選択解除を切り替える。</summary>
    public void ToggleMarkAll()
    {
        _state.ToggleMarkAll();
        SyncMarks();
    }

    /// <summary>
    /// 再読込。呼び出し直後に表示項目を同期参照する利用者(プレビューの画像送り等)が
    /// いるため、2段階表示は使わず全件を即時反映する。
    /// </summary>
    public void Reload() => RunTimed("Reload", () => _state.Reload(), chunked: false);

    /// <summary>フォルダー移動時間の調査用ログの出力先(環境変数 FILER_PERFLOG にパス指定で有効)。</summary>
    private static readonly string? PerfLogPath = Environment.GetEnvironmentVariable("FILER_PERFLOG");

    /// <summary>ナビゲーション(読み取り+ソート)と表示更新(Refresh)の所要時間を計測して実行する。</summary>
    private void RunTimed(string label, Action navigate, bool chunked)
    {
        if (PerfLogPath is null)
        {
            navigate();
            Refresh(chunked);
            return;
        }
        var sw = Stopwatch.StartNew();
        navigate();
        var navMs = sw.ElapsedMilliseconds;
        Refresh(chunked);
        File.AppendAllText(PerfLogPath,
            $"{DateTime.Now:HH:mm:ss.fff} {label} path={_state.CurrentPath} n={_state.Entries.Count} readSort={navMs}ms refresh={sw.ElapsedMilliseconds - navMs}ms\n");
    }

    /// <summary>新しいタブを現在フォルダで開き、そのタブをアクティブにする。</summary>
    public void AddTab()
    {
        _tabs.AddTab();
        RebuildTabs();
        Refresh();
    }

    /// <summary>アクティブなタブを閉じる(最後の1枚は閉じない)。</summary>
    public void CloseActiveTab()
    {
        _tabs.CloseActive();
        RebuildTabs();
        Refresh();
    }

    /// <summary>指定タブを閉じる(最後の1枚は閉じない)。タブの × ボタンから呼ぶ。</summary>
    public void CloseTab(TabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        _tabs.CloseTab(index);
        RebuildTabs();
        Refresh();
    }

    /// <summary>次のタブへ切り替える(末尾の次は先頭へループ)。</summary>
    public void ActivateNextTab()
    {
        _tabs.ActivateNext();
        SyncActiveTab();
    }

    /// <summary>前のタブへ切り替える(先頭の前は末尾へループ)。</summary>
    public void ActivatePrevTab()
    {
        _tabs.ActivatePrev();
        SyncActiveTab();
    }

    /// <summary>ListView 側の選択変更をモデルへ反映する。</summary>
    partial void OnSelectedIndexChanged(int value)
    {
        if (value >= 0)
            _state.MoveCursorTo(value);
    }

    /// <summary>タブ一覧の選択変更(クリック等)をモデルへ反映する。</summary>
    partial void OnActiveTabIndexChanged(int value)
    {
        if (_suppressTabSync) return;
        if (value < 0 || value >= _tabs.Tabs.Count || value == _tabs.ActiveIndex) return;
        _tabs.Activate(value);
        Refresh();
    }

    /// <summary>タブ見出しコレクションをモデルの現状から作り直し、選択を同期する。</summary>
    private void RebuildTabs()
    {
        _suppressTabSync = true;
        Tabs.Clear();
        foreach (var tab in _tabs.Tabs)
            Tabs.Add(new TabViewModel(TabTitle(tab.CurrentPath)));
        ActiveTabIndex = _tabs.ActiveIndex;
        _suppressTabSync = false;
    }

    /// <summary>アクティブタブのインデックスを UI へ反映し、表示を更新する。</summary>
    private void SyncActiveTab()
    {
        _suppressTabSync = true;
        ActiveTabIndex = _tabs.ActiveIndex;
        _suppressTabSync = false;
        Refresh();
    }

    /// <summary>タブ見出しに使うフォルダー名(パス末尾の階層名。検索結果の仮想一覧はラベル)。</summary>
    private static string TabTitle(string path)
    {
        if (SearchResultsReader.TryGetLabel(path, out var label))
            return label;
        var segments = PathBreadcrumb.Build(path);
        return segments.Count > 0 ? segments[^1].Name : path;
    }

    /// <summary>最初の描画で差し替える件数(おおむね一画面分強)。</summary>
    private const int FirstChunkSize = 128;

    /// <summary>世代カウンタ。残り追加(遅延)中に次の移動が来たら古い追加を破棄する。</summary>
    private int _refreshGeneration;

    private void Refresh(bool chunked = false)
    {
        var generation = ++_refreshGeneration;
        var source = _state.Entries;
        var firstCount = chunked
            ? ListChunking.FirstChunkCount(source.Count, _state.CursorIndex, FirstChunkSize)
            : source.Count;

        var entries = new List<EntryViewModel>(source.Count);
        foreach (var e in source)
            entries.Add(new EntryViewModel(e) { IsMarked = _state.IsMarked(e) });

        // 大量件数のフォルダー移動は先頭チャンクだけ即表示し、残りは描画後(Loaded)に一括追加する。
        // 追加はキー入力(Input)より先に処理されるため、操作時には常に全件そろっている。
        Entries.ReplaceAll(entries.Take(firstCount));
        if (firstCount < entries.Count)
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    if (generation != _refreshGeneration) return;   // 別の移動・更新で置き換え済み
                    Entries.AddRange(entries.Skip(firstCount));
                    SelectedIndex = _state.CursorIndex;             // Reset 通知で消えた選択を戻す
                    ApplyGitStates();                               // 先に届いたステータスを追加分へも反映
                }));

        CurrentPath = _state.CurrentPath;
        SelectedIndex = _state.CursorIndex;

        Breadcrumb.Clear();
        if (SearchResultsReader.TryGetLabel(_state.CurrentPath, out var searchLabel))
            Breadcrumb.Add(new BreadcrumbSegment(searchLabel, _state.CurrentPath));   // 仮想一覧は1区切りで表示
        else
            foreach (var segment in PathBreadcrumb.Build(_state.CurrentPath))
                Breadcrumb.Add(segment);

        // アクティブタブの見出しを現在フォルダ名へ更新(移動で変わるため)。
        if (ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count)
            Tabs[ActiveTabIndex].Title = TabTitle(_state.CurrentPath);

        OnPropertyChanged(nameof(MarkSummaryText));
        OnPropertyChanged(nameof(FilterText));
        OnPropertyChanged(nameof(HasFilter));

        RefreshGitStatus();
    }

    /// <summary>Git ステータス取得の世代。取得中に次の移動が来たら古い結果を破棄する。</summary>
    private int _gitGeneration;

    /// <summary>最後に取得した Git ステータス(2段階表示の追加分への再適用に使う)。</summary>
    private GitStatusService.Result? _gitResult;

    /// <summary>現在フォルダーの Git ステータスを非同期に取得し、ブランチ表示と行の色分けへ反映する。</summary>
    private async void RefreshGitStatus()
    {
        var generation = ++_gitGeneration;
        _gitResult = null;
        var path = _state.CurrentPath;

        // 実在フォルダーのみ対象(検索結果の仮想一覧・書庫内は対象外)。
        // ネットワークパスで固まらないよう存在確認ごとバックグラウンドで行う。
        var result = await Task.Run(() =>
            Directory.Exists(path) ? GitStatusService.QueryAsync(path) : Task.FromResult<GitStatusService.Result?>(null));

        if (generation != _gitGeneration) return;
        _gitResult = result;
        HasGitRepository = result is not null;
        var branch = result?.Snapshot.BranchDisplay;
        GitBranchText = string.IsNullOrEmpty(branch) ? string.Empty : $"({branch})";
        ApplyGitStates();
    }

    /// <summary>取得済みステータスを表示中の全行へ適用する(未取得・リポジトリ外は None へ戻す)。</summary>
    private void ApplyGitStates()
    {
        var result = _gitResult;
        var prefix = string.Empty;
        if (result is not null)
        {
            var relative = Path.GetRelativePath(result.RepositoryRoot, _state.CurrentPath);
            prefix = relative == "." ? string.Empty : relative.Replace('\\', '/') + "/";
        }

        foreach (var vm in Entries)
        {
            vm.GitState = result is null || vm.IsParent
                ? GitEntryState.None
                : result.Snapshot.StateOf(prefix + vm.Name, vm.IsDirectory);
        }
    }

    private void SyncMarks()
    {
        foreach (var vm in Entries)
            vm.IsMarked = _state.IsMarked(vm.Entry);
        OnPropertyChanged(nameof(MarkSummaryText));
    }
}
