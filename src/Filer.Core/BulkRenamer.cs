using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Filer.Core;

/// <summary>一括リネームの方式。</summary>
public enum BulkRenameMode
{
    /// <summary>文字列置換(Find を Replace に置き換える)。</summary>
    Replace,
    /// <summary>正規表現置換(Find=パターン、Replace=置換($1 等の参照可))。</summary>
    Regex,
    /// <summary>連番(テンプレートに沿って通し番号を振る)。</summary>
    Sequence,
}

/// <summary>連番(Sequence)で通し番号を割り当てる順序。結果一覧の並びは入力順のまま、番号だけこの順で振る。</summary>
public enum SequenceOrder
{
    /// <summary>表示順(入力された順)のまま。</summary>
    Current,
    /// <summary>名前 昇順。</summary>
    NameAsc,
    /// <summary>名前 降順。</summary>
    NameDesc,
    /// <summary>更新日時 古い順。</summary>
    DateAsc,
    /// <summary>更新日時 新しい順。</summary>
    DateDesc,
    /// <summary>サイズ 小さい順。</summary>
    SizeAsc,
    /// <summary>サイズ 大きい順。</summary>
    SizeDesc,
}

/// <summary>一括リネーム1件の入力(現在名・サイズ・更新日時)。連番の並び替えと日付トークンに使う。</summary>
public sealed record BulkRenameItem(string Name, long Size = 0, DateTime LastModified = default);

/// <summary>一括リネーム1件の状態。</summary>
public enum BulkRenameStatus
{
    /// <summary>変更あり・問題なし(実行対象)。</summary>
    Ok,
    /// <summary>新旧同名(変更なし)。</summary>
    Unchanged,
    /// <summary>不正(空名・無効文字・正規表現エラー)。</summary>
    Invalid,
    /// <summary>結果が他項目または既存ファイルと重複。</summary>
    Duplicate,
}

/// <summary>一括リネームのオプション。UI から組み立てて <see cref="BulkRenamer.Plan"/> へ渡す。</summary>
public sealed record BulkRenameOptions
{
    public BulkRenameMode Mode { get; init; } = BulkRenameMode.Replace;

    /// <summary>検索文字列 / 正規表現パターン(Replace・Regex)。</summary>
    public string Find { get; init; } = "";
    /// <summary>置換文字列(Replace・Regex)。</summary>
    public string Replace { get; init; } = "";
    /// <summary>大文字小文字を区別する(Replace・Regex)。既定は区別しない。</summary>
    public bool CaseSensitive { get; init; }
    /// <summary>置換対象に拡張子を含める(Replace・Regex)。既定は名前部分のみ。</summary>
    public bool IncludeExtension { get; init; }

    /// <summary>
    /// 連番テンプレート。<c>*</c>=元の名前(拡張子を除く)、<c>#</c>の連続=連番の桁、
    /// <c>$(書式)</c>=更新日時を .NET 標準の日付書式で展開(例: <c>$(yyyyMMdd)</c>)。
    /// </summary>
    public string Template { get; init; } = "#####_$(yyyyMMddHHmmss)";
    /// <summary>連番の開始値(Sequence)。</summary>
    public int Start { get; init; } = 1;
    /// <summary>連番の増分(Sequence)。</summary>
    public int Step { get; init; } = 1;
    /// <summary>連番を振る順序(Sequence)。既定は表示順のまま。</summary>
    public SequenceOrder Order { get; init; } = SequenceOrder.Current;
}

/// <summary>一括リネーム1件の結果(元の名前・新しい名前・状態)。</summary>
public sealed record BulkRenameResult(string OriginalName, string NewName, BulkRenameStatus Status);

