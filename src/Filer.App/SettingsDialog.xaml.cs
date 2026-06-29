using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// 設定ダイアログ(Z)。キー割り当て(組み込み操作+外部ツール)の変更と、
/// 外部ツール一覧の追加・編集・削除を行う。OK で <see cref="Result"/> に新しい設定が入る。
/// </summary>
public partial class SettingsDialog : Window
{
    /// <summary>キー割り当て一覧の1行(1操作)。</summary>
    private sealed class KeyBindingRow
    {
        public required string ActionId { get; init; }
        public required string Category { get; init; }
        public required string DisplayName { get; init; }
        public required string GesturesText { get; init; }
        public required string CustomMark { get; init; }
    }

    /// <summary>外部ツール一覧の1行。</summary>
    private sealed class ToolRow
    {
        public required string Id { get; init; }
        public required string Label { get; init; }
        public required string KindText { get; init; }
        public required string Target { get; init; }
        public required string KeyText { get; init; }
    }

    // 編集中の状態: _tools がツール定義(キー含む)の真実、_map は組み込み上書き+ツールから導出。
    private readonly List<ExternalTool> _tools;
    private KeyBindingMap _map;

    private List<KeyBindingRow> _rows = new();

    /// <summary>キーキャプチャ中の対象行(null なら非キャプチャ)。</summary>
    private KeyBindingRow? _captureRow;
    /// <summary>true: 既存キーへ追加 / false: 置き換え。</summary>
    private bool _captureAppend;

    /// <summary>OK 時の新しい設定(キャンセルなら null)。</summary>
    public AppSettings? Result { get; private set; }

    public SettingsDialog(AppSettings current)
    {
        InitializeComponent();
        _tools = current.Tools.Select(Clone).ToList();
        _map = BuildMap(current.KeyBindingOverrides);

        // テーマはラジオ選択で即プレビューする。キャンセル時に戻せるよう元のテーマを覚えておく。
        _originalTheme = current.Theme;
        _suppressThemeApply = true;
        SetThemeRadios(current.Theme);
        _suppressThemeApply = false;

        LightweightUiaCheck.IsChecked = current.LightweightListAutomation;

        ConfirmMoveCheck.IsChecked = current.ConfirmMove;
        ConfirmRecycleCheck.IsChecked = current.ConfirmRecycle;
        ConfirmPermanentDeleteCheck.IsChecked = current.ConfirmPermanentDelete;

        EnableFastSearchCheck.IsChecked = current.EnableElevatedFastSearch;

        SetMarkupModeRadios(current.MarkupPreviewMode);

        BindingList.SelectionChanged += (_, _) => UpdateButtonStates();
        ToolList.SelectionChanged += (_, _) => UpdateToolButtonStates();
        // 衝突確認ダイアログや Alt+Tab でフォーカスが離れたらキャプチャを安全に中止する。
        Deactivated += (_, _) => EndCapture();

        RefreshRows();
        RefreshToolRows();
        UpdateButtonStates();
        UpdateToolButtonStates();

        // 起動時は最初のタブのキー割り当て一覧へフォーカスし、先頭行を選んで
        // すぐに矢印・Enter・Delete で操作できるようにする。
        Loaded += (_, _) => FocusBindingList();
    }

    /// <summary>キー割り当て一覧へフォーカスし、未選択なら先頭行を選ぶ。</summary>
    private void FocusBindingList()
    {
        if (BindingList.SelectedItem is null && BindingList.Items.Count > 0)
            BindingList.SelectedIndex = 0;

        BindingList.Focus();
        if (BindingList.SelectedItem is { } selected &&
            BindingList.ItemContainerGenerator.ContainerFromItem(selected) is ListViewItem item)
        {
            item.Focus();
        }
    }

    private static ExternalTool Clone(ExternalTool t) =>
        t with { Gestures = t.Gestures.ToList() };

    /// <summary>組み込み上書き + 現在のツール定義から対応表を構築する。</summary>
    private KeyBindingMap BuildMap(IReadOnlyDictionary<string, string[]> overrides) =>
        KeyBindingMap.Build(overrides, _tools.Select(KeyBindingActions.ForTool));

    /// <summary>ツール定義の変更後、組み込み上書きを保ったまま対応表を作り直し、解決結果をツールへ反映する。</summary>
    private void RebuildMap()
    {
        _map = BuildMap(_map.ToOverrides());
        SyncToolGesturesFromMap();
    }

