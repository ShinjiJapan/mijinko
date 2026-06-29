using System.Net;
using System.Text;
using System.Text.Json;

namespace Filer.Core;

/// <summary>
/// ソースコード/データファイルを highlight.js でシンタックスハイライトする完全な HTML 文書へ
/// 変換する(UI 非依存)。配色はテーマ追従の <see cref="ThemeColors"/> から背景色などを生成し、
/// トークン色は highlight.js のテーマ CSS(ローカル同梱 <c>hl-dark.css</c> / <c>hl-light.css</c>)に委ねる。
/// highlight.min.js と追加言語(powershell/dos/apex)はローカル同梱(相対参照)を読み込む前提。
/// </summary>
public static class CodeRenderer
{
    // 拡張子 → highlight.js 言語 ID。空文字は自動判定(plaintext 相当)。
    private static readonly Dictionary<string, string> LanguageByExtension =
        new(StringComparer.OrdinalIgnoreCase)
    {
        [".json"] = "json",
        [".xml"] = "xml", [".xaml"] = "xml", [".csproj"] = "xml",
        // ソース表示(S)用。Markdown/HTML は本来レンダリング表示だが、ソース表示時にハイライトする。
        [".md"] = "markdown", [".markdown"] = "markdown",
        [".html"] = "xml", [".htm"] = "xml", [".xhtml"] = "xml", [".svg"] = "xml",
        [".yaml"] = "yaml", [".yml"] = "yaml",
        [".ini"] = "ini", [".cfg"] = "ini", [".conf"] = "ini", [".toml"] = "ini",
        [".css"] = "css",
        [".js"] = "javascript", [".jsx"] = "javascript",
        [".ts"] = "typescript", [".tsx"] = "typescript",
        [".cs"] = "csharp",
        [".java"] = "java", [".kt"] = "kotlin", [".go"] = "go", [".rs"] = "rust",
        [".c"] = "c", [".h"] = "c", [".cpp"] = "cpp", [".hpp"] = "cpp",
        [".py"] = "python", [".rb"] = "ruby", [".php"] = "php", [".sql"] = "sql",
        [".sh"] = "bash", [".ps1"] = "powershell", [".bat"] = "dos", [".cmd"] = "dos",
        [".cls"] = "apex", [".apex"] = "apex",
    };

    /// <summary>拡張子から highlight.js の言語 ID を返す。未対応は空文字(自動判定)。</summary>
    public static string LanguageId(string path)
    {
        var ext = Path.GetExtension(path);
        return LanguageByExtension.TryGetValue(ext, out var id) ? id : string.Empty;
    }

