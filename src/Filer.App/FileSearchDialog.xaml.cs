using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// ファイル検索ダイアログ(F)。基準ディレクトリ配下を再帰検索し、結果を一覧表示する。
/// 検索はバックグラウンドで実行し、発見バッチを一定間隔でまとめて一覧へ流し込む(UI を固めない)。
/// 「転送して閉じる」=結果をアクティブペインへ仮想一覧として表示、
/// 「ジャンプ」(ダブルクリック可)=選択項目のあるフォルダーへ移動。閉じた後の処理は呼び出し側が行う。
/// </summary>
public partial class FileSearchDialog : Window
{
    private readonly BulkObservableCollection<FileEntry> _items = new();
    private readonly DispatcherTimer _flushTimer;
    private readonly object _pendingGate = new();
    private List<FileEntry> _pending = new();

    private CancellationTokenSource? _cts;
    private Task? _searchTask;
    private IReadOnlyList<FileEntry> _results = Array.Empty<FileEntry>();

    /// <summary>内容検索(grep)のマッチ行(FullPath→行一覧)。選択ファイルのプレビュー表示に使う。</summary>
    private readonly Dictionary<string, IReadOnlyList<ContentMatchLine>> _matchLines =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _matchLinesGate = new();

    /// <summary>直近の検索が内容検索(grep)だったか(ファイル名検索なら false)。</summary>
    private bool _isContentSearch;

    /// <summary>非管理者起動時の高速検索(昇格ヘルパー)。管理者起動なら null(ボタンも出さない)。</summary>
    private readonly ElevatedSearchProxy? _elevatedProxy;

    /// <summary>検索結果(検索完了時は相対パス順)。</summary>
    public IReadOnlyList<FileEntry> Results => _results;

    /// <summary>「転送して閉じる」で閉じたか。</summary>
    public bool TransferRequested { get; private set; }

    /// <summary>「ジャンプ」で閉じた場合の対象項目。</summary>
    public FileEntry? JumpTarget { get; private set; }

    /// <summary>検索条件(転送時の仮想一覧ラベルに使う)。内容検索時は内容語(+ファイル名フィルタ)を表す。</summary>
    public string PatternText
    {
        get
        {
            var content = ContentQuery.Text.Trim();
            if (content.Length == 0) return Query.Text;   // ファイル名検索(従来)
            var nameFilter = Query.Text.Trim();
            return nameFilter.Length > 0 ? $"内容「{content}」 名前「{nameFilter}」" : $"内容「{content}」";
        }
    }

    /// <summary>最後に実行した検索の基準ディレクトリ(転送時の ".." の戻り先)。</summary>
    public string SearchedBaseDirectory { get; private set; } = string.Empty;

    public FileSearchDialog(string initialBaseDirectory, ElevatedSearchProxy? elevatedProxy = null)
    {
        InitializeComponent();
        BaseDir.Text = initialBaseDirectory;
        ResultsList.ItemsSource = _items;
        _elevatedProxy = elevatedProxy;
        // 非管理者起動(proxy あり)のときだけ高速検索ボタンを出す。
        if (elevatedProxy is not null)
            FastSearchButton.Visibility = Visibility.Visible;
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _flushTimer.Tick += (_, _) => FlushPending();
        Loaded += (_, _) => Query.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _flushTimer.Stop();
        base.OnClosed(e);
    }

    // ---- 検索の実行 ----

    private async void Search_Click(object sender, RoutedEventArgs e) => await StartSearchAsync(elevated: false);

    private async void FastSearch_Click(object sender, RoutedEventArgs e) => await StartSearchAsync(elevated: true);

    /// <summary>
    /// 検索を開始する。<paramref name="elevated"/>=true なら昇格ヘルパー経由(高速検索ボタン)、
    /// false なら通常どおりインプロセスで検索する(検索開始ボタン)。
    /// </summary>
    private async Task StartSearchAsync(bool elevated)
    {
        var baseDir = BaseDir.Text.Trim();
        if (!Directory.Exists(baseDir))
        {
            ShowError("基準ディレクトリが見つかりません");
            return;
        }

        // 内容(grep)欄に文字があれば内容検索。空ならファイル名検索(従来)。
        if (ContentQuery.Text.Trim().Length > 0)
        {
            await StartContentSearchAsync(baseDir);
            return;
        }

        if (FileCheck.IsChecked != true && DirCheck.IsChecked != true)
        {
            ShowError("「ファイル」「ディレクトリ」の少なくとも一方をONにしてください");
            return;
        }
        if (!FileSearcher.TryCreateMatcher(Query.Text, RegexCheck.IsChecked == true, out _, out var error))
        {
            ShowError($"正規表現が不正です: {error}");
            return;
        }

        await CancelRunningSearchAsync();

        var options = new FileSearchOptions(Query.Text, baseDir)
        {
            UseRegex = RegexCheck.IsChecked == true,
            IncludeFiles = FileCheck.IsChecked == true,
            IncludeDirectories = DirCheck.IsChecked == true,
            SearchArchives = ArchiveCheck.IsChecked == true,
        };
        _searchTask = RunSearchAsync(options, elevated);
        await _searchTask;
    }

