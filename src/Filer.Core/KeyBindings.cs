namespace Filer.Core;

/// <summary>
/// キーで起動できる操作1つの定義。Id は設定ファイルのキー、DefaultGestures は既定の割り当て。
/// HelpLabel はフッターのキー操作説明に出す短いラベル(null なら非表示)。
/// </summary>
public sealed record KeyBindingAction(
    string Id, string DisplayName, string Category, string[] DefaultGestures, string? HelpLabel);

/// <summary>全キー操作の定義(定義順 = フッター表示順・設定画面の表示順)。</summary>
public static class KeyBindingActions
{
    public static readonly IReadOnlyList<KeyBindingAction> All = new KeyBindingAction[]
    {
        // ---- フッター(修飾なし)に出す主要操作。定義順がそのまま表示順になる ----
        new("pane.switchOrTerminal", "ペイン切替(ターミナル表示中はターミナルへ)", "ペイン・タブ", new[] { "Tab" }, "切替"),
        new("entry.activate", "開く(フォルダー移動/プレビュー)", "基本操作", new[] { "Enter" }, "開く"),
        new("nav.parent", "親フォルダーへ", "基本操作", new[] { "Back" }, "親へ"),
        new("mark.toggle", "マークして下へ", "基本操作", new[] { "Space" }, "マーク"),
        new("file.copy", "コピー(相手ペインへ)", "ファイル操作", new[] { "C" }, "コピー"),
        new("file.move", "移動(相手ペインへ)", "ファイル操作", new[] { "M" }, "移動"),
        new("file.delete", "ごみ箱へ送る", "ファイル操作", new[] { "D", "Delete" }, "ごみ箱"),
        new("file.deletePermanent", "完全削除(ごみ箱を経由しない)", "ファイル操作", new[] { "Shift+D" }, "完全削除"),
        new("file.rename", "名前の変更", "ファイル操作", new[] { "R" }, "改名"),
        new("file.bulkRename", "一括リネーム(連番/置換/正規表現)", "ファイル操作", new[] { "Shift+R" }, "一括改名"),
        new("folder.create", "新規フォルダー作成", "ファイル操作", new[] { "K" }, "新規フォルダー"),
        new("archive.zip", "ZIP 圧縮", "ファイル操作", new[] { "X" }, "圧縮"),
        new("sort.select", "ソート(方法と順番の選択)", "表示", new[] { "S" }, "ソート"),
        new("search.incremental", "インクリメンタルサーチ(名前検索)", "基本操作", new[] { "E" }, "検索"),
        new("search.file", "ファイル検索(サブフォルダーも検索)", "基本操作", new[] { "F" }, "ファイル検索"),
        new("filter.show", "フィルター表示(絞り込み)", "表示", new[] { "Shift+F" }, "絞り込み"),
        new("filter.clear", "フィルター解除", "表示", new[] { "Escape" }, null),
        new("nav.sameAsOther", "相手ペインと同じ場所へ", "移動", new[] { "O" }, "相手と同じ"),
        new("path.copy", "フルパスをクリップボードへコピー", "ファイル操作", new[] { "Q" }, "パスコピー"),
        new("file.diff", "2ファイルの差分を表示", "ファイル操作", new[] { "Shift+C" }, "差分"),
        new("drive.select", "ドライブ選択", "移動", new[] { "L" }, "ドライブ"),
        new("favorite.add", "お気に入りに登録", "移動", new[] { "A" }, "お気に入り"),
        new("favorite.select", "お気に入りへ移動", "移動", new[] { "D1", "NumPad1" }, "お気に入り選択"),
        new("history.select", "フォルダー履歴から移動", "移動", new[] { "H" }, "履歴"),
        new("terminal.open", "ターミナルを開く/フォーカス", "ツール", new[] { "T" }, "ターミナル"),
        new("view.toggleFullscreen", "表示切替(通常⇄全画面)", "表示", new[] { "F1" }, "表示切替(通常⇄全画面)"),
        new("view.toggleGrid", "詳細⇔サムネイル表示の切替", "表示", new[] { "Ctrl+G" }, "サムネ表示"),
        new("view.gridSize", "サムネイルのサイズ切替(通常⇔拡大)", "表示", new[] { "Ctrl+Shift+G" }, "サムネ拡大"),
        new("view.reload", "最新の状態に更新", "表示", new[] { "F5" }, "更新"),
        new("settings.open", "設定を開く", "ツール", new[] { "Z" }, "設定"),

        // ---- フッター非表示(カーソル移動など) ----
        new("mark.toggleAll", "全ファイル選択⇔解除", "基本操作", new[] { "Home" }, null),
        new("cursor.up", "カーソルを上へ", "カーソル移動", new[] { "Up", "NumPad8", "D8" }, null),
        new("cursor.down", "カーソルを下へ", "カーソル移動", new[] { "Down", "NumPad2", "D2" }, null),
        new("cursor.pageUp", "1ページ上へ", "カーソル移動", new[] { "PageUp" }, null),
        new("cursor.pageDown", "1ページ下へ", "カーソル移動", new[] { "PageDown" }, null),
        new("cursor.bottom", "末尾へ移動", "カーソル移動", new[] { "End" }, null),
        new("pane.left", "左へ(左ペイン:親 / 右ペイン:左へ)", "ペイン・タブ", new[] { "Left", "NumPad4", "D4" }, null),
        new("pane.right", "右へ(左ペイン:右へ / 右ペイン:親)", "ペイン・タブ", new[] { "Right", "NumPad6", "D6" }, null),

        // ---- Shift 系 ----
        new("entry.openWith", "Windows の関連付けで開く", "基本操作", new[] { "Shift+Enter" }, "関連付けで開く"),
        new("entry.openInOther", "反対ペインでフォルダーを開く", "基本操作", new[] { "Ctrl+Enter" }, "反対側で開く"),
        new("terminal.pick", "ターミナル種類を選んで開く", "ツール", new[] { "Shift+T" }, "ターミナル種類"),

        // ---- Ctrl 系(タブ) ----
        new("tab.new", "新しいタブ", "ペイン・タブ", new[] { "Ctrl+T" }, "新タブ"),
        new("tab.close", "タブを閉じる", "ペイン・タブ", new[] { "Ctrl+W" }, "タブを閉じる"),
        new("tab.prev", "前のタブ", "ペイン・タブ", new[] { "Ctrl+Left" }, "前タブ"),
        new("tab.next", "次のタブ", "ペイン・タブ", new[] { "Ctrl+Right" }, "次タブ"),
    };