    /// <summary>
    /// 表示前の整形。JSON は読みやすくインデント整形する(解析できないときは原文のまま)。
    /// それ以外の種別は原文をそのまま返す(ユーザーの整形を尊重する)。
    /// </summary>
    public static string FormatSource(string path, string content)
    {
        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            return content;

        try
        {
            using var doc = JsonDocument.Parse(content);
            return JsonSerializer.Serialize(doc.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            // 不正な JSON(コメント付き等)は整形せず原文を表示する(ハイライトは効く)。
            return content;
        }
    }

    /// <summary>
    /// ソース文字列を、指定テーマの背景色と highlight.js を含む完全な HTML 文書へ変換する。
    /// </summary>
    /// <summary>既定の表示切替ジェスチャ(設定で上書きしないときの値)。</summary>
    private static readonly string[] DefaultToggleGestures = { "F1" };
    /// <summary>既定のソース/表示モード切替ジェスチャ(設定で上書きしないときの値)。</summary>
    private static readonly string[] DefaultSourceToggleGestures = { "S" };
    /// <summary>既定の「閉じる」ジェスチャ(設定で上書きしないときの値)。</summary>
    private static readonly string[] DefaultCloseGestures = { "Escape", "Enter" };

    public static string ToHtmlDocument(string code, string languageId, ThemeColors colors) =>
        ToHtmlDocument(code, languageId, colors, DefaultToggleGestures);

    /// <summary>
    /// ソースを highlight.js で表示する完全な HTML 文書を生成する。
    /// <paramref name="fullscreenGestures"/> は表示切替(全画面⇄ペイン領域)を発火させるキー(設定値)。
    /// </summary>
    public static string ToHtmlDocument(
        string code, string languageId, ThemeColors colors, IReadOnlyList<string> fullscreenGestures) =>
        ToHtmlDocument(code, languageId, colors, fullscreenGestures, Array.Empty<string>());

    /// <summary>
    /// ソースを highlight.js で表示する完全な HTML 文書を生成する。
    /// <paramref name="editGestures"/> は編集モードへ移るキー(設定値)。空なら編集キーを発火させない。
    /// 表示モード切替(S)と閉じる(Esc/Enter)は既定キーを使う。
    /// </summary>
    public static string ToHtmlDocument(
        string code, string languageId, ThemeColors colors,
        IReadOnlyList<string> fullscreenGestures, IReadOnlyList<string> editGestures) =>
        ToHtmlDocument(code, languageId, colors, fullscreenGestures, editGestures,
            DefaultSourceToggleGestures, DefaultCloseGestures);

    /// <summary>
    /// ソースを highlight.js で表示する完全な HTML 文書を生成する。
    /// <paramref name="sourceToggleGestures"/> は表示モード切替、<paramref name="closeGestures"/> は
    /// 閉じるキー(いずれも設定値)。
    /// </summary>
    public static string ToHtmlDocument(
        string code, string languageId, ThemeColors colors,
        IReadOnlyList<string> fullscreenGestures, IReadOnlyList<string> editGestures,
        IReadOnlyList<string> sourceToggleGestures, IReadOnlyList<string> closeGestures)
    {
        var langClass = string.IsNullOrEmpty(languageId) ? string.Empty : $" class=\"language-{languageId}\"";
        var theme = colors.IsDark ? "hl-dark.css" : "hl-light.css";

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"ja\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<link rel=\"stylesheet\" href=\"").Append(theme).Append("\">\n");
        sb.Append("<style>\n").Append(BuildCss(colors)).Append("\n</style>\n</head>\n");
        sb.Append("<body>\n<pre><code").Append(langClass).Append('>');
        sb.Append(WebUtility.HtmlEncode(code));
        sb.Append("</code></pre>\n");
        sb.Append("<script src=\"highlight.min.js\"></script>\n");
        sb.Append("<script src=\"powershell.min.js\"></script>\n");
        sb.Append("<script src=\"dos.min.js\"></script>\n");
        sb.Append("<script src=\"apex.min.js\"></script>\n");
        sb.Append("<script>\n")
          .Append(BuildScript(fullscreenGestures, editGestures, sourceToggleGestures, closeGestures))
          .Append("\n</script>\n");
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    private static string BuildCss(ThemeColors c) => $@"
html, body {{ height: 100%; }}
body {{ margin: 0; background: {c.Background}; color: {c.Foreground}; }}
pre {{ margin: 0; min-height: 100vh; box-sizing: border-box; }}
pre code.hljs {{ font-family: 'Consolas', 'MS Gothic', monospace; font-size: 13px;
       line-height: 1.5; padding: 12px 16px; min-height: 100vh; box-sizing: border-box; }}
";

    // highlight.js を走らせ、表示切替キー(設定値)=表示形態切替・編集キー(設定値)=編集モード・
    // 表示モード切替キー(設定値)=ソース切替・閉じるキー(設定値)=閉じる をホストへ通知する。
    private static string BuildScript(
        IReadOnlyList<string> gestures, IReadOnlyList<string> editGestures,
        IReadOnlyList<string> sourceToggleGestures, IReadOnlyList<string> closeGestures) => $@"
hljs.highlightAll();
document.addEventListener('keydown', function (e) {{
  if ({KeyChordJs.MatchExpression(gestures, "e")}) {{   // 表示形態の切替(全画面 ⇄ ペイン領域)をホストへ通知
    e.preventDefault();
    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('cycle-view');
    return;
  }}
  if ({KeyChordJs.MatchExpression(editGestures, "e")}) {{   // 編集キー: 編集モードへ移るようホストへ通知
    e.preventDefault();
    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('request-edit');
    return;
  }}
  if ({KeyChordJs.MatchExpression(sourceToggleGestures, "e")}) {{   // 表示モード切替をホストへ通知
    e.preventDefault();
    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('toggle-source');
    return;
  }}
  if (!({KeyChordJs.MatchExpression(closeGestures, "e")})) return;
  if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('close');
}});
";
}
