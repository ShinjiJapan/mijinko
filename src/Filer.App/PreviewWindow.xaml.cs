using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Filer.App.ViewModels;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// アプリ内プレビューウィンドウ(全画面)。画像/テキスト/Markdown/PDF を表示する。
/// 画像表示中は ↑↓ でアクティブペインのカーソルを前後の画像へ移動し、表示も連動して切り替える。
/// Markdown は Markdig→HTML を WebView2 で描画する(mermaid 図に対応)。
/// Esc / Enter で閉じる。
/// </summary>
public partial class PreviewWindow : Window
{
    private const string PreviewHost = PreviewWebHost.Host;
    private const string DocHost = PreviewWebHost.DocHost;

    private readonly MainViewModel _main;
    private readonly PaneViewModel _pane;
    private readonly FrameworkElement _paneRegion;
    // 設定キー割り当て。表示切替・画像操作(コピー/移動/削除/再読込)を設定値どおりに効かせるため使う。
    private readonly KeyBindingMap _keyMap;
    // プレビュー種別ごとの表示形態(全画面/1ペイン)を記憶する永続ストア。null なら記憶しない。
    private readonly PreviewSizePreferenceStore? _sizePrefs;
    private PreviewKind _kind;
    private bool _isImage;
    // Markdown/Html を S キーでソース(テキスト)表示に切り替えているか。
    private bool _sourceMode;
    // WebView2 の初期化(メッセージ購読)済みか。
    private bool _webViewReady;
    // 仮想ホストにフォルダーをマップ済みか(レンダリング対象が変わるたびに張り替える)。
    private bool _hostMapped;
    // 表示対象自身のフォルダー(Markdown の相対画像解決用)をマップ済みか。
    private bool _docMapped;
    // 描画完了時に WebView へフォーカスを移すか。自前生成ページ(Markdig/highlight.js。S/Esc/表示切替 を
    // JS で受けネイティブスクロールさせる)は true、外部コンテンツ(HTML ブラウザ描画/PDF)は false。
    private bool _focusWebViewOnLoad;
    /// <summary>表示形態(表示切替キーで 全画面 ⇄ ペイン領域 をトグル)。</summary>
    private enum PreviewView { Maximized, PaneRegion }
    private PreviewView _view = PreviewView.Maximized;
    // true: 横並び2枚表示(左=カーソル画像/右=次の画像)。1 キーで切り替える。
    private bool _twoUp;
    // 画像ドラッグ開始の判定用。押下位置としきい値超えでアプリ外へのファイルドラッグを始める。
    private Point _dragStart;
    private bool _dragArmed;
    private FileEntry? _dragEntry;
    // 画像表示中、無操作が続いたらマウスカーソルを隠す。マウス移動で再表示し再カウントする。
    private readonly DispatcherTimer _cursorHideTimer;
    // 原寸画像(縮小元)。表示領域サイズに合わせて段階縮小したものを Image へ渡す(スクリーントーンのモアレ対策)。
    private BitmapSource? _originalLeft;
    private BitmapSource? _originalRight;
    // 直近に適用した縮小段数(-1=未適用/原寸入替時にリセット)。表示寸が段差をまたがない間は作り直さない。
    private int _stepsLeft = -1;
    private int _stepsRight = -1;

    /// <summary>編集キー(entry.edit)が押されて閉じたか。呼び出し側はこれを見て編集モードへ移る。</summary>
    public bool EditRequested { get; private set; }

    /// <summary>編集要求時のプレビューが全画面だったか(編集モードの表示形態を合わせるために使う)。</summary>
    public bool EditAsFullScreen { get; private set; }

    public PreviewWindow(MainViewModel main, FrameworkElement paneRegion, KeyBindingMap keyMap,
        bool startInPaneRegion = false, PreviewSizePreferenceStore? sizePrefs = null)
    {
        InitializeComponent();
        // 表示専用(テキストは読み取り専用)。日本語入力 ON でも 表示切替/Esc 等が効くよう IME を無効化する。
        Ime.Disable(this);
        _main = main;
        _pane = main.Active;
        _paneRegion = paneRegion;
        _keyMap = keyMap;
        _sizePrefs = sizePrefs;
        // Markdown / HTML はソース表示で開く(S キーでレンダリングへ切替)。
        _sourceMode = FilePreview.InitialSourceMode(FilePreview.ClassifyByExtension(_pane.SelectedItemPath));
        ShowCurrent();   // _kind を確定させる

        // 表示中の画像をアプリ外へドラッグ&ドロップ(コピー)できるようにする(一覧と同じ挙動)。
        ImagePanel.PreviewMouseLeftButtonDown += ImagePanel_PreviewMouseLeftButtonDown;
        ImagePanel.PreviewMouseMove += ImagePanel_PreviewMouseMove;
        ImagePanel.PreviewMouseLeftButtonUp += (_, _) => _dragArmed = false;
        // 表示領域サイズが変わったら(全画面⇄ペイン、ウィンドウ最大化アニメ等)縮小段数を見直す。
        ImagePanel.SizeChanged += (_, _) => ApplyScaledImages();

        // 画像表示中は無操作が続いたらカーソルを隠す(マウス移動で再表示)。画像以外では作動させない。
        _cursorHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _cursorHideTimer.Tick += (_, _) => HideCursor();
        if (_isImage)
        {
            MouseMove += (_, _) => OnImageMouseActivity();
            _cursorHideTimer.Start();
        }
        Closed += (_, _) => _cursorHideTimer.Stop();

        // 編集中の逆ペインプレビューは常にペイン領域。それ以外は種別ごとに記憶した表示形態で開く(既定は全画面)。
        _view = startInPaneRegion || (_sizePrefs is not null && !_sizePrefs.IsFullScreen(_kind))
            ? PreviewView.PaneRegion : PreviewView.Maximized;
        // 全画面・ペイン領域とも、フィラーと同じモニター上へ Loaded 後に配置する
        // (全画面を XAML 任せにするとプライマリーモニターで最大化され、サブモニター利用時に別画面へ飛ぶ)。
        Loaded += (_, _) => ApplyPreviewView();
    }

