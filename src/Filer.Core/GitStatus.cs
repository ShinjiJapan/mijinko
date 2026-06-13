namespace Filer.Core;

/// <summary>一覧項目の Git 状態(色分け用)。値の大小がディレクトリ集約時の優先度。</summary>
public enum GitEntryState
{
    None = 0,
    Ignored = 1,
    Untracked = 2,
    Added = 3,
    Modified = 4,
    Conflicted = 5,
}

/// <summary>
/// 1リポジトリ分の Git ステータス。ブランチ情報と、リポジトリ相対パス(/区切り)ごとの状態を持つ。
/// ディレクトリは配下の変更を優先度最大で集約した状態を返す。
/// </summary>
public sealed class GitStatusSnapshot
{
    private readonly Dictionary<string, GitEntryState> _files;
    private readonly Dictionary<string, GitEntryState> _directories;

    /// <summary>現在のブランチ名(ヘッダーなしは null。デタッチ時は "(detached)")。</summary>
    public string? Branch { get; }

    /// <summary>上流より進んでいるコミット数。</summary>
    public int Ahead { get; }

    /// <summary>上流より遅れているコミット数。</summary>
    public int Behind { get; }

    internal GitStatusSnapshot(
        string? branch, int ahead, int behind,
        Dictionary<string, GitEntryState> files,
        Dictionary<string, GitEntryState> directories)
    {
        Branch = branch;
        Ahead = ahead;
        Behind = behind;
        _files = files;
        _directories = directories;
    }

    /// <summary>パンくず表示用のブランチ文字列(例 "main ↑1 ↓2")。ブランチ不明は空。</summary>
    public string BranchDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(Branch)) return string.Empty;
            var text = Branch;
            if (Ahead > 0) text += $" ↑{Ahead}";
            if (Behind > 0) text += $" ↓{Behind}";
            return text;
        }
    }

    /// <summary>リポジトリ相対パスの項目の状態を返す(\ 区切り・大小文字の差は吸収)。</summary>
    public GitEntryState StateOf(string relativePath, bool isDirectory)
    {
        var key = relativePath.Replace('\\', '/').Trim('/');
        var map = isDirectory ? _directories : _files;
        return map.TryGetValue(key, out var state) ? state : GitEntryState.None;
    }
}

/// <summary>`git status --porcelain=v2 --branch -z` の出力(NUL区切り)を解析する。</summary>
public static class GitStatusParser
{
    public static GitStatusSnapshot Parse(string output)
    {
        string? branch = null;
        var ahead = 0;
        var behind = 0;
        var files = new Dictionary<string, GitEntryState>(StringComparer.OrdinalIgnoreCase);
        var directories = new Dictionary<string, GitEntryState>(StringComparer.OrdinalIgnoreCase);

        var tokens = output.Split('\0');
        for (var i = 0; i < tokens.Length; i++)
        {
            var record = tokens[i];
            if (record.Length == 0) continue;

            if (record[0] == '#')
            {
                ParseBranchHeader(record, ref branch, ref ahead, ref behind);
                continue;
            }

            switch (record[0])
            {
                case '1':
                    Register(LastField(record, 9), ChangedState(record), files, directories);
                    break;
                case '2':
                    // パスの次の NUL 区切りトークンはリネーム元(表示対象外)なので読み飛ばす
                    Register(LastField(record, 10), ChangedState(record), files, directories);
                    i++;
                    break;
                case 'u':
                    Register(LastField(record, 11), GitEntryState.Conflicted, files, directories);
                    break;
                case '?':
                    Register(record[2..], GitEntryState.Untracked, files, directories);
                    break;
                case '!':
                    Register(record[2..], GitEntryState.Ignored, files, directories);
                    break;
            }
        }

        return new GitStatusSnapshot(branch, ahead, behind, files, directories);
    }

    private static void ParseBranchHeader(string record, ref string? branch, ref int ahead, ref int behind)
    {
        const string head = "# branch.head ";
        const string ab = "# branch.ab ";
        if (record.StartsWith(head, StringComparison.Ordinal))
        {
            branch = record[head.Length..];
        }
        else if (record.StartsWith(ab, StringComparison.Ordinal))
        {
            // 形式: "+1 -2"
            var parts = record[ab.Length..].Split(' ');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var a) &&
                int.TryParse(parts[1], out var b))
            {
                ahead = a;
                behind = -b;
            }
        }
    }

    /// <summary>固定フィールド数 fieldCount のレコードから最終フィールド(パス。空白を含み得る)を取り出す。</summary>
    private static string? LastField(string record, int fieldCount)
    {
        var parts = record.Split(' ', fieldCount);
        return parts.Length == fieldCount ? parts[fieldCount - 1] : null;
    }

    /// <summary>変更レコード(種別 1/2)の XY コードから状態を決める。新規ステージのみ Added、それ以外は Modified。</summary>
    private static GitEntryState ChangedState(string record)
    {
        // レコード形式: "<種別> <XY> ..." — XY は位置 2,3
        if (record.Length < 4) return GitEntryState.Modified;
        var x = record[2];
        var y = record[3];
        return x == 'A' && y == '.' ? GitEntryState.Added : GitEntryState.Modified;
    }

    private static void Register(
        string? path, GitEntryState state,
        Dictionary<string, GitEntryState> files,
        Dictionary<string, GitEntryState> directories)
    {
        if (string.IsNullOrEmpty(path)) return;

        // 未追跡ディレクトリは「dir/」と末尾スラッシュで報告される
        var isDirectory = path.EndsWith('/');
        var key = path.TrimEnd('/');
        if (key.Length == 0) return;

        if (isDirectory)
            Merge(directories, key, state);
        else
            files[key] = state;

        // 祖先ディレクトリへ集約(優先度の高い状態で上書き)
        var index = key.LastIndexOf('/');
        while (index > 0)
        {
            key = key[..index];
            Merge(directories, key, state);
            index = key.LastIndexOf('/');
        }
    }

    private static void Merge(Dictionary<string, GitEntryState> map, string key, GitEntryState state)
    {
        if (!map.TryGetValue(key, out var current) || state > current)
            map[key] = state;
    }
}

/// <summary>Git リポジトリのルート(.git のあるフォルダー)を上方向に探す。</summary>
public static class GitRepositoryLocator
{
    /// <summary>
    /// startDirectory から親へ辿り、.git(ディレクトリまたはワークツリーのファイル)を持つ
    /// 最初のフォルダーを返す。見つからなければ null。存在判定は注入(テスト用)。
    /// </summary>
    public static string? FindRoot(string startDirectory, Func<string, bool> gitMarkerExists)
    {
        var dir = startDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (gitMarkerExists(Path.Combine(dir, ".git"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