    private async Task RunSearchAsync(FileSearchOptions options, bool elevated)
    {
        var cts = _cts = new CancellationTokenSource();
        _isContentSearch = false;
        PreviewPanel.Visibility = Visibility.Collapsed;
        PreviewList.ItemsSource = null;
        SearchedBaseDirectory = options.BaseDirectory;
        _results = Array.Empty<FileEntry>();
        _items.Clear();
        lock (_pendingGate) _pending.Clear();
        ShowError("");
        EngineText.Text = "";
        UpdateCount(searching: true);
        _flushTimer.Start();

        try
        {
            // onBatch はワーカースレッドから並行に呼ばれる。ここでは溜めるだけにして、
            // UI への反映は DispatcherTimer がまとめて行う(数十万件でも UI を固めない)。
            void Collect(IReadOnlyList<FileEntry> batch)
            {
                lock (_pendingGate) _pending.AddRange(batch);
            }

            // elevated=高速検索は昇格ヘルパー経由で MFT 索引を使う。それ以外はインプロセス走査。
            var result = elevated
                ? await _elevatedProxy!.SearchAsync(options, Collect, cts.Token)
                : await Task.Run(() => FileSearcher.SearchWithInfo(options, cts.Token, Collect));
            _results = result.Entries;
            // どのエンジンで検索したか(MFT が使えない理由を含む)を明示する。
            EngineText.Text = result.Engine == FileSearchEngine.MftIndex
                ? result.EngineNote ?? "MFT索引"
                : result.EngineNote ?? "通常走査";
        }
        catch (ElevationDeclinedException ex)
        {
            // UAC 拒否。ボタンは残し、理由だけ表示する(勝手な通常走査フォールバックはしない)。
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError($"検索に失敗しました: {ex.Message}");
        }
        finally
        {
            _flushTimer.Stop();
            if (ReferenceEquals(_cts, cts))
            {
                // 完了(キャンセル含む)時点の全結果をソート済みで表示し直す。
                lock (_pendingGate) _pending.Clear();
                _items.ReplaceAll(_results);
                UpdateCount(searching: false);
                TransferButton.IsEnabled = JumpButton.IsEnabled = _results.Count > 0;
            }
        }
    }

    // ---- 内容検索(grep) ----

    /// <summary>内容(grep)欄に基づき内容検索を開始する。ファイル名欄は対象ファイルの絞り込みに使う。</summary>
    private async Task StartContentSearchAsync(string baseDir)
    {
        var query = ContentQuery.Text;   // 末尾空白も検索語になりうるため Trim しない
        if (!FileSearcher.TryCreateMatcher(Query.Text, RegexCheck.IsChecked == true, out _, out var nameError))
        {
            ShowError($"ファイル名の正規表現が不正です: {nameError}");
            return;
        }
        if (!ContentSearcher.TryCreateLineMatcher(query, ContentRegexCheck.IsChecked == true,
                ContentCaseCheck.IsChecked == true, out _, out var contentError))
        {
            ShowError($"内容の検索条件が不正です: {contentError}");
            return;
        }

        await CancelRunningSearchAsync();

        var options = new ContentSearchOptions(query, baseDir)
        {
            NamePattern = Query.Text,
            NameUseRegex = RegexCheck.IsChecked == true,
            UseRegex = ContentRegexCheck.IsChecked == true,
            CaseSensitive = ContentCaseCheck.IsChecked == true,
        };
        _searchTask = RunContentSearchAsync(options);
        await _searchTask;
    }