/// <summary>
/// マークした複数ファイルの一括リネーム(連番/置換/正規表現)を計算する純粋ロジック(I/O なし)。
/// 結果一覧をプレビューに見せ、状態が <see cref="BulkRenameStatus.Ok"/> の項目だけを実行する。
/// </summary>
public static class BulkRenamer
{
    /// <summary>
    /// 入力名の一覧に対し新しい名前と状態を計算する(名前のみ。連番の日付・並び替えは既定)。
    /// </summary>
    /// <param name="names">リネーム対象の現在のファイル名(表示順)。</param>
    /// <param name="options">リネーム方式。</param>
    /// <param name="existingNames">同フォルダー内の対象外ファイル名(衝突判定用)。null 可。</param>
    public static IReadOnlyList<BulkRenameResult> Plan(
        IReadOnlyList<string> names,
        BulkRenameOptions options,
        IReadOnlyCollection<string>? existingNames = null)
        => Plan(names.Select(n => new BulkRenameItem(n)).ToArray(), options, existingNames);

    /// <summary>
    /// 入力一覧に対し新しい名前と状態を計算する。
    /// 結果は入力順で返り、連番の番号だけ <see cref="BulkRenameOptions.Order"/> の順で割り当てる。
    /// </summary>
    /// <param name="items">リネーム対象(現在名・サイズ・更新日時、表示順)。</param>
    /// <param name="options">リネーム方式。</param>
    /// <param name="existingNames">同フォルダー内の対象外ファイル名(衝突判定用)。null 可。</param>
    public static IReadOnlyList<BulkRenameResult> Plan(
        IReadOnlyList<BulkRenameItem> items,
        BulkRenameOptions options,
        IReadOnlyCollection<string>? existingNames = null)
    {
        var comparison = options.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        Regex? regex = null;
        if (options.Mode == BulkRenameMode.Regex)
        {
            try
            {
                var ro = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                regex = new Regex(options.Find, ro);
            }
            catch (ArgumentException)
            {
                // パターン不正 → 全件 Invalid
                return items.Select(it => new BulkRenameResult(it.Name, it.Name, BulkRenameStatus.Invalid)).ToArray();
            }
        }

        // 連番の番号割り当て順位(入力 index → 0 始まりの順位)。
        var ranks = BuildSequenceRanks(items, options.Order);

        var invalidChars = Path.GetInvalidFileNameChars();

        // まず新しい名前と一次状態(Invalid / Unchanged / 仮 Ok)を計算する。
        var staged = new List<(string Original, string New, BulkRenameStatus Status)>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var original = items[i].Name;
            var newName = Compute(items[i], options, regex, comparison, ranks[i]);

            BulkRenameStatus status;
            if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(invalidChars) >= 0)
                status = BulkRenameStatus.Invalid;
            else if (string.Equals(newName, original, StringComparison.Ordinal))
                status = BulkRenameStatus.Unchanged;
            else
                status = BulkRenameStatus.Ok;

