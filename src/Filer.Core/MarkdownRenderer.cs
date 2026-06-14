using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Filer.Core;

/// <summary>
/// Markdown をプレビュー用の完全な HTML 文書へ変換する(UI 非依存)。
/// CSS の配色は <see cref="ThemeColors"/> から生成し、<c>```mermaid</c> フェンスは
/// mermaid.js が走査できる <c>&lt;pre class="mermaid"&gt;</c> に変換する。
/// mermaid.js はローカル同梱(相対参照 <c>mermaid.min.js</c>)を読み込む前提。
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>Markdown 本文を HTML 断片へ変換する(文書ラッパー無し)。</summary>
    public static string RenderBodyHtml(string markdown)
    {
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        // mermaid フェンスだけ専用描画に差し替える。
        renderer.ObjectRenderers.Replace<CodeBlockRenderer>(new MermaidCodeBlockRenderer());

        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>既定の表示切替ジェスチャ(設定で上書きしないときの値)。</summary>
    private static readonly string[] DefaultToggleGestures = { "F1" };

    /// <summary>Markdown を、既定(ダーク)テーマ CSS と mermaid 初期化を含む完全な HTML 文書へ変換する。</summary>
    public static string ToHtmlDocument(string markdown) => ToHtmlDocument(markdown, ThemeColors.Dark);

    /// <summary>Markdown を、指定テーマの CSS と mermaid 初期化を含む完全な HTML 文書へ変換する。</summary>
    public static string ToHtmlDocument(string markdown, ThemeColors colors) =>
        ToHtmlDocument(markdown, colors, DefaultToggleGestures);

    /// <summary>
    /// Markdown を、指定テーマの CSS と mermaid 初期化を含む完全な HTML 文書へ変換する。
    /// <paramref name="fullscreenGestures"/> は表示切替(全画面⇄ペイン領域)を発火させるキー(設定値)。
    /// </summary>
    public static string ToHtmlDocument(string markdown, ThemeColors colors, IReadOnlyList<string> fullscreenGestures)
    {
        var body = RenderBodyHtml(markdown);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"ja\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<style>\n").Append(BuildCss(colors)).Append("\n</style>\n</head>\n");
        sb.Append("<body>\n<article class=\"markdown-body\">\n");
        sb.Append(body);
        sb.Append("\n</article>\n");
        sb.Append("<script src=\"mermaid.min.js\"></script>\n");
        sb.Append("<script>\n").Append(BuildScript(colors, fullscreenGestures)).Append("\n</script>\n");
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>画像書き換えの結果。<see cref="Html"/> は書き換え後 HTML、
    /// <see cref="MappedRoot"/> は仮想ホストへマップすべきルートフォルダー(相対画像が無ければ null)。</summary>
    public readonly record struct ImageRebaseResult(string Html, string? MappedRoot);

    // <img ... src="値"> の src 属性を抜き出す(値はダブルクオート前提。Markdig 出力もこの形)。
    private static readonly Regex ImgSrcPattern =
        new(@"(<img\b[^>]*?\bsrc="")([^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// HTML 中の相対パス画像 <c>&lt;img src&gt;</c> を、<paramref name="baseDir"/>(md ファイルのフォルダー)
    /// 基準で絶対パスへ解決し、<paramref name="hostBaseUrl"/>(末尾 <c>/</c>)配下の URL へ書き換える。
    /// 上位参照(<c>../</c>)も解決できるよう、md フォルダーと全参照画像の共通祖先フォルダーをマップ対象とし
    /// <see cref="ImageRebaseResult.MappedRoot"/> に返す。絶対 URL・data:・ルート絶対・アンカー・絶対パス・
    /// 別ドライブの画像は書き換えない。相対画像が無ければ <see cref="ImageRebaseResult.MappedRoot"/> は null。
    /// </summary>
    public static ImageRebaseResult RebaseImages(string html, string baseDir, string hostBaseUrl)
    {
        var drive = Path.GetPathRoot(baseDir);
        string? root = null;
        foreach (Match m in ImgSrcPattern.Matches(html))
        {
            var abs = ResolveLocalImage(m.Groups[2].Value, baseDir, drive);
            if (abs is null) continue;
            var dir = Path.GetDirectoryName(abs)!;
            // md フォルダー自身も祖先に織り込み、公開ルートが必ず md ファイルを含むようにする。
            root = CommonDir(root ?? baseDir, dir);
        }
        if (root is null) return new ImageRebaseResult(html, null);

        var mapped = root;
        var rewritten = ImgSrcPattern.Replace(html, m =>
        {
            var abs = ResolveLocalImage(m.Groups[2].Value, baseDir, drive);
            if (abs is null) return m.Value;
            var rel = Path.GetRelativePath(mapped, abs).Replace('\\', '/');
            var url = hostBaseUrl + string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
            return $"{m.Groups[1].Value}{url}\"";
        });
        return new ImageRebaseResult(rewritten, mapped);
    }

    /// <summary>相対パス画像なら <paramref name="baseDir"/> 基準の絶対パスを返す。対象外なら null。</summary>
    private static string? ResolveLocalImage(string src, string baseDir, string? drive)
    {
        if (!IsRelativeResource(src)) return null;
        var unescaped = Uri.UnescapeDataString(src);
        if (Path.IsPathRooted(unescaped)) return null;   // C:\ や \\server 等の絶対指定は対象外
        var abs = Path.GetFullPath(Path.Combine(baseDir, unescaped));
        // 別ドライブへ抜ける画像は同一ルートにまとめられないため対象外。
        return string.Equals(Path.GetPathRoot(abs), drive, StringComparison.OrdinalIgnoreCase) ? abs : null;
    }

    /// <summary>同一ドライブの2フォルダーの共通祖先フォルダーを返す。</summary>
    private static string CommonDir(string a, string b)
    {
        var sa = a.TrimEnd('\\', '/').Split('\\', '/');
        var sb = b.TrimEnd('\\', '/').Split('\\', '/');
        var n = Math.Min(sa.Length, sb.Length);
        var i = 0;
        while (i < n && string.Equals(sa[i], sb[i], StringComparison.OrdinalIgnoreCase)) i++;
        var joined = string.Join('\\', sa.Take(i));
        return joined.EndsWith(':') ? joined + '\\' : joined;   // ドライブのみ("C:")はルート("C:\")に整える
    }

    /// <summary>書き換え対象の相対参照か(絶対 URL・data:・ルート絶対・アンカーは対象外)。</summary>
    private static bool IsRelativeResource(string url) =>
        !string.IsNullOrEmpty(url)
        && url[0] != '#'
        && url[0] != '/'
        && !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        && !Uri.IsWellFormedUriString(url, UriKind.Absolute);

    /// <summary>テーマ配色から CSS を生成する。</summary>
    private static string BuildCss(ThemeColors c) => $@"
:root {{ color-scheme: {(c.IsDark ? "dark" : "light")}; }}
body {{ margin: 0; padding: 24px 32px; background: {c.Background}; color: {c.Foreground};
       font-family: 'Segoe UI', 'Meiryo', sans-serif; font-size: 15px; line-height: 1.7; }}
.markdown-body {{ max-width: none; margin: 0; }}
h1, h2, h3, h4, h5, h6 {{ color: {c.Heading}; font-weight: 600; line-height: 1.3;
       margin: 1.4em 0 0.6em; }}
h1 {{ font-size: 1.9em; border-bottom: 1px solid {c.HeadingBorder}; padding-bottom: 0.3em; }}
h2 {{ font-size: 1.5em; border-bottom: 1px solid {c.HeadingBorder}; padding-bottom: 0.3em; }}
h3 {{ font-size: 1.25em; }}
a {{ color: {c.Link}; text-decoration: none; }}
a:hover {{ text-decoration: underline; }}
code {{ font-family: 'Consolas', 'MS Gothic', monospace; font-size: 0.9em;
       background: {c.CodeBackground}; padding: 0.15em 0.4em; border-radius: 4px; }}
pre {{ background: {c.PreBackground}; padding: 14px 16px; border-radius: 6px; overflow: auto;
      border: 1px solid {c.PreBorder}; }}
pre code {{ background: none; padding: 0; }}
.mermaid-fig {{ position: relative; margin: 1em 0; border: 1px solid {c.PreBorder}; border-radius: 6px;
       background: {c.Background}; }}
.mermaid-toolbar {{ position: absolute; top: 6px; right: 6px; display: flex; gap: 4px;
       z-index: 5; opacity: 0.85; }}
.mermaid-toolbar:hover {{ opacity: 1; }}
.mermaid-toolbar button {{ background: {c.ToolbarBackground}; color: {c.ToolbarText}; border: 1px solid {c.ToolbarBorder};
       border-radius: 4px; min-width: 30px; height: 28px; padding: 0 6px; font-size: 14px;
       line-height: 1; cursor: pointer; }}
.mermaid-toolbar button:hover {{ background: {c.CodeBackground}; }}
pre.mermaid {{ background: {c.Background}; border: none; text-align: left; margin: 0;
       padding: 12px; overflow: auto; max-height: 80vh; }}
pre.mermaid svg {{ display: block; }}
.mermaid-fig:fullscreen {{ background: {c.Background}; border: none; }}
.mermaid-fig:fullscreen pre.mermaid {{ max-height: 100vh; height: 100vh; }}
blockquote {{ margin: 0; padding: 0.2em 1em; color: {c.Blockquote}; border-left: 4px solid {c.BlockquoteBar}; }}
table {{ border-collapse: collapse; margin: 1em 0; }}
th, td {{ border: 1px solid {c.TableBorder}; padding: 6px 12px; }}
th {{ background: {c.TableHeaderBackground}; }}
tr:nth-child(even) td {{ background: {c.TableEvenRowBackground}; }}
img {{ max-width: 100%; }}
hr {{ border: none; border-top: 1px solid {c.TableBorder}; margin: 1.6em 0; }}
ul, ol {{ padding-left: 1.6em; }}
";

    // mermaid を明示描画し、図ごとにズーム/全画面ボタンを配線する。
    // Esc/Enter はホスト(WPF)へ通知して閉じる(全画面中は全画面解除を優先)。
    private static string BuildScript(ThemeColors c, IReadOnlyList<string> gestures) => $@"
mermaid.initialize({{ startOnLoad: false, theme: '{(c.IsDark ? "dark" : "default")}', securityLevel: 'loose' }});"
        + MermaidZoomScript + KeydownScript(KeyChordJs.MatchExpression(gestures, "e"));

    // 表示切替キー(設定値)で cycle-view、S でソース切替、Esc/Enter で閉じる をホストへ通知する。
    private static string KeydownScript(string toggleExpr) => $@"
document.addEventListener('keydown', function (e) {{
  if ({toggleExpr}) {{   // 表示形態の切替(全画面 ⇄ ペイン領域)をホストへ通知
    e.preventDefault();
    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('cycle-view');
    return;
  }}
  if (e.key === 's' || e.key === 'S') {{   // S: レンダリング ⇄ ソース表示をホストへ通知
    e.preventDefault();
    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('toggle-source');
    return;
  }}
  if (e.key !== 'Escape' && e.key !== 'Enter') return;
  if (document.fullscreenElement) return;   // 全画面中の Esc は解除を優先
  if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('close');
}});
";

    private const string MermaidZoomScript = @"
mermaid.run({ querySelector: '.mermaid' }).then(function () {
  document.querySelectorAll('.mermaid-fig').forEach(function (fig) {
    var svg = fig.querySelector('svg');
    if (!svg) return;
    var pre = fig.querySelector('pre.mermaid');
    var vb = svg.viewBox && svg.viewBox.baseVal;
    var rect = svg.getBoundingClientRect();
    var bw = (vb && vb.width) ? vb.width : rect.width;
    var bh = (vb && vb.height) ? vb.height : rect.height;
    var scale = 1;
    function apply() {
      svg.style.maxWidth = 'none';
      svg.style.width = (bw * scale) + 'px';
      svg.style.height = (bh * scale) + 'px';
    }
    // 枠幅に合わせて縮小、ただし拡大はしない(最大100%)。
    function fitScale() {
      var avail = pre.clientWidth - 24;   // pre の左右パディング 12px ずつ
      if (avail <= 0 || !bw) return 1;
      return Math.min(1, avail / bw);
    }
    scale = fitScale();
    apply();
    // 全画面の出入りで枠幅が変わるため再フィットする。
    document.addEventListener('fullscreenchange', function () { scale = fitScale(); apply(); });
    function on(act, fn) {
      var b = fig.querySelector('[data-act=""' + act + '""]');
      if (b) b.addEventListener('click', fn);
    }
    on('in', function () { scale = Math.min(scale * 1.25, 8); apply(); });
    on('out', function () { scale = Math.max(scale / 1.25, 0.1); apply(); });
    on('reset', function () { scale = 1; apply(); });   // 100%(実寸)
    on('full', function () {
      if (document.fullscreenElement === fig) document.exitFullscreen();
      else fig.requestFullscreen();
    });
  });
});
";
}

/// <summary>
/// <c>```mermaid</c> フェンスを <c>&lt;pre class="mermaid"&gt;</c> として出力する描画器。
/// それ以外のコードブロックは既定動作に委譲する。
/// </summary>
internal sealed class MermaidCodeBlockRenderer : CodeBlockRenderer
{
    protected override void Write(HtmlRenderer renderer, CodeBlock obj)
    {
        if (obj is FencedCodeBlock fenced
            && string.Equals(fenced.Info, "mermaid", StringComparison.OrdinalIgnoreCase))
        {
            // 図ごとに拡大/縮小/等倍/全画面ツールバーを付け、折り返し容器で包む。
            renderer.Write("<div class=\"mermaid-fig\"><div class=\"mermaid-toolbar\">")
                    .Write("<button type=\"button\" data-act=\"out\" title=\"縮小\">－</button>")
                    .Write("<button type=\"button\" data-act=\"reset\" title=\"等倍\">100%</button>")
                    .Write("<button type=\"button\" data-act=\"in\" title=\"拡大\">＋</button>")
                    .Write("<button type=\"button\" data-act=\"full\" title=\"全画面\">⛶</button>")
                    .Write("</div><pre class=\"mermaid\">");
            var lines = obj.Lines.Lines;
            if (lines is not null)
            {
                for (var i = 0; i < obj.Lines.Count; i++)
                {
                    var slice = lines[i].Slice;
                    renderer.WriteEscape(ref slice);
                    renderer.WriteLine();
                }
            }
            renderer.Write("</pre></div>");
            return;
        }

        base.Write(renderer, obj);
    }
}