    /// <summary>ペインのカーソル位置の項目を、種別に応じて表示する。</summary>
    private void ShowCurrent()
    {
        var path = _pane.SelectedItemPath;
        var kind = FilePreview.ClassifyByExtension(path);
        _kind = kind;
        _isImage = kind == PreviewKind.Image;

        if (kind == PreviewKind.Image)
        {
            LoadImage(path);
            SetImageInfo(Path.GetFileName(path));
            return;
        }
        if (kind == PreviewKind.Markdown)
        {
            // ソース表示は highlight.js でシンタックスハイライトする。
            if (_sourceMode) _ = LoadCodeAsync(path); else _ = LoadMarkdownAsync(path);
        }
        else if (kind == PreviewKind.Html)
        {
            if (_sourceMode) _ = LoadCodeAsync(path); else _ = LoadHtmlAsync(path);
        }
        else if (kind == PreviewKind.Code)
        {
            // Code はレンダリング自体がハイライト表示。ソースはハイライト無しの素のテキスト。
            if (_sourceMode) LoadText(path); else _ = LoadCodeAsync(path);
        }
        else if (kind == PreviewKind.Pdf)
            _ = LoadPdfAsync(path);
        else if (kind == PreviewKind.Text)
            LoadText(path);

        var name = Path.GetFileName(path);
        var info = _sourceMode && IsToggleable(kind) ? $"{name}  [ソース]" : name;
        // 編集可能なテキストは編集キーのヒントを併記する(キーは設定に追従)。
        if (FilePreview.IsEditable(kind) && _keyMap.GesturesFor("entry.edit").FirstOrDefault() is { } editKey)
            info += $"   ({editKey}:編集)";
        InfoText.Text = info;
        Title = $"プレビュー — {name}";
    }

    /// <summary>パス(実ファイル/書庫内)から凍結済みビットマップを読み込む。</summary>
    private static BitmapSource LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (ArchivePath.TrySplit(path, out _, out _))
            bitmap.StreamSource = new MemoryStream(ArchiveExtractor.ReadEntryBytes(path));
        else
            bitmap.UriSource = new Uri(path);   // OnLoad なのでファイルはロックしない
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>左(カーソル)の画像を表示する。横並び時は右の2枚目も更新する。</summary>
    private void LoadImage(string path)
    {
        _originalLeft = LoadBitmap(path);
        _stepsLeft = -1;   // 原寸が入れ替わったので段数キャッシュを無効化
        ImagePanel.Visibility = Visibility.Visible;
        TextView.Visibility = Visibility.Collapsed;
        MarkdownView.Visibility = Visibility.Collapsed;
        UpdateSecondImage();
        ApplyScaledImages();
    }

    /// <summary>横並び表示中、カーソルの次の画像の原寸を保持する(無ければ空)。割り当ては ApplyScaledImages が行う。</summary>
    private void UpdateSecondImage()
    {
        _stepsRight = -1;   // 原寸が入れ替わったので段数キャッシュを無効化
        if (!_twoUp) { _originalRight = null; return; }
        var next = NextImageIndex(_pane.SelectedIndex, +1);
        _originalRight = next >= 0 ? LoadBitmap(_pane.Entries[next].Entry.FullPath) : null;
    }