    public static KeyBindingAction? Find(string id)
    {
        foreach (var action in All)
            if (action.Id == id)
                return action;
        return null;
    }

    /// <summary>アクション Id の接頭辞(外部ツールの動的アクション)。</summary>
    public const string ToolPrefix = "tool:";

    /// <summary>外部ツールを動的なキーバインドアクションへ変換する(Id=<c>tool:&lt;tool.Id&gt;</c>)。</summary>
    public static KeyBindingAction ForTool(ExternalTool tool) =>
        new(ToolPrefix + tool.Id, tool.Label, "外部ツール", tool.Gestures.ToArray(), tool.Label);

    /// <summary>アクション Id が外部ツールのものなら、そのツール Id を返す(でなければ null)。</summary>
    public static string? ToolIdOf(string actionId) =>
        actionId.StartsWith(ToolPrefix, StringComparison.Ordinal) ? actionId[ToolPrefix.Length..] : null;
}

/// <summary>
/// アクション⇔ジェスチャの対応表。既定の割り当てに設定ファイルの上書きを適用して構築する。
/// 1つのジェスチャは常に1つのアクションだけが持つ(上書きで衝突したら相手から外れる)。
/// </summary>
public sealed class KeyBindingMap
{
    // 有効なアクション一覧(組み込み + 外部ツールの動的アクション。この順=定義順)
    private readonly IReadOnlyList<KeyBindingAction> _actions;
    // actionId → ジェスチャ標準形のリスト(_actions の定義順で保持)
    private readonly Dictionary<string, List<string>> _gestures = new();
    // ジェスチャ正規化文字列 → actionId
    private readonly Dictionary<string, string> _resolve = new();

