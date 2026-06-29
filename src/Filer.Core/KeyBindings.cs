namespace Filer.Core;

/// <summary>
/// キー割り当ての有効範囲。<see cref="KeyBindingContext.Global"/> はファイラー本体(一覧)で効くキー、
/// <see cref="KeyBindingContext.Preview"/> はプレビューウィンドウ専用キー。コンテキストが違えば
/// 同じキーを別々の操作へ割り当てられる(例: 1=お気に入り選択(Global) と 1=1枚⇔2枚表示(Preview))。
/// </summary>
public enum KeyBindingContext
{
    Global,
    Preview,
}

/// <summary>
/// キーで起動できる操作1つの定義。Id は設定ファイルのキー、DefaultGestures は既定の割り当て。
/// HelpLabel はフッターのキー操作説明に出す短いラベル(null なら非表示)。
/// Context は有効範囲(既定は本体=Global。プレビュー専用キーは Preview)。
/// </summary>
public sealed record KeyBindingAction(
    string Id, string DisplayName, string Category, string[] DefaultGestures, string? HelpLabel,
    KeyBindingContext Context = KeyBindingContext.Global);

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
        new("folder.compare", "フォルダー(ツリー)比較", "ファイル操作", new[] { "Ctrl+Shift+C" }, "フォルダー比較"),
        new("drive.select", "ドライブ選択", "移動", new[] { "L" }, "ドライブ"),
        new("favorite.add", "お気に入りに登録", "移動", new[] { "A" }, "お気に入り"),
        new("favorite.select", "お気に入りへ移動", "移動", new[] { "D1", "NumPad1" }, "お気に入り選択"),
        new("history.select", "フォルダー履歴から移動", "移動", new[] { "H" }, "履歴"),
        new("entry.edit", "テキスト編集(アクティブペインで開く)", "基本操作", new[] { "I" }, "編集"),
        new("memo.toggle", "メモ表示の切替(反対ペイン)", "ツール", new[] { "U" }, "メモ"),
        new("terminal.open", "ターミナルを開く/フォーカス", "ツール", new[] { "T" }, "ターミナル"),
        // ターミナルにフォーカスがある間だけ働く専用キー(それ以外のキーは全て端末へ送る)。
        // 既定は空きキー(F5 は更新に割当済み)。フッターには出さない。
        new("terminal.focusBack", "ターミナル中: 一覧へフォーカスを戻す", "ツール", new[] { "F6" }, null),
        new("terminal.collapse", "ターミナル中: 表示をたたむ(セッションは保持)", "ツール", new[] { "F4" }, null),
        // テキスト編集中だけ働く専用キー(逆ペインにプレビュー)。フッターには出さない(エディター中のみ表示)。
        new("editor.preview", "テキスト編集中: 逆ペインにプレビュー(Markdown など)", "ツール", new[] { "F2" }, null),
        new("view.toggleFullscreen", "表示切替(通常⇄全画面)", "表示", new[] { "F1" }, "表示切替(通常⇄全画面)"),
        new("view.toggleGrid", "詳細⇔サムネイル表示の切替", "表示", new[] { "Ctrl+G" }, "サムネ表示"),
        new("view.gridSize", "サムネイルのサイズ切替(小⇔大)", "表示", new[] { "Ctrl+Shift+G" }, "サムネ拡大"),
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

        new("palette.show", "コマンドパレット(全コマンドを検索して実行)", "ツール", new[] { "Ctrl+Shift+P" }, "コマンド"),

        // ---- Ctrl 系(タブ) ----
        new("tab.new", "新しいタブ", "ペイン・タブ", new[] { "Ctrl+T" }, "新タブ"),
        new("tab.close", "タブを閉じる", "ペイン・タブ", new[] { "Ctrl+W" }, "タブを閉じる"),
        new("tab.prev", "前のタブ", "ペイン・タブ", new[] { "Ctrl+Left" }, "前タブ"),
        new("tab.next", "次のタブ", "ペイン・タブ", new[] { "Ctrl+Right" }, "次タブ"),

        // ---- プレビュー専用(プレビューウィンドウでのみ有効。本体キーと重複してよい) ----
        // フッターには出さない(HelpLabel=null)。画像ヘッダーのヒントは別途このアクションから引く。
        new("preview.image.next", "プレビュー: 次の画像へ(漫画送り)", "プレビュー",
            new[] { "Down", "Space", "D4", "NumPad4" }, null, KeyBindingContext.Preview),
        new("preview.image.prev", "プレビュー: 前の画像へ(漫画戻し)", "プレビュー",
            new[] { "Up", "D6", "NumPad6" }, null, KeyBindingContext.Preview),
        new("preview.image.toggleTwoUp", "プレビュー: 1枚⇔2枚表示の切替", "プレビュー",
            new[] { "D1", "NumPad1", "End" }, null, KeyBindingContext.Preview),
        new("preview.source.toggle", "プレビュー: 表示切替(レンダリング/ハイライト/テキスト)", "プレビュー",
            new[] { "S" }, null, KeyBindingContext.Preview),
        new("preview.close", "プレビュー: 閉じる", "プレビュー",
            new[] { "Escape", "Enter" }, null, KeyBindingContext.Preview),
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
    // コンテキストごとの「ジェスチャ正規化文字列 → actionId」。コンテキストが違えば同じキーを別操作へ割り当て可。
    private readonly Dictionary<KeyBindingContext, Dictionary<string, string>> _resolve = new();
    // actionId → コンテキスト(衝突判定・解決をコンテキスト内に閉じるため使う)
    private readonly Dictionary<string, KeyBindingContext> _contextOf = new();

    private KeyBindingMap(IReadOnlyList<KeyBindingAction> actions)
    {
        _actions = actions;
        foreach (var action in actions)
            _contextOf[action.Id] = action.Context;
    }

    /// <summary>アクションのコンテキスト(未知の Id は Global)。</summary>
    private KeyBindingContext ContextOf(string actionId) =>
        _contextOf.TryGetValue(actionId, out var ctx) ? ctx : KeyBindingContext.Global;

    /// <summary>コンテキストの解決辞書(無ければ作る)。</summary>
    private Dictionary<string, string> ResolveDict(KeyBindingContext context)
    {
        if (!_resolve.TryGetValue(context, out var dict))
            _resolve[context] = dict = new Dictionary<string, string>();
        return dict;
    }

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

        // 明示指定されたジェスチャは、同じコンテキストの明示指定でないアクションの既定から奪う
        // (コンテキストが違えば奪わない=本体とプレビューで同じキーを共存させる)。
        var takenByContext = new Dictionary<KeyBindingContext, HashSet<string>>();
        foreach (var id in explicitIds)
        {
            var ctx = map.ContextOf(id);
            if (!takenByContext.TryGetValue(ctx, out var set))
                takenByContext[ctx] = set = new HashSet<string>();
            foreach (var g in map._gestures[id])
                set.Add(Normalize(g));
        }
        foreach (var action in actions)
        {
            if (explicitIds.Contains(action.Id)) continue;
            if (takenByContext.TryGetValue(action.Context, out var taken))
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
    /// ジェスチャ→アクションの索引(コンテキスト別)を作り直す。同一コンテキスト内では重複を許さず、
    /// 定義順で先のアクションが勝つ。索引に載らなかったジェスチャはリストからも外す。
    /// コンテキストが違えば同じキーが両方に載る。
    /// </summary>
    private void Reindex()
    {
        _resolve.Clear();
        foreach (var action in _actions)
        {
            var dict = ResolveDict(action.Context);
            _gestures[action.Id].RemoveAll(g => !dict.TryAdd(Normalize(g), action.Id));
        }
    }

    /// <summary>アクションに割り当て中のジェスチャ(標準形)。</summary>
    public IReadOnlyList<string> GesturesFor(string actionId) =>
        _gestures.TryGetValue(actionId, out var list) ? list : Array.Empty<string>();

    /// <summary>ジェスチャ文字列から本体(Global)コンテキストのアクション Id を引く。</summary>
    public string? TryResolve(string gesture) => TryResolve(gesture, KeyBindingContext.Global);

    /// <summary>ジェスチャ文字列から指定コンテキストのアクション Id を引く。未割り当て・解析不能は null。</summary>
    public string? TryResolve(string gesture, KeyBindingContext context) =>
        KeyChord.TryParse(gesture, out var chord) &&
        _resolve.TryGetValue(context, out var dict) &&
        dict.TryGetValue(chord.Normalized, out var id) ? id : null;

    /// <summary>ジェスチャを本体(Global)コンテキストで持っているアクション Id。</summary>
    public string? OwnerOf(string gesture) => TryResolve(gesture);

    /// <summary>ジェスチャを指定コンテキストで持っているアクション Id(設定画面の衝突確認用)。</summary>
    public string? OwnerOf(string gesture, KeyBindingContext context) => TryResolve(gesture, context);

    /// <summary>
    /// アクションの割り当てを置き換える。同じコンテキストで同じジェスチャを持つ他アクションからは外す
    /// (コンテキストが違うアクションのキーは奪わない)。
    /// </summary>
    public void Replace(string actionId, IReadOnlyList<string> gestures)
    {
        if (!_gestures.ContainsKey(actionId)) return;

        var context = ContextOf(actionId);
        var parsed = ParseAll(gestures);
        var taken = new HashSet<string>(parsed.Select(Normalize));
        foreach (var (id, list) in _gestures)
            if (id != actionId && ContextOf(id) == context)
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

    /// <summary>
    /// 状態別フッター(メモ編集中・ターミナルフォーカス中など、キー操作が変わる画面)の1項目。
    /// <see cref="FixedGesture"/> を指定するとそのキーをそのまま表示し、null なら
    /// <see cref="ActionId"/> のジェスチャを設定マップから引く(設定変更に追従)。
    /// </summary>
    public readonly record struct ContextHelpEntry(string? ActionId, string? FixedGesture, string Label);

    /// <summary>状態別フッター文字列を生成する。各項目を "キー:ラベル" 形式で連結。
    /// ジェスチャを解決できない項目(割り当てなし等)は省く。</summary>
    public static string BuildContext(KeyBindingMap map, IReadOnlyList<ContextHelpEntry> entries)
    {
        var parts = new List<string>();
        foreach (var entry in entries)
        {
            var gesture = entry.FixedGesture is { } fixedGesture
                ? (KeyChord.TryParse(fixedGesture, out var fixedChord) ? fixedChord.DisplayText : fixedGesture)
                : ResolveFirstGesture(map, entry.ActionId);
            if (!string.IsNullOrEmpty(gesture))
                parts.Add($"{gesture}:{entry.Label}");
        }
        return string.Join("  ", parts);
    }

    /// <summary>アクションに割り当てられた最初のジェスチャの表示文字列(なければ null)。</summary>
    private static string? ResolveFirstGesture(KeyBindingMap map, string? actionId)
    {
        if (actionId is null) return null;
        foreach (var gesture in map.GesturesFor(actionId))
            if (KeyChord.TryParse(gesture, out var chord))
                return chord.DisplayText;
        return null;
    }

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
