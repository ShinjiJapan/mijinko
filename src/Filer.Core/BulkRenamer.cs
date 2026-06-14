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

    /// <summary>連番テンプレート。<c>*</c>=元の名前(拡張子を除く)、<c>#</c>の連続=連番の桁。</summary>
    public string Template { get; init; } = "*_#";
    /// <summary>連番の開始値(Sequence)。</summary>
    public int Start { get; init; } = 1;
    /// <summary>連番の増分(Sequence)。</summary>
    public int Step { get; init; } = 1;
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
    /// 入力名の一覧に対し新しい名前と状態を計算する。
    /// </summary>
    /// <param name="names">リネーム対象の現在のファイル名(表示順)。</param>
    /// <param name="options">リネーム方式。</param>
    /// <param name="existingNames">同フォルダー内の対象外ファイル名(衝突判定用)。null 可。</param>
    public static IReadOnlyList<BulkRenameResult> Plan(
        IReadOnlyList<string> names,
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
                return names.Select(n => new BulkRenameResult(n, n, BulkRenameStatus.Invalid)).ToArray();
            }
        }

        var invalidChars = Path.GetInvalidFileNameChars();

        // まず新しい名前と一次状態(Invalid / Unchanged / 仮 Ok)を計算する。
        var staged = new List<(string Original, string New, BulkRenameStatus Status)>(names.Count);
        for (int i = 0; i < names.Count; i++)
        {
            var original = names[i];
            var newName = Compute(original, options, regex, comparison, i);

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

    private static string Compute(string name, BulkRenameOptions options, Regex? regex,
        StringComparison comparison, int index)
    {
        var (baseName, ext) = FileNameParts.Split(name);

        if (options.Mode == BulkRenameMode.Sequence)
        {
            var number = options.Start + index * options.Step;
            var produced = ExpandTemplate(options.Template, baseName, number);
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

    /// <summary><c>*</c>=元の名前、<c>#</c>の連続=ゼロ埋め連番でテンプレートを展開する。</summary>
    private static string ExpandTemplate(string template, string baseName, int number)
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
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }
}
