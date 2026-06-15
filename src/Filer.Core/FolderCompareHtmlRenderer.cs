using System.Net;
using System.Text;

namespace Filer.Core;

/// <summary>
/// フォルダー比較のツリー(<see cref="FolderCompareNode"/> 列)を、テーマ配色付きの完全な HTML 文書へ変換する(UI 非依存)。
/// 左右2カラムで項目を並べ、状態ごとに色分けする(変更=黄/左のみ=赤/右のみ=緑/同一=無色)。
/// 変更ファイルの行クリックで 2ファイル差分を開くようホスト(WPF)へ通知し、Esc/Enter で閉じ・表示切替キーで表示形態を切り替える。
/// </summary>
public static class FolderCompareHtmlRenderer
{
    /// <summary>既定の表示切替ジェスチャ(設定で上書きしないときの値)。</summary>
    private static readonly string[] DefaultToggleGestures = { "F1" };
    private static readonly string[] DefaultTransferGestures = { "T" };

    /// <summary>2ファイル差分を開く通知の区切り(Windows パスに現れないタブを使う)。</summary>
    public const string DiffSeparator = "\t";

    public static string ToHtmlDocument(
        IReadOnlyList<FolderCompareNode> nodes, string leftName, string rightName, ThemeColors colors) =>
        ToHtmlDocument(nodes, leftName, rightName, colors, DefaultToggleGestures, DefaultTransferGestures);

