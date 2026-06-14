using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// 2ファイルの差分を side-by-side で表示するアプリ内ウィンドウ(全画面)。
/// 行差分を HTML 化し、<see cref="PreviewWindow"/> と同じ WebView2 + 仮想ホスト基盤で描画する。
/// Esc / Enter で閉じ、表示切替キー(設定値)で 全画面 ⇄ ペイン領域 を切り替える。
/// </summary>
public partial class DiffWindow : Window
{
    private readonly string _leftPath;
    private readonly string _rightPath;
    private readonly FrameworkElement? _paneRegion;
    // 設定キー割り当て。表示切替を設定値どおりに効かせるため使う(WPF/WebView 双方の判定)。
    private readonly KeyBindingMap _keyMap;

    /// <summary>表示形態(設定キーで 全画面 ⇄ ペイン領域 をトグル)。</summary>
    private enum ViewPlacement { Maximized, PaneRegion }
    private ViewPlacement _view = ViewPlacement.Maximized;

    public DiffWindow(string leftPath, string rightPath, FrameworkElement? paneRegion, KeyBindingMap keyMap)
    {
        InitializeComponent();
        Ime.Disable(this);   // 日本語入力 ON でも Esc/Enter/表示切替 が効くよう IME を無効化する。
        _leftPath = leftPath;
        _rightPath = rightPath;
        _paneRegion = paneRegion;
        _keyMap = keyMap;

        InfoText.Text = $"{Path.GetFileName(leftPath)}  ⇔  {Path.GetFileName(rightPath)}";
        Title = $"差分 — {Path.GetFileName(leftPath)} ⇔ {Path.GetFileName(rightPath)}";
        _ = LoadDiffAsync();
    }

    /// <summary>左右ファイルを読み、差分 HTML を作って WebView2 で描画する。</summary>
    private async Task LoadDiffAsync()
    {
        var left = DiffSource.Read(_leftPath);
        var right = DiffSource.Read(_rightPath);
        var leftName = Path.GetFileName(_leftPath);
        var rightName = Path.GetFileName(_rightPath);
        var colors = ThemeManager.CurrentMarkdownColors();
        var toggleGestures = _keyMap.GesturesFor("view.toggleFullscreen");

        string html;
        if (left.Kind == DiffContentKind.TooLarge || right.Kind == DiffContentKind.TooLarge)
        {
            html = DiffHtmlRenderer.SizeLimitNoticeDocument(leftName, rightName, DiffSource.DefaultMaxBytes, colors, toggleGestures);
        }
        else if (left.Kind == DiffContentKind.Binary || right.Kind == DiffContentKind.Binary)
        {
            var same = FilesEqual(_leftPath, _rightPath);
            html = DiffHtmlRenderer.BinaryNoticeDocument(leftName, rightName, same, colors, toggleGestures);
        }
        else
        {
            var rows = LineDiff.Compute(left.Lines, right.Lines);
            html = DiffHtmlRenderer.ToHtmlDocument(rows, leftName, rightName, colors, toggleGestures);
        }

        var previewDir = PreviewWebHost.PreviewDir();
        CleanupOldPages(previewDir);
        var pageName = $"diff-{Guid.NewGuid():N}.html";
        File.WriteAllText(Path.Combine(previewDir, pageName), html);

        try
        {
            var env = await PreviewWebHost.CreateEnvironmentAsync();
            await DiffView.EnsureCoreWebView2Async(env);
            // WebView2 にフォーカスがある間はキーが WPF 側へ届かないため、HTML 側の Esc/Enter/F1 通知で処理する。
            DiffView.CoreWebView2.WebMessageReceived += OnWebViewMessage;
            DiffView.CoreWebView2.NavigationCompleted += (_, _) => DiffView.Focus();
            DiffView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                PreviewWebHost.Host, previewDir, CoreWebView2HostResourceAccessKind.Allow);
            DiffView.CoreWebView2.Navigate($"https://{PreviewWebHost.Host}/{pageName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"差分を表示できません(WebView2 ランタイムが必要です)。\n{ex.Message}",
                "差分", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool FilesEqual(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }

    /// <summary>WebView2 内の通知(Esc/Enter=閉じる, F1=表示切替)を処理する。</summary>
    private void OnWebViewMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        switch (e.TryGetWebMessageAsString())
        {
            case "close": Dispatcher.Invoke(Close); break;
            case "cycle-view": Dispatcher.Invoke(CycleView); break;
        }
    }

    /// <summary>過去に書き出した一時 HTML(diff-*.html)を掃除する。失敗は無視(使用中の可能性)。</summary>
    private static void CleanupOldPages(string previewDir)
    {
        foreach (var old in Directory.EnumerateFiles(previewDir, "diff-*.html"))
        {
            try { File.Delete(old); } catch (IOException) { }
        }
    }

    /// <summary>表示形態を 全画面 ⇄ ペイン領域 でトグルする。</summary>
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

        // 表示切替(全画面 ⇄ ペイン領域)は設定キーに従う。
        if (KeyChordWpf.Resolve(_keyMap, e.Key, Keyboard.Modifiers) == "view.toggleFullscreen")
        {
            CycleView();
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
}
