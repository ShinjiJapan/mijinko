using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Filer.Core;

/// <summary>
/// お気に入りツリーの 1 ノード。
/// 項目: Path=フォルダーパス・Children=null(ラベルが空ならパスを表示に使う)。
/// グループ: Label=グループ名・Children=子ノード一覧・Path=空。
/// </summary>
public sealed record FavoriteNode(string Path, string Label, IReadOnlyList<FavoriteNode>? Children = null)
{
    public bool IsGroup => Children is not null;
}

/// <summary>
/// お気に入り(項目+グループの階層ツリー)を JSON ファイルへ永続化する。
/// 項目のパスはツリー全体で大文字小文字を無視して重複を排除し、登録順を保持する。
/// グループは「仕事/CLI」のようなスラッシュ区切りのグループパスで指定する(空=ルート直下)。
/// 同名グループは同一階層で 1 つに統合され、存在しないグループは自動作成される。
/// 旧形式(パス文字列の配列・Children なしのフラットなオブジェクト配列)は読み込み時に
/// ルート直下の項目として自動移行し、保存時に新形式へ書き換える。
/// </summary>
public sealed class FavoritesStore
{
    /// <summary>グループパスの区切り文字。グループ名自体には使用できない。</summary>
    public const char GroupSeparator = '/';

    private readonly string _filePath;

    public FavoritesStore(string filePath) => _filePath = filePath;

    /// <summary>お気に入りツリー(ルート直下のノード一覧)。</summary>
    public IReadOnlyList<FavoriteNode> GetTree() => Load().Select(ToPublic).ToList();

    /// <summary>全グループのグループパス一覧(深さ優先・登録順)。</summary>
    public IReadOnlyList<string> GetGroupPaths()
    {
        var result = new List<string>();
        CollectGroupPaths(Load(), "", result);
        return result;
    }

    /// <summary>
    /// お気に入り項目を追加する(同一パスがツリー内のどこかに既にあれば何もしない)。
    /// group は「仕事/CLI」形式のグループパス(空=ルート直下)。無ければ自動作成。
    /// 追加したら true、重複で追加しなかったら false。
    /// </summary>
    public bool Add(string path, string label = "", string group = "")
    {
        var roots = Load();
        if (FindItem(roots, path) is not null)
            return false;
        var target = EnsureGroup(roots, group);
        target.Add(Node.Item(path, label ?? ""));
        Save(roots);
        return true;
    }

    /// <summary>
    /// 既存項目のパス・ラベル・所属グループを更新する
    /// (対象が無い、または変更後のパスが別の項目と重複する場合は何もしない)。
    /// 同じグループなら位置を保持し、グループが変わる場合は移動先の末尾へ追加する。
    /// </summary>
    public void Update(string oldPath, string newPath, string label, string group)
    {
        var roots = Load();
        var found = FindItem(roots, oldPath);
        if (found is null)
            return;
        if (!SamePath(oldPath, newPath) && FindItem(roots, newPath) is not null)
            return;
        var (list, index, currentGroup) = found.Value;
        if (SameGroupPath(currentGroup, group))
        {
            list[index] = Node.Item(newPath, label ?? "");
        }
        else
        {
            list.RemoveAt(index);
            EnsureGroup(roots, group).Add(Node.Item(newPath, label ?? ""));
        }
        Save(roots);
    }

    /// <summary>項目をツリー全体から削除する(大文字小文字無視)。空になったグループは残す。</summary>
    public void Remove(string path)
    {
        var roots = Load();
        if (FindItem(roots, path) is not { } found)
            return;
        found.List.RemoveAt(found.Index);
        Save(roots);
    }

    /// <summary>
    /// グループ名を変更する(子ノードは保持)。
    /// 対象が無い・名前が不正(空または「/」を含む)・同一階層に同名グループがある場合は false。
    /// </summary>
    public bool RenameGroup(string groupPath, string newName)
    {
        newName = (newName ?? "").Trim();
        if (newName.Length == 0 || newName.Contains(GroupSeparator))
            return false;
        var roots = Load();
        if (FindGroup(roots, groupPath) is not { } found)
            return false;
        if (found.List.Any(n => n != found.Group && n.IsGroup && SameName(n.Label, newName)))
            return false;
        found.Group.Label = newName;
        Save(roots);
        return true;
    }

    /// <summary>グループを子孫ごと削除する(対象が無ければ何もしない)。</summary>
    public void RemoveGroup(string groupPath)
    {
        var roots = Load();
        if (FindGroup(roots, groupPath) is not { } found)
            return;
        found.List.Remove(found.Group);
        Save(roots);
    }

    /// <summary>
    /// 項目を所属する階層の中で delta だけ上下に移動する(負=上へ・正=下へ)。
    /// 先頭/末尾を越える分はクランプし、位置が変わったときだけ true を返す。
    /// 番号(1〜9)は階層内の並び順で決まるため、これがショートカット番号の変更になる。
    /// </summary>
    public bool MoveItem(string path, int delta)
    {
        var roots = Load();
        if (FindItem(roots, path) is not { } found)
            return false;
        if (!MoveWithin(found.List, found.Index, delta))
            return false;
        Save(roots);
        return true;
    }