    /// <summary>
    /// 比較ツリーと左右ルート名・テーマ配色から HTML 文書を生成する。
    /// <paramref name="fullscreenGestures"/> は表示切替を発火させるキー(設定値)。
    /// <paramref name="transferGestures"/> は「転送して閉じる」を発火させるキー(設定値)。
    /// </summary>
    public static string ToHtmlDocument(
        IReadOnlyList<FolderCompareNode> nodes, string leftName, string rightName, ThemeColors colors,
        IReadOnlyList<string> fullscreenGestures, IReadOnlyList<string> transferGestures)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"ja\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<style>\n").Append(BuildCss(colors)).Append("\n</style>\n</head>\n<body>\n");

        sb.Append("<table class=\"cmp\">\n<thead>\n<tr>")
          .Append("<th class=\"side\">").Append(Esc(leftName)).Append("</th>")
          .Append("<th class=\"side\">").Append(Esc(rightName)).Append("</th>")
          .Append("</tr>\n</thead>\n<tbody>\n");

        var anyRow = false;
        foreach (var node in nodes)
            anyRow |= AppendNode(sb, node, depth: 0);

        if (!anyRow)
            sb.Append("<tr class=\"empty\"><td colspan=\"2\">差異はありません。</td></tr>\n");

        sb.Append("</tbody>\n</table>\n");
        sb.Append("<script>\n").Append(BuildScript(fullscreenGestures, transferGestures)).Append("\n</script>\n");
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>1ノード(と配下)を行として出力する。1行でも出したら true。同一の除外は呼び出し側(木の絞り込み)で行う。</summary>
    private static bool AppendNode(StringBuilder sb, FolderCompareNode node, int depth)
    {
        var emitted = false;

        var cls = node.Kind switch
        {
            FolderCompareKind.Same => "same",
            FolderCompareKind.Modified => "modified",
            FolderCompareKind.LeftOnly => "leftonly",
            FolderCompareKind.RightOnly => "rightonly",
            _ => "same",
        };

        // クリックで差分を開けるのは「左右に存在する変更ファイル」のみ。
        var clickable = node is { IsDirectory: false, Kind: FolderCompareKind.Modified, LeftPath: not null, RightPath: not null };
        var attrs = clickable
            ? $" class=\"{cls} clickable\" data-l=\"{Esc(node.LeftPath!)}\" data-r=\"{Esc(node.RightPath!)}\""
            : $" class=\"{cls}\"";

        sb.Append("<tr").Append(attrs).Append('>')
          .Append("<td class=\"s l\">").Append(SideCell(node, depth, isLeft: true)).Append("</td>")
          .Append("<td class=\"s r\">").Append(SideCell(node, depth, isLeft: false)).Append("</td>")
          .Append("</tr>\n");
        emitted = true;

        foreach (var child in node.Children)
            emitted |= AppendNode(sb, child, depth + 1);

        return emitted;
    }

    /// <summary>片側セル(インデント+アイコン+名前+サイズ)。その側に存在しなければ空。</summary>
    private static string SideCell(FolderCompareNode node, int depth, bool isLeft)
    {
        var exists = isLeft ? node.LeftPath is not null : node.RightPath is not null;
        if (!exists) return "&nbsp;";

        var indent = new string(' ', depth * 4);   // 空白でインデント(td は white-space:pre-wrap で保持)
        var icon = node.IsDirectory ? "📁 " : "📄 ";
        var size = node.IsDirectory ? "" : FormatSize(isLeft ? node.LeftSize : node.RightSize);
        var name = Esc(node.Name) + (node.IsDirectory ? "\\" : "");
        return $"{indent}{icon}{name}{size}";
    }

    private static string FormatSize(long? size) =>
        size is null ? "" : $"<span class=\"sz\">{size.Value:#,0} B</span>";

    private static string Esc(string s) => WebUtility.HtmlEncode(s);

    private static string BuildCss(ThemeColors c)
    {
        var (del, ins, mod) = c.IsDark
            ? ("#4B1818", "#15401C", "#4A3A12")
            : ("#FFE0E0", "#DDFBE0", "#FFF4CC");
        var hover = c.IsDark ? "#3A3A3A" : "#E8E8E8";
        return $@"
html, body {{ margin: 0; padding: 0; height: 100%; background: {c.Background}; color: {c.Foreground}; }}
body {{ font-family: 'Consolas', 'MS Gothic', monospace; font-size: 13px; }}
table.cmp {{ border-collapse: collapse; width: 100%; table-layout: fixed; }}
table.cmp thead th {{ position: sticky; top: 0; z-index: 2; background: {c.TableHeaderBackground};
       color: {c.Heading}; text-align: left; padding: 6px 10px; border-bottom: 1px solid {c.PreBorder};
       font-family: 'Segoe UI', 'Meiryo', sans-serif; font-size: 13px; }}
table.cmp td {{ padding: 1px 8px; vertical-align: top; white-space: pre-wrap; word-break: break-all;
       border-bottom: 1px solid transparent; }}
td.s {{ width: 50%; }}
th.side {{ width: 50%; }}
span.sz {{ color: {c.Blockquote}; font-size: 12px; margin-left: 8px; }}
tr.leftonly td.s.l {{ background: {del}; }}
tr.rightonly td.s.r {{ background: {ins}; }}
tr.modified td.s {{ background: {mod}; }}
tr.clickable {{ cursor: pointer; }}
tr.clickable:hover td.s {{ outline: 1px solid {c.Heading}; outline-offset: -1px; }}
tr:hover td {{ background-color: {hover}; }}
tr.leftonly:hover td.s.l, tr.rightonly:hover td.s.r, tr.modified:hover td.s {{ background-color: inherit; }}
tr.empty td {{ padding: 24px; color: {c.Blockquote}; }}
";
    }

    // 行クリック=差分通知, 転送キー=転送, Esc/Enter=閉じる, 表示切替キー=表示形態切替 をホスト(WPF)へ通知する。
    private static string BuildScript(IReadOnlyList<string> gestures, IReadOnlyList<string> transferGestures) => $@"
function post(msg) {{ if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(msg); }}
document.addEventListener('click', function (e) {{
  var tr = e.target.closest('tr.clickable');
  if (!tr) return;
  post('diff' + '{DiffSeparator}' + tr.getAttribute('data-l') + '{DiffSeparator}' + tr.getAttribute('data-r'));
}});
document.addEventListener('keydown', function (e) {{
  if ({KeyChordJs.MatchExpression(gestures, "e")}) {{
    e.preventDefault();
    post('cycle-view');
    return;
  }}
  if ({KeyChordJs.MatchExpression(transferGestures, "e")}) {{
    e.preventDefault();
    post('transfer');
    return;
  }}
  if (e.key !== 'Escape' && e.key !== 'Enter') return;
  post('close');
}});";
}
