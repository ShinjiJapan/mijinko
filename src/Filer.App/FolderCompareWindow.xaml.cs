using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Filer.Core;
using Filer.App.ViewModels;

namespace Filer.App;

/// <summary>
/// 2つのフォルダーを再帰比較し、結果のツリーを side-by-side で表示するアプリ内ウィンドウ(全画面)。
/// <see cref="DiffWindow"/> と同じ WebView2 + 仮想ホスト基盤で描画する。変更ファイルの行クリックで 2ファイル差分を開き、
/// Esc/Enter で閉じ、表示切替キー(設定値)で 全画面 ⇄ ペイン領域 を切り替える。
/// </summary>
public partial class FolderCompareWindow : Window
{
    private readonly string _leftPath;
    private readonly string _rightPath;
    private readonly FolderCompareOptions _options;
    private readonly FrameworkElement? _paneRegion;
    private readonly KeyBindingMap _keyMap;
    private readonly MainViewModel _main;
    private readonly CancellationTokenSource _cts = new();

    // 転送対象を集めるための比較結果(ShowSame 絞り込み前の全件)と集計。比較完了後に確定。
    private IReadOnlyList<FolderCompareNode> _nodes = Array.Empty<FolderCompareNode>();
    private FolderCompareSummary _summary = new(0, 0, 0, 0);
    private bool _ready;

    // 「転送して閉じる」のキー(ファイル検索の T と同じ。ウィンドウ固有でフォルダー比較中のみ働く)。
    private static readonly string[] TransferGestures = { "T" };

    private enum ViewPlacement { Maximized, PaneRegion }
    private ViewPlacement _view = ViewPlacement.Maximized;

    public FolderCompareWindow(
        string leftPath, string rightPath, FolderCompareOptions options,
        FrameworkElement? paneRegion, KeyBindingMap keyMap, MainViewModel main)
    {
        InitializeComponent();
        Ime.Disable(this);
        _leftPath = leftPath;
        _rightPath = rightPath;
        _options = options;
        _paneRegion = paneRegion;
        _keyMap = keyMap;
        _main = main;

        InfoText.Text = $"比較中... {leftPath}  ⇔  {rightPath}";
        Title = $"フォルダー比較 — {Path.GetFileName(leftPath.TrimEnd('\\'))} ⇔ {Path.GetFileName(rightPath.TrimEnd('\\'))}";
        _ = LoadAsync();
    }