            staged.Add((original, newName, status));
        }

        // 重複・既存衝突を判定する(Invalid / Unchanged は対象外)。
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in staged)
            if (s.Status == BulkRenameStatus.Ok)
                counts[s.New] = counts.TryGetValue(s.New, out var c) ? c + 1 : 1;

        // 占有済みの名前 = 同フォルダーの対象外ファイル + 変更しない対象が今も使う名前。
        // (変更なし対象はリネームされず元の名前を保持するため、そこへの衝突も検出する)
        var occupied = existingNames is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        foreach (var s in staged)
            if (s.Status == BulkRenameStatus.Unchanged)
                occupied.Add(s.New);

        var result = new List<BulkRenameResult>(staged.Count);
        foreach (var s in staged)
        {
            var status = s.Status;
            if (status == BulkRenameStatus.Ok &&
                ((counts.TryGetValue(s.New, out var c) && c > 1) || occupied.Contains(s.New)))
                status = BulkRenameStatus.Duplicate;

            result.Add(new BulkRenameResult(s.Original, s.New, status));
        }
        return result;
    }

    /// <summary>連番の番号を割り当てる順位を求める。ranks[i] = 入力 index i の 0 始まり順位。</summary>
    private static int[] BuildSequenceRanks(IReadOnlyList<BulkRenameItem> items, SequenceOrder order)
    {
        var ranks = new int[items.Count];
        if (order == SequenceOrder.Current)
        {
            for (int i = 0; i < ranks.Length; i++) ranks[i] = i;
            return ranks;
        }

        var idx = Enumerable.Range(0, items.Count);
        // OrderBy/OrderByDescending は安定ソート。同値は入力順を保つ。
        IEnumerable<int> sorted = order switch
        {
            SequenceOrder.NameAsc => idx.OrderBy(i => items[i].Name, StringComparer.OrdinalIgnoreCase),
            SequenceOrder.NameDesc => idx.OrderByDescending(i => items[i].Name, StringComparer.OrdinalIgnoreCase),
            SequenceOrder.DateAsc => idx.OrderBy(i => items[i].LastModified),
            SequenceOrder.DateDesc => idx.OrderByDescending(i => items[i].LastModified),
            SequenceOrder.SizeAsc => idx.OrderBy(i => items[i].Size),
            SequenceOrder.SizeDesc => idx.OrderByDescending(i => items[i].Size),
            _ => idx,
        };

        int rank = 0;
        foreach (var i in sorted) ranks[i] = rank++;
        return ranks;
    }

    private static string Compute(BulkRenameItem item, BulkRenameOptions options, Regex? regex,
        StringComparison comparison, int rank)
    {
        var name = item.Name;
        var (baseName, ext) = FileNameParts.Split(name);

        if (options.Mode == BulkRenameMode.Sequence)
        {
            var number = options.Start + rank * options.Step;
            var produced = ExpandTemplate(options.Template, baseName, number, item.LastModified);
            return Reassemble(produced, ext);
        }

        var target = options.IncludeExtension ? name : baseName;
        var transformed = options.Mode == BulkRenameMode.Regex
            ? regex!.Replace(target, options.Replace)
            : (options.Find.Length == 0 ? target : target.Replace(options.Find, options.Replace, comparison));

        return options.IncludeExtension ? transformed : Reassemble(transformed, ext);
    }

    /// <summary>名前部分に拡張子を付け直す(拡張子は <see cref="FileNameParts"/> 仕様でドットなし)。</summary>
    private static string Reassemble(string baseName, string ext) =>
        ext.Length == 0 ? baseName : baseName + "." + ext;

    /// <summary>
    /// <c>*</c>=元の名前、<c>#</c>の連続=ゼロ埋め連番、<c>$(書式)</c>=更新日時の日付書式で
    /// テンプレートを展開する。<c>$(</c> に対応する <c>)</c> が無ければそのままリテラル扱い。
    /// </summary>
    private static string ExpandTemplate(string template, string baseName, int number, DateTime date)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < template.Length;)
        {
            var c = template[i];
            if (c == '*')
            {
                sb.Append(baseName);
                i++;
            }
            else if (c == '#')
            {
                int j = i;
                while (j < template.Length && template[j] == '#') j++;
                sb.Append(number.ToString(CultureInfo.InvariantCulture).PadLeft(j - i, '0'));
                i = j;
            }
            else if (c == '$' && i + 1 < template.Length && template[i + 1] == '(')
            {
                int close = template.IndexOf(')', i + 2);
                if (close < 0)
                {
                    // 閉じ括弧なし → リテラルの '$' として扱い、残りは通常処理。
                    sb.Append(c);
                    i++;
                }
                else
                {
                    var format = template.Substring(i + 2, close - (i + 2));
                    sb.Append(FormatDate(date, format));
                    i = close + 1;
                }
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>更新日時を .NET 標準の日付書式で展開する。書式が不正なら <c>$(書式)</c> をそのまま残す。</summary>
    private static string FormatDate(DateTime date, string format)
    {
        if (format.Length == 0) return "";
        try
        {
            return date.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return "$(" + format + ")";
        }
    }
}
