using System.Net;
using System.Text;

namespace Filer.Core;

/// <summary>
/// side-by-side 差分(<see cref="DiffRow"/> 列)を、テーマ配色付きの完全な HTML 文書へ変換する(UI 非依存)。
/// 行種別ごとに CSS クラス(equal/modified/deleted/inserted)で色分けし、Esc/Enter で閉じ・F1 で
/// 表示形態切替をホスト(WPF)へ通知する。差分専用の add/del/mod 背景色は <see cref="ThemeColors.IsDark"/> で切り替える。
/// </summary>
public static class DiffHtmlRenderer
{
    /// <summary>差分行列と左右ファイル名・テーマ配色から HTML 文書を生成する。</summary>
    public static string ToHtmlDocument(
        IReadOnlyList<DiffRow> rows, string leftName, string rightName, ThemeColors colors)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"ja\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<style>\n").Append(BuildCss(colors)).Append("\n</style>\n</head>\n<body>\n");

        sb.Append("<table class=\"diff\">\n<thead>\n<tr>")
          .Append("<th class=\"num\"></th><th class=\"side\">").Append(Esc(leftName)).Append("</th>")
          .Append("<th class=\"num\"></th><th class=\"side\">").Append(Esc(rightName)).Append("</th>")
          .Append("</tr>\n</thead>\n<tbody>\n");

        foreach (var row in rows)
        {
            var cls = row.Kind switch
            {
                DiffRowKind.Equal => "equal",
                DiffRowKind.Modified => "modified",
                DiffRowKind.Deleted => "deleted",
                DiffRowKind.Inserted => "inserted",
                _ => "equal",
            };
            string leftCell, rightCell;
            if (row.Kind == DiffRowKind.Modified && row.LeftText is not null && row.RightText is not null)
            {
                // 変更行は文字単位で比較し、実際に変わった文字だけを <span class="chg"> で強調する。
                var (leftSegs, rightSegs) = InlineDiff.Compute(row.LeftText, row.RightText);
                leftCell = SegmentCell(leftSegs);
                rightCell = SegmentCell(rightSegs);
            }
            else
            {
                leftCell = Cell(row.LeftText);
                rightCell = Cell(row.RightText);
            }

            sb.Append("<tr class=\"").Append(cls).Append("\">")
              .Append("<td class=\"n l\">").Append(Num(row.LeftNo)).Append("</td>")
              .Append("<td class=\"s l\">").Append(leftCell).Append("</td>")
              .Append("<td class=\"n r\">").Append(Num(row.RightNo)).Append("</td>")
              .Append("<td class=\"s r\">").Append(rightCell).Append("</td>")
              .Append("</tr>\n");
        }

        sb.Append("</tbody>\n</table>\n");
        sb.Append("<script>\n").Append(ScriptBody).Append("\n</script>\n");
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>
    /// バイナリファイルで行差分を表示できないときの案内文書。
    /// <paramref name="identical"/> が true なら内容一致、false なら相違を伝える。
    /// </summary>
    public static string BinaryNoticeDocument(
        string leftName, string rightName, bool identical, ThemeColors colors)
    {
        var verdict = identical ? "内容は同一です。" : "内容は異なります。";
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"ja\">\n<head>\n<meta charset=\"utf-8\">\n<style>\n");
        sb.Append($@"html, body {{ margin: 0; height: 100%; background: {colors.Background}; color: {colors.Foreground};
       font-family: 'Segoe UI', 'Meiryo', sans-serif; }}
.notice {{ padding: 40px; line-height: 1.8; }}
.files {{ color: {colors.Heading}; font-family: 'Consolas', 'MS Gothic', monospace; }}
");
        sb.Append("</style>\n</head>\n<body>\n<div class=\"notice\">\n");
        sb.Append("<p>バイナリファイルのため行差分は表示できません。</p>\n");
        sb.Append("<p class=\"files\">").Append(Esc(leftName)).Append("  ⇔  ").Append(Esc(rightName)).Append("</p>\n");
        sb.Append("<p>").Append(verdict).Append("</p>\n");
        sb.Append("</div>\n<script>\n").Append(ScriptBody).Append("\n</script>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static string Num(int? n) => n is null ? "" : n.Value.ToString();

    /// <summary>本文セル。空行は高さ確保のため &amp;nbsp; を置く。null(存在しない側)は空。</summary>
    private static string Cell(string? text)
    {
        if (text is null) return "";
        return text.Length == 0 ? "&nbsp;" : Esc(text);
    }

    /// <summary>変更行のセル。変更区間を &lt;span class="chg"&gt; で包み、共通部分はそのまま出す。</summary>
    private static string SegmentCell(IReadOnlyList<InlineSegment> segments)
    {
        if (segments.Count == 0) return "&nbsp;";
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            if (seg.Changed)
                sb.Append("<span class=\"chg\">").Append(Esc(seg.Text)).Append("</span>");
            else
                sb.Append(Esc(seg.Text));
        }
        return sb.ToString();
    }

    private static string Esc(string s) => WebUtility.HtmlEncode(s);

    private static string BuildCss(ThemeColors c)
    {
        // 差分専用の背景色(行全体)と番号桁の背景。明暗で見やすい彩度に分ける。
        var (del, ins, mod, gutter) = c.IsDark
            ? ("#4B1818", "#15401C", "#4A3A12", "#2A2A2A")
            : ("#FFE0E0", "#DDFBE0", "#FFF4CC", "#F0F0F0");
        // 変更行内の「変わった文字」だけを強調する色(左=削除寄りの赤 / 右=追加寄りの緑)。
        var (delStrong, insStrong) = c.IsDark
            ? ("#8B2B2B", "#2E7D38")
            : ("#FFB3B3", "#9BE6A6");
        return $@"
html, body {{ margin: 0; padding: 0; height: 100%; background: {c.Background}; color: {c.Foreground}; }}
body {{ font-family: 'Consolas', 'MS Gothic', monospace; font-size: 13px; }}
table.diff {{ border-collapse: collapse; width: 100%; table-layout: fixed; }}
table.diff thead th {{ position: sticky; top: 0; z-index: 2; background: {c.TableHeaderBackground};
       color: {c.Heading}; text-align: left; padding: 6px 10px; border-bottom: 1px solid {c.PreBorder};
       font-family: 'Segoe UI', 'Meiryo', sans-serif; font-size: 13px; }}
table.diff td {{ padding: 0 8px; vertical-align: top; white-space: pre-wrap; word-break: break-all;
       border-bottom: 1px solid transparent; }}
td.n {{ width: 52px; text-align: right; color: {c.Blockquote}; background: {gutter};
       -webkit-user-select: none; user-select: none; }}
td.s {{ width: calc(50% - 52px); }}
th.num {{ width: 52px; }}
th.side {{ width: calc(50% - 52px); }}
tr.deleted td.s.l {{ background: {del}; }}
tr.inserted td.s.r {{ background: {ins}; }}
tr.modified td.s {{ background: {mod}; }}
tr.modified td.s.l span.chg {{ background: {delStrong}; border-radius: 2px; }}
tr.modified td.s.r span.chg {{ background: {insStrong}; border-radius: 2px; }}
";
    }

    // Esc/Enter=閉じる, F1=表示形態切替 をホスト(WPF)へ通知する。MarkdownRenderer と同じプロトコル。
    private const string ScriptBody = @"
document.addEventListener('keydown', function (e) {
  if (e.key === 'F1') {
    e.preventDefault();
    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('cycle-view');
    return;
  }
  if (e.key !== 'Escape' && e.key !== 'Enter') return;
  if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage('close');
});";
}