    /// <summary>グループを所属する階層の中で delta だけ上下に移動する。位置が変わったら true。</summary>
    public bool MoveGroup(string groupPath, int delta)
    {
        var roots = Load();
        if (FindGroup(roots, groupPath) is not { } found)
            return false;
        if (!MoveWithin(found.List, found.List.IndexOf(found.Group), delta))
            return false;
        Save(roots);
        return true;
    }

    /// <summary>list 内の index のノードを delta だけずらす(範囲外はクランプ)。動いたら true。</summary>
    private static bool MoveWithin(List<Node> list, int index, int delta)
    {
        var target = Math.Clamp(index + delta, 0, list.Count - 1);
        if (target == index)
            return false;
        var node = list[index];
        list.RemoveAt(index);
        list.Insert(target, node);
        return true;
    }

    // ---- 内部表現(可変ツリー) ----

    private sealed class Node
    {
        public string Path = "";
        public string Label = "";
        public List<Node>? Children;

        public bool IsGroup => Children is not null;

        public static Node Item(string path, string label) => new() { Path = path, Label = label };
        public static Node Group(string name) => new() { Label = name, Children = new List<Node>() };
    }

    private static FavoriteNode ToPublic(Node n) =>
        new(n.Path, n.Label, n.Children?.Select(ToPublic).ToList());

    private static bool SamePath(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool SameName(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string[] SplitGroupPath(string group) =>
        (group ?? "").Split(GroupSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool SameGroupPath(string a, string b) =>
        SplitGroupPath(a).SequenceEqual(SplitGroupPath(b), StringComparer.OrdinalIgnoreCase);

    /// <summary>グループパスのグループを取得(無ければ途中階層も含めて作成)し、その子一覧を返す。</summary>
    private static List<Node> EnsureGroup(List<Node> roots, string group)
    {
        var current = roots;
        foreach (var name in SplitGroupPath(group))
        {
            var next = current.FirstOrDefault(n => n.IsGroup && SameName(n.Label, name));
            if (next is null)
            {
                next = Node.Group(name);
                current.Add(next);
            }
            current = next.Children!;
        }
        return current;
    }

    /// <summary>項目をツリー全体から検索する。見つかったら所属リスト・位置・グループパスを返す。</summary>
    private static (List<Node> List, int Index, string Group)? FindItem(List<Node> list, string path, string group = "")
    {
        for (var i = 0; i < list.Count; i++)
        {
            var n = list[i];
            if (n.IsGroup)
            {
                var child = FindItem(n.Children!, path, JoinGroup(group, n.Label));
                if (child is not null)
                    return child;
            }
            else if (SamePath(n.Path, path))
            {
                return (list, i, group);
            }
        }
        return null;
    }

    /// <summary>グループパスのグループを検索する。見つかったら所属リストとノードを返す。</summary>
    private static (List<Node> List, Node Group)? FindGroup(List<Node> roots, string groupPath)
    {
        var names = SplitGroupPath(groupPath);
        if (names.Length == 0)
            return null;
        var list = roots;
        Node? group = null;
        foreach (var name in names)
        {
            if (group is not null)
                list = group.Children!;
            group = list.FirstOrDefault(n => n.IsGroup && SameName(n.Label, name));
            if (group is null)
                return null;
        }
        return (list, group!);
    }

    /// <summary>親グループパスとグループ名を「仕事/CLI」形式のグループパスへ連結する。</summary>
    public static string JoinGroup(string parent, string name) =>
        parent.Length == 0 ? name : parent + GroupSeparator + name;

    private static void CollectGroupPaths(List<Node> list, string parent, List<string> result)
    {
        foreach (var n in list.Where(n => n.IsGroup))
        {
            var path = JoinGroup(parent, n.Label);
            result.Add(path);
            CollectGroupPaths(n.Children!, path, result);
        }
    }

    // ---- 永続化 ----

    private sealed class NodeDto
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Path { get; set; }
        public string? Label { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<NodeDto>? Children { get; set; }
    }

    private List<Node> Load()
    {
        if (!File.Exists(_filePath))
            return new List<Node>();
        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<Node>();

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return new List<Node>();
        return ReadNodes(doc.RootElement);
    }

    private static List<Node> ReadNodes(JsonElement array)
    {
        var result = new List<Node>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                // 旧形式: パス文字列のみ → ラベル空の項目として移行
                var path = element.GetString();
                if (!string.IsNullOrEmpty(path))
                    result.Add(Node.Item(path, ""));
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                var label = element.TryGetProperty("Label", out var l) ? l.GetString() ?? "" : "";
                if (element.TryGetProperty("Children", out var c) && c.ValueKind == JsonValueKind.Array)
                {
                    var group = Node.Group(label);
                    group.Children!.AddRange(ReadNodes(c));
                    result.Add(group);
                }
                else if (element.TryGetProperty("Path", out var p) && p.GetString() is { Length: > 0 } path)
                {
                    result.Add(Node.Item(path, label));
                }
            }
        }
        return result;
    }

    private void Save(List<Node> roots)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(roots.Select(ToDto), new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
    }

    private static NodeDto ToDto(Node n) => n.IsGroup
        ? new NodeDto { Label = n.Label, Children = n.Children!.Select(ToDto).ToList() }
        : new NodeDto { Path = n.Path, Label = n.Label };
}