    /// <summary>
    /// 原寸画像を表示領域サイズ(Uniform 後の実表示寸・DPI 考慮)に合わせて段階縮小し、Image へ割り当てる。
    /// 表示寸付近まで 2:1 の面積平均で縮めてから端数を高品質補間(Fant)に委ねることで、網点のモアレを抑える。
    /// </summary>
    private void ApplyScaledImages()
    {
        var w = ImagePanel.ActualWidth;
        var h = ImagePanel.ActualHeight;
        if (w <= 0 || h <= 0)   // レイアウト未確定。原寸のまま渡し、SizeChanged 後に縮小し直す。
        {
            ImageView.Source = _originalLeft;
            ImageView2.Source = _originalRight;
            _stepsLeft = _stepsRight = -1;
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        // 横並び時は各画像が領域の半分の幅に収まる。デバイス px へ換算して縮小先寸とする。
        var availW = (_twoUp ? w / 2 : w) * dpi;
        var availH = h * dpi;
        ImageView.Source = Downscaled(_originalLeft, availW, availH, ref _stepsLeft, ImageView.Source);
        ImageView2.Source = Downscaled(_originalRight, availW, availH, ref _stepsRight, ImageView2.Source);
    }

    /// <summary>
    /// 原寸 <paramref name="src"/> を、Stretch=Uniform での実表示寸まで 1/2 ずつ段階縮小して返す。
    /// 段数が前回と同じ(表示寸が段差をまたいでいない)なら作り直さず <paramref name="current"/> を保つ。
    /// 表示領域より小さい画像は縮小しない(拡大表示)。
    /// </summary>
    private static ImageSource? Downscaled(BitmapSource? src, double availW, double availH,
        ref int lastSteps, ImageSource? current)
    {
        if (src is null) { lastSteps = -1; return null; }
        var scale = Math.Min(availW / src.PixelWidth, availH / src.PixelHeight);
        var steps = scale >= 1.0
            ? 0
            : ImageDownscale.HalvingSteps(src.PixelWidth, src.PixelHeight,
                src.PixelWidth * scale, src.PixelHeight * scale);
        if (steps == lastSteps && current is not null) return current;
        lastSteps = steps;
        var result = (BitmapSource)src;
        for (var i = 0; i < steps; i++)
        {
            var halved = new TransformedBitmap(result, new ScaleTransform(0.5, 0.5));
            halved.Freeze();
            result = halved;
        }
        return result;
    }

    /// <summary>マウス操作があったらカーソルを再表示し、無操作タイマーを巻き戻す。</summary>
    private void OnImageMouseActivity()
    {
        if (Cursor == Cursors.None) Cursor = null;   // 既定(矢印)へ戻す
        _cursorHideTimer.Stop();
        _cursorHideTimer.Start();
    }

    /// <summary>無操作が続いたらカーソルを隠す。次のマウス移動まで隠し続ける。</summary>
    private void HideCursor()
    {
        Cursor = Cursors.None;
        _cursorHideTimer.Stop();   // 隠した後は移動があるまで再計測しない
    }

    /// <summary>画像上で押下したら、ドラッグ対象(押下した側の画像)を記録してドラッグを待機する。</summary>
    private void ImagePanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isImage) return;
        _dragStart = e.GetPosition(null);
        _dragEntry = ImageEntryFrom(e.OriginalSource as DependencyObject);
        _dragArmed = _dragEntry is not null;
    }

    /// <summary>しきい値を超えて移動したら、表示中の画像をアプリ外へドラッグ(コピー)する。</summary>
    private void ImagePanel_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragArmed = false;
        StartImageDrag(_dragEntry!);
    }

    /// <summary>押下した視覚要素から、ドラッグ対象の画像エントリを求める。横並び時は左=次の画像/右=カーソル画像。</summary>
    private FileEntry? ImageEntryFrom(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, ImageView2))
            {
                var next = NextImageIndex(_pane.SelectedIndex, +1);
                return next >= 0 ? _pane.Entries[next].Entry : null;
            }
            if (ReferenceEquals(source, ImageView))
                return _pane.Current;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    /// <summary>指定画像をアプリ外へドラッグ(コピー)する。書庫内画像は一時フォルダーへ抽出する。</summary>
    private void StartImageDrag(FileEntry entry)
    {
        string[] files;
        try
        {
            files = DragFileBuilder.Build(new[] { entry });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ドラッグの準備に失敗しました",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (files.Length == 0) return;

        var data = new DataObject(DataFormats.FileDrop, files);
        DragDrop.DoDragDrop(ImagePanel, data, DragDropEffects.Copy);   // 移動ではなくコピー
    }

    /// <summary>
    /// ヘッダーの情報とタイトルを更新する。横並び時は見た目どおり「左(次)| 右(現在)」の順で併記する。
    /// </summary>
    private void SetImageInfo(string currentName)
    {
        var text = currentName;
        if (_twoUp)
        {
            var next = NextImageIndex(_pane.SelectedIndex, +1);
            if (next >= 0)
                text = _pane.Entries[next].Entry.Name + "  |  " + currentName;
        }
        InfoText.Text = text;
        Title = $"プレビュー — {currentName}";
    }

    private void LoadText(string path)
    {
        TextView.Text = ReadPreviewText(path);
        TextView.Visibility = Visibility.Visible;
        ImagePanel.Visibility = Visibility.Collapsed;
        MarkdownView.Visibility = Visibility.Collapsed;
        // レンダリング(WebView)から S で切り替えた場合、フォーカスが隠れた WebView に残り
        // 次の S が WPF へ届かない。TextView へ移して S/スクロールを効かせる。
        TextView.Focus();
    }

    /// <summary>テキストを BOM 判定付きで読み込む(既定 UTF-8)。書庫内ファイルはストリームから読む。</summary>
    private static string ReadPreviewText(string path)
    {
        if (ArchivePath.TrySplit(path, out _, out _))
        {
            using var stream = new MemoryStream(ArchiveExtractor.ReadEntryBytes(path));
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        return File.ReadAllText(path);
    }

    /// <summary>WebView2 を表示し、画像/テキストを隠す。</summary>
    private void ShowWebView()
    {
        MarkdownView.Visibility = Visibility.Visible;
        ImagePanel.Visibility = Visibility.Collapsed;
        TextView.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// WebView2 環境を生成・初期化し、仮想ホストへ <paramref name="hostDir"/> をマップして CoreWebView2 を返す。
    /// <paramref name="docDir"/> を与えると <see cref="DocHost"/> をそのフォルダーへマップする(相対画像の解決用)。
    /// メッセージ購読は初回のみ行う。マップは描画対象が変わるたびに張り替える。
    /// </summary>
    private async Task<CoreWebView2> EnsureWebViewMappedAsync(string hostDir, string? docDir = null)
    {
        // 環境生成と初期化は初回のみ(再生成すると「別の環境で初期化済み」例外になる)。
        if (!_webViewReady)
        {
            var env = await PreviewWebHost.CreateEnvironmentAsync();
            await MarkdownView.EnsureCoreWebView2Async(env);
            var c = MarkdownView.CoreWebView2;
            // WebView2 にフォーカスがある間はキーが WPF 側へ届かないため、HTML 側の Esc/Enter 通知で閉じる。
            c.WebMessageReceived += OnWebViewMessage;
            // 自前生成ページ(Markdig/highlight.js)では WebView へフォーカスを移し、↑↓等のネイティブ
            // スクロールと JS 側の S/Esc/表示切替 処理を効かせる。外部コンテンツ(HTML/PDF)では移さない。
            c.NavigationCompleted += (_, _) =>
            {
                if (_focusWebViewOnLoad) MarkdownView.Focus();
            };
            _webViewReady = true;
        }
        var core = MarkdownView.CoreWebView2;
        // レンダリング対象が変わるたびに仮想ホストのマップを張り替える。
        if (_hostMapped) core.ClearVirtualHostNameToFolderMapping(PreviewHost);
        core.SetVirtualHostNameToFolderMapping(PreviewHost, hostDir, CoreWebView2HostResourceAccessKind.Allow);
        _hostMapped = true;

        // 表示対象自身のフォルダー(相対画像の解決用)。対象が変わるたびに張り替える。
        if (_docMapped) { core.ClearVirtualHostNameToFolderMapping(DocHost); _docMapped = false; }
        if (docDir is not null)
        {
            core.SetVirtualHostNameToFolderMapping(DocHost, docDir, CoreWebView2HostResourceAccessKind.Allow);
            _docMapped = true;
        }
        return core;
    }

    /// <summary>
    /// Markdown を Markdig で HTML 化し WebView2 で描画する。mermaid.js はローカル同梱を読み込む。
    /// HTML と mermaid.js を作業フォルダーへ書き出し、仮想ホスト経由で同一オリジンとして表示する。
    /// </summary>
    private async Task LoadMarkdownAsync(string path)
    {
        ShowWebView();
        _focusWebViewOnLoad = true;

        var markdown = ReadPreviewText(path);
        var previewDir = GetPreviewDir();
        EnsureMermaidScript(previewDir);
        CleanupOldPages(previewDir);
        var html = MarkdownRenderer.ToHtmlDocument(markdown, ThemeManager.CurrentMarkdownColors(),
            _keyMap.GesturesFor("view.toggleFullscreen"), EditGestures());
        // 実ファイルは相対画像(上位 ../ も含む)を解決し、必要なルートを仮想ホストへマップする。書庫内は対象外。
        string? docRoot = null;
        if (!ArchivePath.TrySplit(path, out _, out _) && Path.GetDirectoryName(path) is { } mdDir)
        {
            var rebased = MarkdownRenderer.RebaseImages(html, mdDir, $"https://{DocHost}/");
            html = rebased.Html;
            docRoot = rebased.MappedRoot;
        }
        var pageName = $"page-{Guid.NewGuid():N}.html";
        File.WriteAllText(Path.Combine(previewDir, pageName), html);

        try
        {
            var core = await EnsureWebViewMappedAsync(previewDir, docRoot);
            core.Navigate($"https://{PreviewHost}/{pageName}");
        }
        catch (Exception ex)
        {
            // WebView2 ランタイム未導入などは握りつぶさずユーザーへ通知する。
            MessageBox.Show(this,
                $"Markdown プレビューを表示できません(WebView2 ランタイムが必要です)。\n{ex.Message}",
                "プレビュー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// ソースコード/データ(JSON/XML/YAML 等)を highlight.js でシンタックスハイライト表示する。
    /// JSON は表示前に整形する。Markdown と同様に HTML を作業フォルダーへ書き出し、仮想ホスト経由で
    /// 同一オリジンとして表示する。highlight.js 一式とテーマ CSS はローカル同梱を読み込む。
    /// </summary>
    private async Task LoadCodeAsync(string path)
    {
        ShowWebView();
        _focusWebViewOnLoad = true;

        var code = CodeRenderer.FormatSource(path, ReadPreviewText(path));
        var previewDir = GetPreviewDir();
        EnsureHighlightAssets(previewDir);
        CleanupOldPages(previewDir);
        var pageName = $"page-{Guid.NewGuid():N}.html";
        File.WriteAllText(Path.Combine(previewDir, pageName),
            CodeRenderer.ToHtmlDocument(code, CodeRenderer.LanguageId(path), ThemeManager.CurrentMarkdownColors(),
                _keyMap.GesturesFor("view.toggleFullscreen"), EditGestures()));

        try
        {
            var core = await EnsureWebViewMappedAsync(previewDir);
            core.Navigate($"https://{PreviewHost}/{pageName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"コードプレビューを表示できません(WebView2 ランタイムが必要です)。\n{ex.Message}",
                "プレビュー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// HTML/XHTML/MHTML/SVG を WebView2 でレンダリング表示する。実ファイルは自身のフォルダーを
    /// 仮想ホストにマップし、相対参照の CSS/画像/JS も解決する。書庫内ファイルは単体ファイルとして
    /// 作業フォルダーへ展開して表示する(相対参照は再現しない)。PDF と同様 WebView へはフォーカスを
    /// 移さず、Esc/Enter での終了とウィンドウ側のスクロール操作(<see cref="ScrollPdf"/>)を維持する。
    /// </summary>
    private async Task LoadHtmlAsync(string path)
    {
        ShowWebView();
        _focusWebViewOnLoad = false;   // 外部 HTML はフォーカスを移さず WPF 側で Esc/スクロールを扱う。

        // 書庫内ファイルは単体ファイルとして作業フォルダーへ展開し、その実パスを使う。
        string hostDir, fileName, localPath;
        if (ArchivePath.TrySplit(path, out _, out _))
        {
            var previewDir = GetPreviewDir();
            CleanupOldWebDocs(previewDir);
            fileName = $"web-{Guid.NewGuid():N}{Path.GetExtension(path)}";
            localPath = Path.Combine(previewDir, fileName);
            File.WriteAllBytes(localPath, ArchiveExtractor.ReadEntryBytes(path));
            hostDir = previewDir;
        }
        else
        {
            hostDir = Path.GetDirectoryName(path)!;
            fileName = Path.GetFileName(path);
            localPath = path;
        }

        // MHTML(自己完結のMIMEアーカイブ)は https 仮想ホストではダウンロード扱いになるため、
        // Chromium がアーカイブとして解釈・描画できる file:// で開く。HTML/XHTML/SVG は相対参照を
        // 解決できるよう仮想ホスト経由で開く。
        var url = IsMhtml(path)
            ? new Uri(localPath).AbsoluteUri
            : $"https://{PreviewHost}/{Uri.EscapeDataString(fileName)}";

        try
        {
            var core = await EnsureWebViewMappedAsync(hostDir);
            core.Navigate(url);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"HTML プレビューを表示できません(WebView2 ランタイムが必要です)。\n{ex.Message}",
                "プレビュー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>拡張子が MHTML(.mht/.mhtml)か。</summary>
    private static bool IsMhtml(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".mht", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".mhtml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// PDF を WebView2 の組み込みビューアで表示する。実ファイル/書庫内ファイルとも作業フォルダーへ
    /// 書き出し、仮想ホスト経由で表示する。WebView へはフォーカスを移さず、Esc/Enter での終了と
    /// ウィンドウ側のキー操作を維持する。スクロールは ↑↓/PgUp/PgDn/Home/End(<see cref="ScrollPdf"/> が
    /// DevTools プロトコルでキー入力を送る)・マウスホイール・ビューアのツールバーで行う。
    /// </summary>
    private async Task LoadPdfAsync(string path)
    {
        MarkdownView.Visibility = Visibility.Visible;
        ImagePanel.Visibility = Visibility.Collapsed;
        TextView.Visibility = Visibility.Collapsed;

        var previewDir = GetPreviewDir();
        CleanupOldDocs(previewDir);
        var docName = $"doc-{Guid.NewGuid():N}.pdf";
        var docPath = Path.Combine(previewDir, docName);
        if (ArchivePath.TrySplit(path, out _, out _))
            File.WriteAllBytes(docPath, ArchiveExtractor.ReadEntryBytes(path));
        else
            File.Copy(path, docPath, overwrite: true);   // OnLoad 相当: 元ファイルをロックしない

        try
        {
            var env = await PreviewWebHost.CreateEnvironmentAsync();
            await MarkdownView.EnsureCoreWebView2Async(env);
            MarkdownView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                PreviewHost, previewDir, CoreWebView2HostResourceAccessKind.Allow);
            MarkdownView.CoreWebView2.Navigate($"https://{PreviewHost}/{docName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"PDF プレビューを表示できません(WebView2 ランタイムが必要です)。\n{ex.Message}",
                "プレビュー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// PDF ビューアへ移動キー(↑↓/PgUp/PgDn/Home/End)を DevTools プロトコル経由で送りスクロールさせる。
    /// WebView へ OS フォーカスを移さずに済むため、ウィンドウ側の Esc/Enter での終了を維持できる。
    /// </summary>
    private async void ScrollPdf(string key, int virtualKey)
    {
        var core = MarkdownView.CoreWebView2;
        if (core is null) return;
        string Event(string type) =>
            $"{{\"type\":\"{type}\",\"windowsVirtualKeyCode\":{virtualKey},\"key\":\"{key}\",\"code\":\"{key}\"}}";
        try
        {
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", Event("rawKeyDown"));
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", Event("keyUp"));
        }
        catch (Exception)
        {
            // プレビューを閉じる途中で CoreWebView2 が破棄された場合の競合は無視する。
        }
    }

    /// <summary>過去に書き出した一時 PDF(doc-*.pdf)を掃除する。失敗は無視(使用中の可能性)。</summary>
    private static void CleanupOldDocs(string previewDir)
    {
        foreach (var old in Directory.EnumerateFiles(previewDir, "doc-*.pdf"))
        {
            try { File.Delete(old); } catch (IOException) { }
        }
    }

    /// <summary>過去に書き出した一時 HTML 系(web-*)を掃除する。失敗は無視(使用中の可能性)。</summary>
    private static void CleanupOldWebDocs(string previewDir)
    {
        foreach (var old in Directory.EnumerateFiles(previewDir, "web-*"))
        {
            try { File.Delete(old); } catch (IOException) { }
        }
    }

    /// <summary>
    /// WebView2 内の通知(Esc/Enter=閉じる, cycle-view=表示切替, S=ソース切替)を処理する(↑↓等はブラウザのスクロールに委ねる)。
    /// Markdown/Code はレンダリング中に WebView がフォーカスを持つため、S は HTML 側から通知される。
    /// </summary>
    private void OnWebViewMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        switch (e.TryGetWebMessageAsString())
        {
            case "close": Dispatcher.Invoke(Close); break;
            case "cycle-view": Dispatcher.Invoke(CyclePreviewView); break;
            case "toggle-source": Dispatcher.Invoke(ToggleSource); break;
            case "request-edit": Dispatcher.Invoke(RequestEdit); break;
        }
    }

    /// <summary>編集キー(レンダリング表示中は HTML 側 JS から通知)で編集モードへ移る。閉じて呼び出し側に委ねる。
    /// 現在のプレビュー表示形態(全画面/ペイン領域)を引き継げるよう記録する。</summary>
    private void RequestEdit()
    {
        if (!FilePreview.IsEditable(_kind)) return;
        EditRequested = true;
        EditAsFullScreen = _view == PreviewView.Maximized;
        Close();
    }

    /// <summary>編集可能なら編集キー(entry.edit)のジェスチャ、編集不可なら空(HTML 側で編集キーを発火させない)。</summary>
    private IReadOnlyList<string> EditGestures() =>
        FilePreview.IsEditable(_kind) ? _keyMap.GesturesFor("entry.edit") : Array.Empty<string>();

    /// <summary>レンダリング ⇄ ソース表示を切り替える(S キー)。</summary>
    private void ToggleSource()
    {
        if (!IsToggleable(_kind)) return;
        _sourceMode = !_sourceMode;
        ShowCurrent();
    }

    private static string GetPreviewDir() => PreviewWebHost.PreviewDir();

    /// <summary>過去に書き出した一時 HTML(page-*.html)を掃除する。失敗は無視(使用中の可能性)。</summary>
    private static void CleanupOldPages(string previewDir)
    {
        foreach (var old in Directory.EnumerateFiles(previewDir, "page-*.html"))
        {
            try { File.Delete(old); } catch (IOException) { }
        }
    }

    /// <summary>埋め込みの mermaid.min.js を作業フォルダーへ展開する(既に最新ならスキップ)。</summary>
    private static void EnsureMermaidScript(string previewDir) =>
        ExtractEmbeddedResource("mermaid.min.js", Path.Combine(previewDir, "mermaid.min.js"));

    /// <summary>
    /// highlight.js 一式(本体・追加言語・テーマ CSS)を作業フォルダーへ展開する(既に最新ならスキップ)。
    /// テーマ CSS はダーク/ライトの両方を hl-dark.css / hl-light.css として展開し、文書側が選んで読み込む。
    /// </summary>
    private static void EnsureHighlightAssets(string previewDir)
    {
        ExtractEmbeddedResource("highlight.min.js", Path.Combine(previewDir, "highlight.min.js"));
        ExtractEmbeddedResource("powershell.min.js", Path.Combine(previewDir, "powershell.min.js"));
        ExtractEmbeddedResource("dos.min.js", Path.Combine(previewDir, "dos.min.js"));
        ExtractEmbeddedResource("apex.min.js", Path.Combine(previewDir, "apex.min.js"));
        ExtractEmbeddedResource("github-dark.min.css", Path.Combine(previewDir, "hl-dark.css"));
        ExtractEmbeddedResource("github.min.css", Path.Combine(previewDir, "hl-light.css"));
    }

    /// <summary>サフィックス一致で埋め込みリソースを探し、宛先へ展開する(同サイズが既にあればスキップ)。</summary>
    private static void ExtractEmbeddedResource(string resourceSuffix, string destPath)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = Array.Find(asm.GetManifestResourceNames(),
            n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"埋め込みリソース {resourceSuffix} が見つかりません。");

        using var resource = asm.GetManifestResourceStream(resourceName)!;
        if (File.Exists(destPath) && new FileInfo(destPath).Length == resource.Length) return;

        using var file = File.Create(destPath);
        resource.CopyTo(file);
    }

    /// <summary>
    /// 画像表示中の ↑↓: 次の画像ファイルへペインのカーソルを移動し、表示を切り替える。
    /// 横並び表示中は2枚分(ペア単位)進む。次の画像が無ければ何もしない。
    /// </summary>
    private void StepToAdjacentImage(int direction)
    {
        var steps = _twoUp ? 2 : 1;
        var index = _pane.SelectedIndex;
        for (var k = 0; k < steps; k++)
        {
            var next = NextImageIndex(index, direction);
            if (next < 0) break;     // これ以上画像が無ければそこで止まる
            index = next;
        }
        if (index != _pane.SelectedIndex)
            TryShowImageAt(index);
    }

    /// <summary>from から direction 方向で最初の画像エントリのインデックスを返す(無ければ -1)。</summary>
    private int NextImageIndex(int from, int direction)
    {
        var entries = _pane.Entries;
        for (var i = from + direction; i >= 0 && i < entries.Count; i += direction)
        {
            var entry = entries[i].Entry;
            if (!entry.IsDirectory && FilePreview.ClassifyByExtension(entry.FullPath) == PreviewKind.Image)
                return i;
        }
        return -1;
    }

    /// <summary>指定インデックスが画像なら表示してカーソルも合わせる。表示したら true。</summary>
    private bool TryShowImageAt(int index)
    {
        var entry = _pane.Entries[index].Entry;
        if (entry.IsDirectory || FilePreview.ClassifyByExtension(entry.FullPath) != PreviewKind.Image)
            return false;

        _pane.MoveCursorTo(index);   // ファイラーの選択も連動して移動(バインディング経由)
        LoadImage(entry.FullPath);
        SetImageInfo(entry.Name);
        return true;
    }

    /// <summary>
    /// 1 / End: 横並び2枚表示(右=カーソル画像/左=次の画像)をトグルする。
    /// 2枚表示時は右画像を左寄せにして、左画像(右寄せ)と中央の境目で隙間なく接する。
    /// </summary>
    private void ToggleTwoUp()
    {
        _twoUp = !_twoUp;
        if (_twoUp)
        {
            SecondCol.Width = new GridLength(1, GridUnitType.Star);
            ImageView2.Visibility = Visibility.Visible;
            ImageView.HorizontalAlignment = HorizontalAlignment.Left;   // 中央の境目へ寄せる
        }
        else
        {
            SecondCol.Width = new GridLength(0);
            ImageView2.Visibility = Visibility.Collapsed;
            ImageView2.Source = null;
            ImageView.HorizontalAlignment = HorizontalAlignment.Stretch; // 1枚は中央表示へ戻す
        }
        UpdateSecondImage();
        ApplyScaledImages();   // 1枚⇄2枚で各画像の表示幅が変わるため縮小段数を見直す
        SetImageInfo(_pane.Current.Name);
    }

    /// <summary>削除/移動でカレント項目が消えた後、カーソル位置以降→以前の順で次の画像を表示する。無ければ閉じる。</summary>
    private void ShowNearestImageOrClose()
    {
        var entries = _pane.Entries;
        if (entries.Count == 0) { Close(); return; }

        var start = _pane.SelectedIndex;
        for (var i = start; i < entries.Count; i++)
            if (TryShowImageAt(i)) return;
        for (var i = start - 1; i >= 0; i--)
            if (TryShowImageAt(i)) return;
        Close();   // 画像が残っていない
    }

    /// <summary>C: カレント(またはマーク群)を相手ペインへコピーする。確認なし。画像は残るので表示は維持。</summary>
    private void CopyToOther() => RunOp(_main.CopyToOther);

    /// <summary>M: 相手ペインへ移動する。確認なし。移動後は次の画像へ送るか、無ければ閉じる。</summary>
    private void MoveToOther()
    {
        if (RunOp(_main.MoveToOther))
            ShowNearestImageOrClose();
    }

    /// <summary>D/Delete: ごみ箱へ送る。確認なし。送出後は次の画像へ送るか、無ければ閉じる。</summary>
    private void DeleteCurrent()
    {
        if (RunOp(_main.DeleteTargets))
            ShowNearestImageOrClose();
    }

    /// <summary>
    /// 設定キーに割り当てられたアクションを処理する(処理したら true)。表示切替は全種別共通、
    /// コピー/移動/削除/再読込は画像表示中のみ。割り当ては設定(<see cref="KeyBindingMap"/>)に従う。
    /// </summary>
    private bool TryHandleBoundAction(Key key, ModifierKeys modifiers)
    {
        switch (KeyChordWpf.Resolve(_keyMap, key, modifiers))
        {
            case "view.toggleFullscreen":
                CyclePreviewView();
                return true;
            case "entry.edit" when FilePreview.IsEditable(_kind):   // 編集キー: 閉じて編集モードへ
                RequestEdit();
                return true;
            case "file.copy" when _isImage:        // コピー(確認なし)
                CopyToOther();
                return true;
            case "file.move" when _isImage:        // 移動(確認なし)
                MoveToOther();
                return true;
            case "file.delete" when _isImage:      // 削除(確認なし)
                DeleteCurrent();
                return true;
            case "view.reload" when _isImage:      // ペインを再読込し、現在位置の画像を再表示
                if (RunOp(_pane.Reload))
                    ShowNearestImageOrClose();
                return true;
            default:
                return false;
        }
    }

    /// <summary>表示切替キー: 表示形態を 全画面 ⇄ ペイン領域 でトグルする。</summary>
    private void CyclePreviewView()
    {
        _view = _view == PreviewView.Maximized ? PreviewView.PaneRegion : PreviewView.Maximized;
        ApplyPreviewView();
        // 反映後の実際の表示形態(ペイン領域が取れず全画面へ戻った場合も含む)を種別ごとに記憶する。
        _sizePrefs?.Set(_kind, _view == PreviewView.Maximized);
    }

    /// <summary>現在の表示形態をウィンドウ配置へ反映する。</summary>
    private void ApplyPreviewView()
    {
        switch (_view)
        {
            case PreviewView.Maximized:
                WindowState = WindowState.Normal;
                // フィラー(オーナー)と同じモニター上で全画面化する。Left=0;Top=0 だと常にプライマリー
                // モニターで最大化され、サブモニター利用時にプレビューだけ別画面へ飛ぶため、ペイン領域
                // (= フィラーのあるモニターのスクリーン座標)を起点にする。取得不可ならオーナー位置で代替。
                var anchor = GetPaneRegionRect();
                Left = anchor?.X ?? Owner?.Left ?? 0;
                Top = anchor?.Y ?? Owner?.Top ?? 0;
                WindowState = WindowState.Maximized;
                break;

            case PreviewView.PaneRegion:
                var rect = GetPaneRegionRect();
                if (rect is null) { WindowState = WindowState.Maximized; _view = PreviewView.Maximized; break; }
                WindowState = WindowState.Normal;
                Left = rect.Value.X;
                Top = rect.Value.Y;
                Width = rect.Value.Width;
                Height = rect.Value.Height;
                break;
        }
    }

    /// <summary>プレビューを重ねるペイン領域(反対側ペイン)のスクリーン矩形(DIP)を求める。取得できなければ null。</summary>
    private Rect? GetPaneRegionRect() => PreviewWebHost.GetPaneRegionRect(_paneRegion);

    /// <summary>操作を実行し、失敗はダイアログで通知する(プレビュー中は確認を求めない)。成功時 true。</summary>
    private bool RunOp(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "操作に失敗しました", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>レンダリング ⇄ ソース表示(S キー)を切り替えられる種別か。</summary>
    private static bool IsToggleable(PreviewKind kind) =>
        kind == PreviewKind.Markdown || kind == PreviewKind.Html || kind == PreviewKind.Code;

    /// <summary>TextView(プレーン)を表示中か。プレーンテキスト、または Code のソース表示時。
    /// Markdown/Html のソースは highlight.js(WebView)で表示するため含めない。</summary>
    private bool ShowingText => _kind == PreviewKind.Text
        || (_kind == PreviewKind.Code && _sourceMode);

    /// <summary>WebView へフォーカスを移さず DevTools 経由でスクロールさせる表示か(PDF / Html レンダリング)。</summary>
    private bool ShowingWebDoc => _kind == PreviewKind.Pdf
        || (_kind == PreviewKind.Html && !_sourceMode);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // 設定キーに割り当てられた操作(表示切替・画像のコピー/移動/削除/再読込)を先に処理する。
        if (TryHandleBoundAction(e.Key, Keyboard.Modifiers))
        {
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

            case Key.S when IsToggleable(_kind):   // ソース表示中(WPF にフォーカス)からの切替
                ToggleSource();
                e.Handled = true;
                break;

            case Key.Up when _isImage:
                StepToAdjacentImage(-1);
                e.Handled = true;
                break;

            case Key.Down when _isImage:
            case Key.Space when _isImage:    // スペースも↓と同様に次の画像へ
                StepToAdjacentImage(1);
                e.Handled = true;
                break;

            // テキスト(ソース)表示中のスクロール(カーソルは動かさずビューだけ動かす)。
            // Markdown レンダリング表示は WebView2 のネイティブスクロールに委ねる。
            case Key.Up when ShowingText:
                TextView.LineUp();
                e.Handled = true;
                break;

            case Key.Down when ShowingText:
                TextView.LineDown();
                e.Handled = true;
                break;

            case Key.PageUp when ShowingText:
                TextView.PageUp();
                e.Handled = true;
                break;

            case Key.PageDown when ShowingText:
                TextView.PageDown();
                e.Handled = true;
                break;

            case Key.Home when ShowingText:
                TextView.ScrollToHome();
                e.Handled = true;
                break;

            case Key.End when ShowingText:
                TextView.ScrollToEnd();
                e.Handled = true;
                break;

            // PDF / Html レンダリング表示中のスクロール。WebView へキー入力を送って動かす。
            case Key.Up when ShowingWebDoc:
                ScrollPdf("ArrowUp", 38);
                e.Handled = true;
                break;

            case Key.Down when ShowingWebDoc:
                ScrollPdf("ArrowDown", 40);
                e.Handled = true;
                break;

            case Key.PageUp when ShowingWebDoc:
                ScrollPdf("PageUp", 33);
                e.Handled = true;
                break;

            case Key.PageDown when ShowingWebDoc:
                ScrollPdf("PageDown", 34);
                e.Handled = true;
                break;

            case Key.Home when ShowingWebDoc:
                ScrollPdf("Home", 36);
                e.Handled = true;
                break;

            case Key.End when ShowingWebDoc:
                ScrollPdf("End", 35);
                e.Handled = true;
                break;

            case Key.D1 when _isImage:       // 1 / End: 横並び2枚表示のトグル
            case Key.NumPad1 when _isImage:
            case Key.End when _isImage:
                ToggleTwoUp();
                e.Handled = true;
                break;

            case Key.D4 when _isImage:       // 4: 進む(漫画の左方向。2枚表示時は2枚送り)
            case Key.NumPad4 when _isImage:
                StepToAdjacentImage(1);
                e.Handled = true;
                break;

            case Key.D6 when _isImage:       // 6: 戻る(漫画の右方向。2枚表示時は2枚送り)
            case Key.NumPad6 when _isImage:
                StepToAdjacentImage(-1);
                e.Handled = true;
                break;
        }
    }
}
