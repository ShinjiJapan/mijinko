namespace Filer.Core;

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

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".csv", ".tsv",
        ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".conf", ".toml",
        ".css", ".js", ".ts", ".jsx", ".tsx",
        ".cs", ".csproj", ".sln", ".xaml", ".java", ".kt", ".go", ".rs",
        ".c", ".h", ".cpp", ".hpp", ".py", ".rb", ".php", ".sql",
        ".sh", ".ps1", ".bat", ".cmd", ".gitignore", ".editorconfig",
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
        if (TextExtensions.Contains(ext)) return PreviewKind.Text;
        if (string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase)) return PreviewKind.Pdf;
        return PreviewKind.None;
    }
}