    /// <summary>対応表で解決されたツールのジェスチャを _tools へ書き戻す(衝突解決の反映)。</summary>
    private void SyncToolGesturesFromMap()
    {
        for (var i = 0; i < _tools.Count; i++)
        {
            var gestures = _map.GesturesFor(KeyBindingActions.ToolPrefix + _tools[i].Id).ToList();
            _tools[i] = _tools[i] with { Gestures = gestures };
        }
    }

    // ---- キー割り当て一覧 ----

    /// <summary>現在の対応表から一覧を作り直す(選択行は維持)。組み込み操作+外部ツールを表示。</summary>
    private void RefreshRows()
    {
        var selectedId = SelectedRow?.ActionId;
        _rows = _map.Actions.Select(action =>
        {
            var gestures = _map.GesturesFor(action.Id);
            var isDefault = gestures.Select(Normalize)
                .SequenceEqual(action.DefaultGestures.Select(Normalize));
            return new KeyBindingRow
            {
                ActionId = action.Id,
                Category = action.Category,
                DisplayName = action.DisplayName,
                GesturesText = gestures.Count == 0
                    ? "(割り当てなし)"
                    : string.Join(", ", gestures.Select(DisplayGesture)),
                CustomMark = isDefault ? "" : "●",
            };
        }).ToList();

        var view = new ListCollectionView(_rows) { Filter = FilterRow };
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(KeyBindingRow.Category)));
        BindingList.ItemsSource = view;

        if (selectedId is not null)
            BindingList.SelectedItem = _rows.FirstOrDefault(r => r.ActionId == selectedId);
    }

    private static string Normalize(string gesture) =>
        KeyChord.TryParse(gesture, out var chord) ? chord.Normalized : gesture.ToUpperInvariant();

    private static string DisplayGesture(string gesture) =>
        KeyChord.TryParse(gesture, out var chord) ? chord.DisplayText : gesture;

    private bool FilterRow(object item)
    {
        var text = SearchBox.Text.Trim();
        if (text.Length == 0) return true;
        var row = (KeyBindingRow)item;
        return row.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               row.GesturesText.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               row.Category.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        (BindingList.ItemsSource as ICollectionView)?.Refresh();

    private KeyBindingRow? SelectedRow => BindingList.SelectedItem as KeyBindingRow;

    private void UpdateButtonStates()
    {
        var has = SelectedRow is not null;
        ChangeButton.IsEnabled = has;
        AddButton.IsEnabled = has;
        ClearButton.IsEnabled = has;
        ResetButton.IsEnabled = has;
    }

    // ---- キーのキャプチャ ----

    private void StartCapture(bool append)
    {
        if (SelectedRow is not { } row) return;
        _captureRow = row;
        _captureAppend = append;
        CaptureText.Text = $"「{row.DisplayName}」に割り当てるキーを押してください(Esc: 中止)";
        CaptureBar.Visibility = Visibility.Visible;
    }

    private void EndCapture()
    {
        _captureRow = null;
        CaptureBar.Visibility = Visibility.Collapsed;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_captureRow is null)
        {
            // Ctrl+数字でタブを切り替える(Ctrl+Tab の標準操作に加えた直接ジャンプ)。
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && TrySwitchTab(e.Key))
            {
                e.Handled = true;
                return;
            }

            // 検索ボックスでの Enter は OK(既定ボタン)で閉じず、一覧へフォーカスを移す。
            if (e.Key == Key.Enter && SearchBox.IsKeyboardFocusWithin)
            {
                BindingList.Focus();
                e.Handled = true;
                return;
            }

            // 非キャプチャ時: 一覧上の Enter は「キーを変更」、Delete は「割り当て解除」。
            if (BindingList.IsKeyboardFocusWithin && SelectedRow is not null)
            {
                if (e.Key == Key.Enter) { StartCapture(append: false); e.Handled = true; return; }
                if (e.Key == Key.Delete) { ClearSelected(); e.Handled = true; return; }
            }
            base.OnPreviewKeyDown(e);
            return;
        }

        // キャプチャ中はすべてのキーをここで消費する。
        e.Handled = true;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        if (KeyChordWpf.IsModifier(key)) return;
        if (key == Key.Escape) { EndCapture(); return; }

        var modifiers = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
        if (KeyChordWpf.FromKeyEvent(key, modifiers) is { } gesture)
        {
            AssignGesture(_captureRow, gesture, _captureAppend);
            EndCapture();
        }
    }

    /// <summary>Ctrl+数字キーならそのタブへ切り替える。切り替えたら true。</summary>
    private bool TrySwitchTab(Key key)
    {
        var digit = key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1 + 1,
            _ => 0,
        };
        if (digit == 0) return false;
        var index = SettingsTabNavigation.IndexForDigit(digit, Tabs.Items.Count);
        if (index < 0) return false;
        Tabs.SelectedIndex = index;
        return true;
    }

    /// <summary>ジェスチャを行へ割り当てる。他の操作と重複していたら確認して付け替える。</summary>
    private void AssignGesture(KeyBindingRow row, string gesture, bool append)
    {
        var current = _map.GesturesFor(row.ActionId);
        if (append && current.Any(g => Normalize(g) == Normalize(gesture)))
            return;

        // 衝突確認は同じコンテキスト内のみ(本体とプレビューで同じキーは共存できる)。
        var context = _map.Actions.FirstOrDefault(a => a.Id == row.ActionId)?.Context ?? KeyBindingContext.Global;
        var owner = _map.OwnerOf(gesture, context);
        if (owner is not null && owner != row.ActionId)
        {
            var ownerName = ActionLabel(owner);
            var answer = MessageBox.Show(this,
                $"「{DisplayGesture(gesture)}」は「{ownerName}」に割り当てられています。\n" +
                $"「{row.DisplayName}」へ割り当て直しますか?(元の操作からは外れます)",
                "キーの重複", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;
        }

        var gestures = append ? current.Append(gesture).ToList() : new List<string> { gesture };
        _map.Replace(row.ActionId, gestures);
        SyncToolGesturesFromMap();   // ツールが奪われた/奪った場合に _tools を同期
        RefreshRows();
        RefreshToolRows();
    }

    /// <summary>アクション Id の表示名(組み込み or ツール)。</summary>
    private string ActionLabel(string actionId) =>
        _map.Actions.FirstOrDefault(a => a.Id == actionId)?.DisplayName ?? actionId;

    private void BindingList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedRow is not null) StartCapture(append: false);
    }

    private void Change_Click(object sender, RoutedEventArgs e) => StartCapture(append: false);
    private void Add_Click(object sender, RoutedEventArgs e) => StartCapture(append: true);
    private void Clear_Click(object sender, RoutedEventArgs e) => ClearSelected();

    private void ClearSelected()
    {
        if (SelectedRow is not { } row) return;
        _map.Replace(row.ActionId, Array.Empty<string>());
        SyncToolGesturesFromMap();
        RefreshRows();
        RefreshToolRows();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row) return;
        _map.ResetToDefault(row.ActionId);
        SyncToolGesturesFromMap();
        RefreshRows();
        RefreshToolRows();
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "すべてのキー割り当てを既定に戻しますか?",
            "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        foreach (var action in _map.Actions)
            _map.ResetToDefault(action.Id);
        SyncToolGesturesFromMap();
        RefreshRows();
        RefreshToolRows();
    }

    // ---- 外部ツール一覧 ----

    private void RefreshToolRows()
    {
        var selectedId = (ToolList.SelectedItem as ToolRow)?.Id;
        var rows = _tools.Select(t => new ToolRow
        {
            Id = t.Id,
            Label = t.Label,
            KindText = t.Kind == ExternalToolKind.StoreApp ? "ストアアプリ" : "実行ファイル",
            Target = t.Target,
            KeyText = t.Gestures.Count == 0 ? "" : string.Join(", ", t.Gestures.Select(DisplayGesture)),
        }).ToList();
        ToolList.ItemsSource = rows;
        ToolList.SelectedItem = rows.FirstOrDefault(r => r.Id == selectedId);
        UpdateToolButtonStates();
    }

    private int SelectedToolIndex =>
        ToolList.SelectedItem is ToolRow row ? _tools.FindIndex(t => t.Id == row.Id) : -1;

    private void UpdateToolButtonStates()
    {
        var i = SelectedToolIndex;
        ToolEditButton.IsEnabled = i >= 0;
        ToolDuplicateButton.IsEnabled = i >= 0;
        ToolDeleteButton.IsEnabled = i >= 0;
        ToolUpButton.IsEnabled = i > 0;
        ToolDownButton.IsEnabled = i >= 0 && i < _tools.Count - 1;
    }

    private void ToolList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedToolIndex >= 0) EditToolAt(SelectedToolIndex);
    }

    private void ToolAdd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ToolEditDialog(null, ConflictLookup(null)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } draft) return;

        var id = GenerateId(draft.Label);
        var tool = new ExternalTool(id, draft.Label, draft.Kind, draft.Target, draft.Arguments, draft.Gestures);
        _tools.Add(tool);
        ApplyToolGestures(tool, freed: Array.Empty<string>());
        SelectTool(id);
    }

    private void ToolEdit_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedToolIndex >= 0) EditToolAt(SelectedToolIndex);
    }

    private void EditToolAt(int index)
    {
        var existing = _tools[index];
        var dialog = new ToolEditDialog(existing, ConflictLookup(existing.Id)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } draft) return;

        var tool = existing with
        {
            Label = draft.Label,
            Kind = draft.Kind,
            Target = draft.Target,
            Arguments = draft.Arguments,
            Gestures = draft.Gestures,
        };
        _tools[index] = tool;
        // このツールから外れたキーは、既定の組込操作へ戻す候補。
        var freed = existing.Gestures.Where(g => !draft.Gestures.Any(n => Normalize(n) == Normalize(g)));
        ApplyToolGestures(tool, freed);
        SelectTool(tool.Id);
    }

    private void ToolDuplicate_Click(object sender, RoutedEventArgs e)
    {
        var i = SelectedToolIndex;
        if (i < 0) return;
        var src = _tools[i];
        // 複製はキー重複を避けるため未割り当てにする。
        var copy = src with { Id = GenerateId(src.Label), Label = src.Label + " (コピー)", Gestures = new List<string>() };
        _tools.Insert(i + 1, copy);
        RebuildMap();
        RefreshRows();
        RefreshToolRows();
        SelectTool(copy.Id);
    }

    private void ToolDelete_Click(object sender, RoutedEventArgs e)
    {
        var i = SelectedToolIndex;
        if (i < 0) return;
        var tool = _tools[i];
        var answer = MessageBox.Show(this, $"外部ツール「{tool.Label}」を削除しますか?",
            "外部ツールの削除", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        _tools.RemoveAt(i);
        RebuildMap();
        RestoreDefaultOwners(tool.Gestures);   // 削除で空いたキーを既定の組込操作へ戻す
        RefreshRows();
        RefreshToolRows();
    }

    private void ToolUp_Click(object sender, RoutedEventArgs e) => MoveTool(-1);
    private void ToolDown_Click(object sender, RoutedEventArgs e) => MoveTool(1);

    private void MoveTool(int delta)
    {
        var i = SelectedToolIndex;
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _tools.Count) return;
        (_tools[i], _tools[j]) = (_tools[j], _tools[i]);
        RebuildMap();
        RefreshRows();
        RefreshToolRows();
        SelectTool(_tools[j].Id);
    }

    /// <summary>編集/追加したツールのキーを確定させる(対応表を再構築し、宣言済みキーを強制的に持たせる)。</summary>
    /// <param name="freed">このツールから外れたキー(既定の組込操作へ戻す候補)。</param>
    private void ApplyToolGestures(ExternalTool tool, IEnumerable<string> freed)
    {
        RebuildMap();
        // 編集画面で確認済みのキーを優先(他アクションから奪う)。
        _map.Replace(KeyBindingActions.ToolPrefix + tool.Id, tool.Gestures);
        RestoreDefaultOwners(freed);
        SyncToolGesturesFromMap();
        RefreshRows();
        RefreshToolRows();
    }

    /// <summary>
    /// 指定ジェスチャが現在どのアクションにも割り当てられていなければ、
    /// それを既定に持つ組込操作へ戻す(ツール削除/キー変更で空いた既定キーの復活)。
    /// </summary>
    private void RestoreDefaultOwners(IEnumerable<string> gestures)
    {
        foreach (var gesture in gestures)
        {
            if (_map.OwnerOf(gesture) is not null) continue;   // 既に誰かが持っている
            var action = KeyBindingActions.All.FirstOrDefault(a =>
                a.DefaultGestures.Any(dg => Normalize(dg) == Normalize(gesture)));
            if (action is not null)
                _map.ResetToDefault(action.Id);
        }
    }

    private void SelectTool(string id)
    {
        if (ToolList.ItemsSource is IEnumerable<ToolRow> rows)
            ToolList.SelectedItem = rows.FirstOrDefault(r => r.Id == id);
    }

    /// <summary>ジェスチャ→競合する操作の表示名(自分自身・未割り当てなら null)を返すルックアップ。</summary>
    private Func<string, string?> ConflictLookup(string? selfToolId) => gesture =>
    {
        var owner = _map.OwnerOf(gesture);
        if (owner is null) return null;
        if (selfToolId is not null && owner == KeyBindingActions.ToolPrefix + selfToolId) return null;
        return ActionLabel(owner);
    };

    /// <summary>ラベルから一意なツール Id を作る(英数字スラッグ。重複時は連番)。</summary>
    private string GenerateId(string label)
    {
        var sb = new StringBuilder();
        foreach (var c in label.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (c is ' ' or '-' or '_') sb.Append('-');
        }
        var baseId = sb.ToString().Trim('-');
        if (baseId.Length == 0) baseId = "tool";

        var id = baseId;
        var n = 2;
        while (_tools.Any(t => t.Id == id))
            id = $"{baseId}-{n++}";
        return id;
    }

    // ---- 決定 ----

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = new AppSettings(_map.ToOverrides(), _tools, SelectedTheme,
            LightweightUiaCheck.IsChecked == true,
            ConfirmMoveCheck.IsChecked == true,
            ConfirmRecycleCheck.IsChecked == true,
            ConfirmPermanentDeleteCheck.IsChecked == true,
            EnableFastSearchCheck.IsChecked == true,
            SelectedMarkupMode);
        DialogResult = true;
    }

    // ---- プレビュー(Markdown/HTML の初期表示モード) ----

    private void SetMarkupModeRadios(MarkupPreviewMode mode)
    {
        MarkupRendered.IsChecked = mode == MarkupPreviewMode.Rendered;
        MarkupHighlight.IsChecked = mode == MarkupPreviewMode.Highlight;
        MarkupText.IsChecked = mode == MarkupPreviewMode.Text;
    }

    private MarkupPreviewMode SelectedMarkupMode =>
        MarkupRendered.IsChecked == true ? MarkupPreviewMode.Rendered :
        MarkupText.IsChecked == true ? MarkupPreviewMode.Text :
        MarkupPreviewMode.Highlight;

    // ---- 外観(テーマ) ----

    /// <summary>ダイアログを開いた時点のテーマ(キャンセル時に戻す)。</summary>
    private readonly AppTheme _originalTheme;
    /// <summary>初期化中のラジオ設定でプレビューを走らせないためのガード。</summary>
    private bool _suppressThemeApply;

    /// <summary>テーマのラジオが選ばれたら即座に全ウィンドウへプレビュー適用する。</summary>
    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressThemeApply) return;
        ThemeManager.Apply(SelectedTheme);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // OK 以外(キャンセル・×・Esc)で閉じたらプレビューを元のテーマへ戻す。
        if (DialogResult != true)
            ThemeManager.Apply(_originalTheme);
    }

    private void SetThemeRadios(AppTheme theme)
    {
        ThemeDark.IsChecked = theme == AppTheme.Dark;
        ThemeLight.IsChecked = theme == AppTheme.Light;
        ThemeBeige.IsChecked = theme == AppTheme.Beige;
        ThemeGreen.IsChecked = theme == AppTheme.Green;
        ThemeNord.IsChecked = theme == AppTheme.Nord;
        ThemeSolarized.IsChecked = theme == AppTheme.Solarized;
        ThemeDracula.IsChecked = theme == AppTheme.Dracula;
        ThemeSystem.IsChecked = theme == AppTheme.System;
    }

    private AppTheme SelectedTheme =>
        ThemeLight.IsChecked == true ? AppTheme.Light :
        ThemeBeige.IsChecked == true ? AppTheme.Beige :
        ThemeGreen.IsChecked == true ? AppTheme.Green :
        ThemeNord.IsChecked == true ? AppTheme.Nord :
        ThemeSolarized.IsChecked == true ? AppTheme.Solarized :
        ThemeDracula.IsChecked == true ? AppTheme.Dracula :
        ThemeSystem.IsChecked == true ? AppTheme.System :
        AppTheme.Dark;
}
