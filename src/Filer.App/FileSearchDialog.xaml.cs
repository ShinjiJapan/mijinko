using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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

    /// <summary>検索結果(検索完了時は相対パス順)。</summary>
    public IReadOnlyList<FileEntry> Results => _results;

    /// <summary>「転送して閉じる」で閉じたか。</summary>
    public bool TransferRequested { get; private set; }

    /// <summary>「ジャンプ」で閉じた場合の対象項目。</summary>
    public FileEntry? JumpTarget { get; private set; }

    /// <summary>検索条件(転送時の仮想一覧ラベルに使う)。</summary>
    public string PatternText => Query.Text;

    /// <summary>最後に実行した検索の基準ディレクトリ(転送時の ".." の戻り先)。</summary>
    public string SearchedBaseDirectory { get; private set; } = string.Empty;

    public FileSearchDialog(string initialBaseDirectory)
    {
        InitializeComponent();
        BaseDir.Text = initialBaseDirectory;
        ResultsList.ItemsSource = _items;
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

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        var baseDir = BaseDir.Text.Trim();
        if (!Directory.Exists(baseDir))
        {
            ShowError("基準ディレクトリが見つかりません");
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
        _searchTask = RunSearchAsync(options);
        await _searchTask;
    }

    private async Task RunSearchAsync(FileSearchOptions options)
    {
        var cts = _cts = new CancellationTokenSource();
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
            var result = await Task.Run(() => FileSearcher.SearchWithInfo(options, cts.Token,
                batch => { lock (_pendingGate) _pending.AddRange(batch); }));
            _results = result.Entries;
            // どのエンジンで検索したか(MFT が使えない理由を含む)を明示する。
            EngineText.Text = result.Engine == FileSearchEngine.MftIndex
                ? result.EngineNote ?? "MFT索引"
                : result.EngineNote ?? "通常走査";
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
    }
}
