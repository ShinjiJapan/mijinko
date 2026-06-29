namespace Filer.Core;

/// <summary>
/// Markdown/HTML/Code プレビューの表示モード。Markdown/HTML は3モードを巡回でき、
/// Code は <see cref="Highlight"/>(ハイライト)と <see cref="Text"/>(素のテキスト)の2モード。
/// </summary>
public enum MarkupPreviewMode
{
    /// <summary>レンダリング後のプレビュー(Markdown=HTML描画 / HTML=ブラウザ描画)。</summary>
    Rendered,
    /// <summary>シンタックスハイライト表示(highlight.js)。</summary>
    Highlight,
    /// <summary>素のテキスト(等幅プレーン)。</summary>
    Text,
}

/// <summary>アプリ内プレビューの種別。</summary>
public enum PreviewKind
{
    /// <summary>プレビュー非対応。</summary>
    None,
    /// <summary>画像。</summary>
    Image,
    /// <summary>テキスト。</summary>
    Text,
    /// <summary>Markdown(レンダリング表示)。</summary>
    Markdown,
    /// <summary>ソースコード/データ(シンタックスハイライト表示。S キーでソース表示と切替)。</summary>
    Code,
    /// <summary>PDF(WebView2 の組み込みビューアで表示)。</summary>
    Pdf,
    /// <summary>HTML/XHTML/MHTML/SVG(WebView2 でレンダリング表示。S キーでソース表示と切替)。</summary>
    Html,
}

/// <summary>
/// ファイル拡張子からアプリ内プレビューの種別を判定する(UI 非依存)。
/// </summary>
public static class FilePreview
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff", ".webp",
    };

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown",
    };

    // 文法ハイライトが効かない/不要なプレーンテキスト(等幅プレーン表示)。
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".csv", ".tsv", ".sln", ".gitignore", ".editorconfig",
    };

    // シンタックスハイライト対象のソースコード/データ(WebView2 + highlight.js で表示)。
    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".conf", ".toml",
        ".css", ".js", ".ts", ".jsx", ".tsx",
        ".cs", ".csproj", ".xaml", ".java", ".kt", ".go", ".rs",
        ".c", ".h", ".cpp", ".hpp", ".py", ".rb", ".php", ".sql",
        ".sh", ".ps1", ".bat", ".cmd",
        ".cls", ".apex",
    };

    private static readonly HashSet<string> HtmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".xhtml", ".mht", ".mhtml", ".svg",
    };

    /// <summary>拡張子からプレビュー種別を判定する。対応しない場合は <see cref="PreviewKind.None"/>。</summary>
    public static PreviewKind ClassifyByExtension(string path)
    {
        if (string.IsNullOrEmpty(path)) return PreviewKind.None;
        var ext = Path.GetExtension(path);
        if (ImageExtensions.Contains(ext)) return PreviewKind.Image;
        if (MarkdownExtensions.Contains(ext)) return PreviewKind.Markdown;
        if (HtmlExtensions.Contains(ext)) return PreviewKind.Html;
        if (CodeExtensions.Contains(ext)) return PreviewKind.Code;
        if (TextExtensions.Contains(ext)) return PreviewKind.Text;
        if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase)) return PreviewKind.Pdf;
        return PreviewKind.None;
    }

    /// <summary>
    /// プレビュー表示開始時の表示モード。Markdown / HTML は設定値(<paramref name="markupDefault"/>)で開く。
    /// Code はハイライト表示を初期とする。その他はモードを持たない(<see cref="MarkupPreviewMode.Rendered"/>)。
    /// </summary>
    public static MarkupPreviewMode InitialMode(PreviewKind kind, MarkupPreviewMode markupDefault) => kind switch
    {
        PreviewKind.Markdown or PreviewKind.Html => markupDefault,
        PreviewKind.Code => MarkupPreviewMode.Highlight,
        _ => MarkupPreviewMode.Rendered,
    };

    /// <summary>
    /// 表示切替キー(S)を押したときの次のモード。Markdown / HTML は
    /// レンダリング→ハイライト→テキスト→… と巡回。Code はハイライト⇔テキストの2モード。
    /// その他は変化しない。
    /// </summary>
    public static MarkupPreviewMode NextMode(PreviewKind kind, MarkupPreviewMode mode) => kind switch
    {
        PreviewKind.Markdown or PreviewKind.Html => (MarkupPreviewMode)(((int)mode + 1) % 3),
        PreviewKind.Code => mode == MarkupPreviewMode.Text ? MarkupPreviewMode.Highlight : MarkupPreviewMode.Text,
        _ => mode,
    };

    /// <summary>表示モードの短いラベル(ヘッダー併記用)。</summary>
    public static string ModeLabel(MarkupPreviewMode mode) => mode switch
    {
        MarkupPreviewMode.Rendered => "プレビュー",
        MarkupPreviewMode.Highlight => "ハイライト",
        MarkupPreviewMode.Text => "テキスト",
        _ => "",
    };

    /// <summary>アプリ内テキストエディターで編集できる種別か(テキスト系のみ。画像・PDF は対象外)。</summary>
    public static bool IsEditable(PreviewKind kind) =>
        kind is PreviewKind.Text or PreviewKind.Markdown or PreviewKind.Code or PreviewKind.Html;

    /// <summary>
    /// レンダリング表示(プレビュー)を持つ種別か。編集中に逆ペインでプレビューできる対象の判定に使う
    /// (Markdown=HTML 描画 / HTML=ブラウザ描画 / Code=シンタックスハイライト表示)。
    /// </summary>
    public static bool HasRenderedPreview(PreviewKind kind) =>
        kind is PreviewKind.Markdown or PreviewKind.Html or PreviewKind.Code;

    /// <summary>表示モード(レンダリング/ハイライト/テキスト)を切り替えられる種別か。</summary>
    public static bool IsModeToggleable(PreviewKind kind) =>
        kind is PreviewKind.Markdown or PreviewKind.Html or PreviewKind.Code;
}