    private KeyBindingMap(IReadOnlyList<KeyBindingAction> actions) => _actions = actions;

    /// <summary>有効なアクション一覧(組み込み + 外部ツール)。設定画面の表示に使う。</summary>
    public IReadOnlyList<KeyBindingAction> Actions => _actions;

    /// <summary>既定の割り当てに上書き(actionId → ジェスチャ配列)を適用して構築する。</summary>
    /// <param name="overrides">未知のアクション・解析できないジェスチャは無視。空配列は「割り当てなし」。</param>
    public static KeyBindingMap Build(IReadOnlyDictionary<string, string[]>? overrides) =>
        Build(overrides, Array.Empty<KeyBindingAction>());

    /// <summary>
    /// 組み込みアクション(+上書き)と外部ツールの動的アクションを合わせて構築する。
    /// 動的アクションのジェスチャは「明示指定」として扱い、組み込みの既定から奪う。
    /// </summary>
    public static KeyBindingMap Build(IReadOnlyDictionary<string, string[]>? overrides,
        IEnumerable<KeyBindingAction> dynamicActions)
    {
        var actions = new List<KeyBindingAction>(KeyBindingActions.All);
        actions.AddRange(dynamicActions);

        var map = new KeyBindingMap(actions);
        var explicitIds = new HashSet<string>();   // 明示指定(上書き or 動的ツール)された actionId

        foreach (var action in actions)
        {
            List<string>? custom = null;
            if (KeyBindingActions.ToolIdOf(action.Id) is not null)
            {
                // 外部ツールのジェスチャはツール定義そのものが明示指定。
                custom = ParseAll(action.DefaultGestures);
            }
            else if (overrides is not null && overrides.TryGetValue(action.Id, out var specified))
            {
                var parsed = ParseAll(specified);
                // 空配列は「割り当てなし」の意思。非空なのに全部不正なら上書き自体を無視する。
                if (specified.Length == 0 || parsed.Count > 0)
                    custom = parsed;
            }

            if (custom is not null)
                explicitIds.Add(action.Id);
            map._gestures[action.Id] = custom ?? ParseAll(action.DefaultGestures);
        }

        // 明示指定されたジェスチャは、明示指定でないアクションの既定から奪う。
        var taken = new HashSet<string>(
            explicitIds.SelectMany(id => map._gestures[id]).Select(Normalize));
        foreach (var action in actions)
        {
            if (explicitIds.Contains(action.Id)) continue;
            map._gestures[action.Id].RemoveAll(g => taken.Contains(Normalize(g)));
        }

        map.Reindex();
        return map;
    }

    /// <summary>解析できたジェスチャだけを標準形にして返す。</summary>
    private static List<string> ParseAll(IReadOnlyList<string> gestures)
    {
        var result = new List<string>(gestures.Count);
        foreach (var text in gestures)
            if (KeyChord.TryParse(text, out var chord))
                result.Add(chord.ToString());
        return result;
    }

    private static string Normalize(string gesture) =>
        KeyChord.TryParse(gesture, out var chord) ? chord.Normalized : gesture.ToUpperInvariant();

    /// <summary>
    /// ジェスチャ→アクションの索引を作り直す。重複は定義順で先のアクションが勝ち、
    /// 索引に載らなかったジェスチャ(他アクションとの衝突・同一アクション内の重複)はリストからも外す。
    /// </summary>
    private void Reindex()
    {
        _resolve.Clear();
        foreach (var action in _actions)
            _gestures[action.Id].RemoveAll(g => !_resolve.TryAdd(Normalize(g), action.Id));
    }

