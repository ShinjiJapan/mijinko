using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// 2ファイルの差分を side-by-side で表示するアプリ内ウィンドウ(全画面)。
/// 行差分を HTML 化し、<see cref="PreviewWindow"/> と同じ WebView2 + 仮想ホスト基盤で描画する。
/// Esc / Enter で閉じ、F1 で 全画面 ⇄ ペイン領域 を切り替える。
/// </summary>
public partial class DiffWindow : Window
{
    private readonly string _leftPath;
    private readonly string _rightPath;
    private readonly FrameworkElement? _paneRegion;

    /// <summary>表示形態(F1 で 全画面 ⇄ ペイン領域 をトグル)。</summary>
    private enum ViewPlacement { Maximized, PaneRegion }
    private ViewPlacement _view = ViewPlacement.Maximized;

    public DiffWindow(string leftPath, string rightPath, FrameworkElement? paneRegion)
    {
        InitializeComponent();
        Ime.Disable(this);   // 日本語入力 ON でも Esc/Enter/F1 が効くよう IME を無効化する。
        _leftPath = leftPath;
        _rightPath = rightPath;
        _paneRegion = paneRegion;

        InfoText.Text = $"{Path.GetFileName(leftPath)}  ⇔  {Path.GetFileName(rightPath)}";
        Title = $"差分 — {Path.GetFileName(leftPath)} ⇔ {Path.GetFileName(rightPath)}";
        _ = LoadDiffAsync();
    }

    /// <summary>左右ファイルを読み、差分 HTML を作って WebView2 で描画する。</summary>
    private async Task LoadDiffAsync()
    {
        var (leftBinary, leftLines) = DiffSource.ReadLines(_leftPath);
        var (rightBinary, rightLines) = DiffSource.ReadLines(_rightPath);

        string html;
        if (leftBinary || rightBinary)
        {
            var same = FilesEqual(_leftPath, _rightPath);
            html = DiffHtmlRenderer.BinaryNoticeDocument(
                Path.GetFileName(_leftPath), Path.GetFileName(_rightPath), same,
                ThemeManager.CurrentMarkdownColors());
        }
        else
        {
            var rows = LineDiff.Compute(leftLines, rightLines);
            html = DiffHtmlRenderer.ToHtmlDocument(rows,
                Path.GetFileName(_leftPath), Path.GetFileName(_rightPath),
                ThemeManager.CurrentMarkdownColors());
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

    /// <summary>F1: 表示形態を 全画面 ⇄ ペイン領域 でトグルする。</summary>
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
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Enter:
                Close();
                e.Handled = true;
                break;
            case Key.F1:
                CycleView();
                e.Handled = true;
                break;
        }
    }
}