    /// <summary>両フォルダーを比較し、ツリー HTML を作って WebView2 で描画する。</summary>
    private async Task LoadAsync()
    {
        IReadOnlyList<FolderCompareNode> nodes;
        FolderCompareSummary summary;
        try
        {
            var source = new FileSystemFolderCompareSource();
            // 比較(内容ハッシュは重いので) はバックグラウンドで実行する。
            (nodes, summary) = await Task.Run(() =>
            {
                var n = FolderComparer.Compare(_leftPath, _rightPath, _options, source, _cts.Token);
                return (n, FolderComparer.Summarize(n));
            }, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;   // ウィンドウが閉じられた
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"フォルダー比較に失敗しました。\n{ex.Message}",
                "フォルダー比較", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }

        _nodes = nodes;
        _summary = summary;
        _ready = true;

        var display = _options.ShowSame ? nodes : FolderComparer.FilterDifferencesOnly(nodes);
        var colors = ThemeManager.CurrentMarkdownColors();
        var toggleGestures = _keyMap.GesturesFor("view.toggleFullscreen");
        var html = FolderCompareHtmlRenderer.ToHtmlDocument(
            display, _leftPath, _rightPath, colors, toggleGestures, TransferGestures);

        InfoText.Text = $"{_leftPath}  ⇔  {_rightPath}    " +
                        $"変更 {summary.Modified} / 左のみ {summary.LeftOnly} / 右のみ {summary.RightOnly} / 同一 {summary.Same}" +
                        "    [T] 転送して閉じる";

        var previewDir = PreviewWebHost.PreviewDir();
        CleanupOldPages(previewDir);
        var pageName = $"foldercmp-{Guid.NewGuid():N}.html";
        File.WriteAllText(Path.Combine(previewDir, pageName), html);

        try
        {
            var env = await PreviewWebHost.CreateEnvironmentAsync();
            await CompareView.EnsureCoreWebView2Async(env);
            CompareView.CoreWebView2.WebMessageReceived += OnWebViewMessage;
            CompareView.CoreWebView2.NavigationCompleted += (_, _) => CompareView.Focus();
            CompareView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                PreviewWebHost.Host, previewDir, CoreWebView2HostResourceAccessKind.Allow);
            CompareView.CoreWebView2.Navigate($"https://{PreviewWebHost.Host}/{pageName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"比較結果を表示できません(WebView2 ランタイムが必要です)。\n{ex.Message}",
                "フォルダー比較", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>WebView2 内の通知(行クリック=差分, Esc/Enter=閉じる, 表示切替)を処理する。</summary>
    private void OnWebViewMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = e.TryGetWebMessageAsString();
        if (msg is null) return;

        if (msg == "close") { Dispatcher.Invoke(Close); return; }
        if (msg == "cycle-view") { Dispatcher.Invoke(CycleView); return; }
        if (msg == "transfer") { Dispatcher.Invoke(ShowTransferDialog); return; }

        if (msg.StartsWith("diff" + FolderCompareHtmlRenderer.DiffSeparator, StringComparison.Ordinal))
        {
            var parts = msg.Split(FolderCompareHtmlRenderer.DiffSeparator);
            if (parts.Length == 3)
                Dispatcher.Invoke(() => OpenDiff(parts[1], parts[2]));
        }
    }

    /// <summary>
    /// 「転送して閉じる」ダイアログを表示し、選んだ差分/重複を各ペインへ仮想一覧として転送して閉じる。
    /// </summary>
    private void ShowTransferDialog()
    {
        if (!_ready) return;   // 比較完了前は無視

        var dialog = new FolderCompareTransferDialog(_summary) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var sel = dialog.Selection;
        if (!sel.Any) { Close(); return; }

        if (sel.LeftDifferences || sel.LeftSame)
            TransferSide(FolderCompareSide.Left, _leftPath, sel.LeftDifferences, sel.LeftSame);
        if (sel.RightDifferences || sel.RightSame)
            TransferSide(FolderCompareSide.Right, _rightPath, sel.RightDifferences, sel.RightSame);

        Close();
    }

    /// <summary>片側の選択を集めて FileEntry 化し、対応ペインへ転送する。</summary>
    private void TransferSide(FolderCompareSide side, string baseDir, bool differences, bool same)
    {
        var items = FolderCompareTransfer.Collect(_nodes, side, differences, same);
        var entries = new List<FileEntry>(items.Count);
        foreach (var item in items)
        {
            var info = new FileInfo(item.FullPath);
            if (!info.Exists) continue;   // 比較後に消えた項目は除く
            entries.Add(new FileEntry(item.RelativePath, item.FullPath, IsDirectory: false,
                info.Length, info.LastWriteTime));
        }

        var label = $"比較({(side == FolderCompareSide.Left ? "左" : "右")}): {DescribeKinds(differences, same)}";
        _main.TransferComparisonToPane(side, label, baseDir, entries);
    }

    private static string DescribeKinds(bool differences, bool same) =>
        differences && same ? "差分+重複" : differences ? "差分" : "重複";

    /// <summary>変更ファイルの 2ファイル差分を開く(比較ウィンドウと同じペイン領域に重ねる)。</summary>
    private void OpenDiff(string leftPath, string rightPath)
    {
        var window = new DiffWindow(leftPath, rightPath, _paneRegion, _keyMap) { Owner = this };
        window.ShowDialog();
    }

    private static void CleanupOldPages(string previewDir)
    {
        foreach (var old in Directory.EnumerateFiles(previewDir, "foldercmp-*.html"))
        {
            try { File.Delete(old); } catch (IOException) { }
        }
    }

    private void CycleView()
    {
        _view = _view == ViewPlacement.Maximized ? ViewPlacement.PaneRegion : ViewPlacement.Maximized;
        ApplyView();
    }

    private void ApplyView()
    {
        switch (_view)
        {
            case ViewPlacement.Maximized:
                WindowState = WindowState.Normal;
                Left = 0; Top = 0;
                WindowState = WindowState.Maximized;
                break;

            case ViewPlacement.PaneRegion:
                var rect = PreviewWebHost.GetPaneRegionRect(_paneRegion);
                if (rect is null) { WindowState = WindowState.Maximized; _view = ViewPlacement.Maximized; break; }
                WindowState = WindowState.Normal;
                Left = rect.Value.X;
                Top = rect.Value.Y;
                Width = rect.Value.Width;
                Height = rect.Value.Height;
                break;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (KeyChordWpf.Resolve(_keyMap, e.Key, Keyboard.Modifiers) == "view.toggleFullscreen")
        {
            CycleView();
            e.Handled = true;
            return;
        }
        // 転送して閉じる(T 固定。ファイル検索ダイアログと同じローカルキー)。
        if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.None)
        {
            ShowTransferDialog();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
            case Key.Enter:
                Close();
                e.Handled = true;
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        base.OnClosed(e);
    }
}