    /// <summary>アクションに割り当て中のジェスチャ(標準形)。</summary>
    public IReadOnlyList<string> GesturesFor(string actionId) =>
        _gestures.TryGetValue(actionId, out var list) ? list : Array.Empty<string>();

    /// <summary>ジェスチャ文字列からアクション Id を引く。未割り当て・解析不能は null。</summary>
    public string? TryResolve(string gesture) =>
        KeyChord.TryParse(gesture, out var chord) &&
        _resolve.TryGetValue(chord.Normalized, out var id) ? id : null;

    /// <summary>ジェスチャを現在持っているアクション Id(設定画面の衝突確認用)。</summary>
    public string? OwnerOf(string gesture) => TryResolve(gesture);

    /// <summary>アクションの割り当てを置き換える。同じジェスチャを持つ他のアクションからは外す。</summary>
    public void Replace(string actionId, IReadOnlyList<string> gestures)
    {
        if (!_gestures.ContainsKey(actionId)) return;

        var parsed = ParseAll(gestures);
        var taken = new HashSet<string>(parsed.Select(Normalize));
        foreach (var (id, list) in _gestures)
            if (id != actionId)
                list.RemoveAll(g => taken.Contains(Normalize(g)));

        _gestures[actionId] = parsed;
        Reindex();
    }

    /// <summary>アクションの割り当てを既定へ戻す(既定ジェスチャは他のアクションから奪い返す)。</summary>
    public void ResetToDefault(string actionId)
    {
        var action = _actions.FirstOrDefault(a => a.Id == actionId);
        if (action is not null)
            Replace(actionId, action.DefaultGestures);
    }

    /// <summary>
    /// 既定と異なる組み込みアクションだけの上書き辞書(設定ファイルの keyBindings 保存用)。
    /// 外部ツール(<c>tool:</c>)はツール定義側にジェスチャを持つため除外する。
    /// </summary>
    public IReadOnlyDictionary<string, string[]> ToOverrides()
    {
        var result = new Dictionary<string, string[]>();
        foreach (var action in _actions)
        {
            if (KeyBindingActions.ToolIdOf(action.Id) is not null) continue;
            var current = _gestures[action.Id];
            var defaults = ParseAll(action.DefaultGestures);
            if (!current.Select(Normalize).SequenceEqual(defaults.Select(Normalize)))
                result[action.Id] = current.ToArray();
        }
        return result;
    }
}

/// <summary>フッターのキー操作説明を現在の割り当てから生成する。</summary>
public static class KeyHelp
{
    /// <summary>修飾なしのジェスチャを持つ操作の説明(例: "Tab:切替  Enter:開く  …")。</summary>
    public static string BuildNormal(KeyBindingMap map) =>
        Build(map, chord => !chord.Ctrl && !chord.Shift && !chord.Alt);

    /// <summary>Ctrl 併用ジェスチャを持つ操作の説明。</summary>
    public static string BuildCtrl(KeyBindingMap map) =>
        Build(map, chord => chord.Ctrl);

    /// <summary>Shift 併用(Ctrl なし)ジェスチャを持つ操作の説明。</summary>
    public static string BuildShift(KeyBindingMap map) =>
        Build(map, chord => chord.Shift && !chord.Ctrl);

    private static string Build(KeyBindingMap map, Func<KeyChord, bool> match)
    {
        var parts = new List<string>();
        foreach (var action in map.Actions)
        {
            if (action.HelpLabel is null) continue;
            foreach (var gesture in map.GesturesFor(action.Id))
            {
                if (KeyChord.TryParse(gesture, out var chord) && match(chord))
                {
                    parts.Add($"{chord.DisplayText}:{action.HelpLabel}");
                    break;   // 1アクションにつき最初の該当ジェスチャのみ表示
                }
            }
        }
        return string.Join("  ", parts);
    }
}
