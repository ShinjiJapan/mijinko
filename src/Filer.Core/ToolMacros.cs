using System.Text;

namespace Filer.Core;

/// <summary>
/// 外部ツールの引数テンプレートを展開するための文脈(だいなファイラー風マクロの値)。
/// パスは末尾の <c>\</c> を含まない。マーク系リストは「マーク優先・無ければカーソル項目」を
/// 呼び出し側で解決して渡す(他方ペインの <see cref="OtherMarkedFullPaths"/> はマークのみ)。
/// </summary>
public sealed record ToolMacroContext(
    string CursorName,                          // $F(カーソル項目のファイル名。".." 等は空)
    string ActivePaneDir,                       // $P(自ファイル窓)
    string ActiveCursorDir,                     // $C(カーソルが実フォルダーならそのパス、それ以外は自窓パス)
    string OtherPaneDir,                        // $O(他ファイル窓)
    string LeftPaneDir,                         // $L(左ファイル窓)
    string RightPaneDir,                        // $R(右ファイル窓)
    IReadOnlyList<string> ActiveMarkedNames,    // $MS(マーク or カーソルのファイル名)
    IReadOnlyList<string> ActiveMarkedFullPaths,// $MF(マーク or カーソルのフルパス)
    IReadOnlyList<string> OtherMarkedFullPaths);// $MO/$mO(他方のマークのみ)

/// <summary>
/// 外部ツールの引数テンプレートをマクロ展開する(UI 非依存)。
///
/// 単一値マクロ(そのまま挿入。引用は利用者がテンプレートに書く):
///   $F=ファイル名 / $W=拡張子を除いた名前 / $E=拡張子 /
///   $P=自窓パス / $C=カーソルのフォルダー(無ければ自窓パス) /
///   $O=他窓パス / $L=左窓パス / $R=右窓パス
/// 複数値マクロ(各項目を "" で囲み空白区切り):
///   $MS=マーク名一覧 / $MF=マークのフルパス一覧 /
///   $MO=他方マークのフルパス一覧(無ければコマンド自体をキャンセル=null) /
///   $mO=同上だが無ければ空文字
///   ($MS/$MF はマークが無ければカーソル項目1つを呼び出し側が渡す)
///   $$ はリテラルの $。未知の $X はそのまま残す。
/// </summary>
public static class ToolMacroExpander
{
    /// <summary>テンプレートを展開する。$MO が空でキャンセルされた場合は null。</summary>
    public static string? Expand(string template, ToolMacroContext ctx)
    {
        if (string.IsNullOrEmpty(template)) return template ?? "";

        var sb = new StringBuilder(template.Length + 32);
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c != '$') { sb.Append(c); i++; continue; }

            // 2文字マクロ($MS/$MF/$MO/$mO)を先に判定する。
            if (i + 2 < template.Length && (template[i + 1] is 'M' or 'm') && template[i + 2] is 'S' or 'F' or 'O')
            {
                var spec = template.Substring(i + 1, 2);   // "MS" / "MF" / "MO" / "mO"
                switch (spec)
                {
                    case "MS": sb.Append(JoinQuoted(ctx.ActiveMarkedNames)); break;
                    case "MF": sb.Append(JoinQuoted(ctx.ActiveMarkedFullPaths)); break;
                    case "MO":
                        if (ctx.OtherMarkedFullPaths.Count == 0) return null;   // 大文字: キャンセル
                        sb.Append(JoinQuoted(ctx.OtherMarkedFullPaths));
                        break;
                    case "mO":
                        sb.Append(JoinQuoted(ctx.OtherMarkedFullPaths));        // 小文字: 無ければ空
                        break;
                }
                i += 3;
                continue;
            }

            if (i + 1 < template.Length)
            {
                var n = template[i + 1];
                var single = n switch
                {
                    '$' => "$",
                    'F' => ctx.CursorName,
                    'W' => FileNameParts.Split(ctx.CursorName).Base,
                    'E' => FileNameParts.Split(ctx.CursorName).Extension,
                    'P' => ctx.ActivePaneDir,
                    'C' => ctx.ActiveCursorDir,
                    'O' => ctx.OtherPaneDir,
                    'L' => ctx.LeftPaneDir,
                    'R' => ctx.RightPaneDir,
                    _ => null,
                };
                if (single is not null) { sb.Append(single); i += 2; continue; }
            }

            // 未知のマクロ・末尾の $ はそのまま残す。
            sb.Append('$');
            i++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// テンプレートを展開し、最初のパス1つを取り出す(ストアアプリのファイルアクティベーション用)。
    /// キャンセル・空なら null。
    /// </summary>
    public static string? ExpandToSinglePath(string template, ToolMacroContext ctx)
    {
        var expanded = Expand(template, ctx);
        if (string.IsNullOrWhiteSpace(expanded)) return null;
        return FirstToken(expanded);
    }

    /// <summary>
    /// $C(<see cref="ToolMacroContext.ActiveCursorDir"/>)の値を決める。
    /// カーソルが実フォルダー(".." を除く)ならそのフルパス、そうでなければ自窓パス。いずれも末尾 \ を除く。
    /// </summary>
    public static string ResolveCursorDir(bool cursorIsDirectory, bool cursorIsParent, string cursorFullPath, string paneDir)
        => (cursorIsDirectory && !cursorIsParent ? cursorFullPath : paneDir).TrimEnd('\\');

    /// <summary>各項目を "" で囲み空白で連結する。</summary>
    private static string JoinQuoted(IReadOnlyList<string> items) =>
        string.Join(" ", items.Select(s => "\"" + s + "\""));

    /// <summary>引用符を考慮して最初のトークン(パス)を取り出す。</summary>
    private static string? FirstToken(string s)
    {
        s = s.TrimStart();
        if (s.Length == 0) return null;
        if (s[0] == '"')
        {
            var end = s.IndexOf('"', 1);
            return end < 0 ? s[1..] : s.Substring(1, end - 1);
        }
        var space = s.IndexOf(' ');
        return space < 0 ? s : s[..space];
    }
}