    private async Task RunContentSearchAsync(ContentSearchOptions options)
    {
        var cts = _cts = new CancellationTokenSource();
        _isContentSearch = true;
        PreviewPanel.Visibility = Visibility.Visible;
        PreviewList.ItemsSource = null;
        SearchedBaseDirectory = options.BaseDirectory;
        _results = Array.Empty<FileEntry>();
        _items.Clear();
        lock (_pendingGate) _pending.Clear();
        lock (_matchLinesGate) _matchLines.Clear();
        ShowError("");
        EngineText.Text = "内容検索(grep)";
        UpdateCount(searching: true);
        _flushTimer.Start();

        try
        {
            // onMatch はワーカースレッドから並行に呼ばれる。発見ファイルは溜めるだけにし、
            // 一覧反映は DispatcherTimer がまとめて行う。マッチ行は即時に辞書へ入れプレビューに使う。
            void Collect(ContentMatch match)
            {
                lock (_pendingGate) _pending.Add(match.Entry);
                lock (_matchLinesGate) _matchLines[match.Entry.FullPath] = match.Lines;
            }

            var result = await Task.Run(() => ContentSearcher.SearchWithInfo(options, cts.Token, Collect));
            _results = result.Matches.Select(m => m.Entry).ToList();
            lock (_matchLinesGate)
            {
                _matchLines.Clear();
                foreach (var match in result.Matches)
                    _matchLines[match.Entry.FullPath] = match.Lines;
            }
        }
        catch (Exception ex)
        {
            ShowError($"内容検索に失敗しました: {ex.Message}");
        }
        finally
        {
            _flushTimer.Stop();
            if (ReferenceEquals(_cts, cts))
            {
                lock (_pendingGate) _pending.Clear();
                _items.ReplaceAll(_results);
                UpdateCount(searching: false);
                TransferButton.IsEnabled = JumpButton.IsEnabled = _results.Count > 0;
            }
        }
    }

    /// <summary>内容(grep)欄の有無で各コントロールの有効/表示を切り替える(内容検索はファイルのみ対象)。</summary>
    private void ContentQuery_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;   // InitializeComponent 中はまだ他コントロールが未生成
        var grep = ContentQuery.Text.Trim().Length > 0;
        ContentRegexCheck.IsEnabled = grep;
        ContentCaseCheck.IsEnabled = grep;
        // 内容検索はファイルのみ対象(ディレクトリ・書庫内 grep は非対応)。MFT 高速検索も名前検索専用。
        DirCheck.IsEnabled = !grep;
        ArchiveCheck.IsEnabled = !grep;
        if (_elevatedProxy is not null)
            FastSearchButton.Visibility = grep ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>実行中の検索があれば中断して終了を待つ。</summary>
    private async Task CancelRunningSearchAsync()
    {
        _cts?.Cancel();
        if (_searchTask is { } task)
        {
            try { await task; }
            catch (Exception) { /* 前回分の失敗はここでは扱わない */ }
        }
    }

    /// <summary>溜まった発見バッチを一覧へ反映する(検索中のみ)。</summary>
    private void FlushPending()
    {
        List<FileEntry>? taken = null;
        lock (_pendingGate)
        {
            if (_pending.Count > 0)
            {
                taken = _pending;
                _pending = new List<FileEntry>();
            }
        }
        if (taken is null) return;

        _items.AddRange(taken);
        UpdateCount(searching: true);
        if (!TransferButton.IsEnabled)
            TransferButton.IsEnabled = JumpButton.IsEnabled = true;
    }

    private void UpdateCount(bool searching)
    {
        if (_isContentSearch)
        {
            int files, lines;
            lock (_matchLinesGate)
            {
                files = _matchLines.Count;
                lines = _matchLines.Values.Sum(l => l.Count);
            }
            CountText.Text = (searching ? "検索中…  " : "") + $"{files:N0} ファイル / {lines:N0} 行一致";
            return;
        }
        var count = searching ? _items.Count : _results.Count;
        CountText.Text = searching ? $"検索中…  {count:N0} 個発見" : $"{count:N0} 個発見";
    }

    private void ShowError(string message) => ErrorText.Text = message;

    // ---- 閉じ方(転送・ジャンプ) ----

    private async void Transfer_Click(object sender, RoutedEventArgs e)
    {
        await CancelRunningSearchAsync();   // 検索中なら中断し、その時点までの結果を転送する
        if (_results.Count == 0) return;
        TransferRequested = true;
        DialogResult = true;
    }

    private async void Jump_Click(object sender, RoutedEventArgs e) => await JumpToSelection();

    private async void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is FileEntry)
            await JumpToSelection();
    }

    private async Task JumpToSelection()
    {
        var target = ResultsList.SelectedItem as FileEntry
            ?? (_items.Count > 0 ? _items[0] : null);
        if (target is null) return;
        await CancelRunningSearchAsync();
        JumpTarget = target;
        DialogResult = true;
    }

    private void ResultsList_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not null)
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);

        if (!_isContentSearch) return;
        if (ResultsList.SelectedItem is FileEntry entry)
        {
            IReadOnlyList<ContentMatchLine>? lines;
            lock (_matchLinesGate) _matchLines.TryGetValue(entry.FullPath, out lines);
            PreviewList.ItemsSource = lines?.Select(l => $"{l.LineNumber,6}: {l.Text}").ToList();
        }
        else
        {
            PreviewList.ItemsSource = null;
        }
    }

    /// <summary>マッチ行のダブルクリックで、その(選択中の)ファイルへジャンプする。</summary>
    private async void PreviewList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is FileEntry)
            await JumpToSelection();
    }
}
