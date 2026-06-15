using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Filer.App;

/// <summary>
/// 表示文字列と値を持つ選択肢。Children を持つ項目はグループ(サブメニュー)になり、
/// 決定の代わりに 1 階層下の一覧へ潜る。
/// </summary>
public sealed record SelectionEntry(string Display, string Value, IReadOnlyList<SelectionEntry>? Children = null)
{
    public bool IsGroup => Children is not null;

    /// <summary>numbered 表示時の番号プレフィックス("3. " など)。非 numbered・10件目以降は空。</summary>
    public string Number { get; init; } = "";

    /// <summary>グループ行のマーカー(フォルダーアイコン・「›」)の表示(DataTemplate からバインド)。</summary>
    public Visibility ChevronVisibility => IsGroup ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// キーボード駆動の一覧選択ダイアログ。ドライブ選択・お気に入り選択・履歴で共用する。
/// ↑↓ で選択、Enter で決定、Esc でキャンセル、シングルクリックで決定。
/// numbered 指定時は各項目に 1〜 の番号を振り、その数字キーで即決定する(先頭9件・階層ごと)。
/// letterSelect 指定時は Value 先頭文字(ドライブ文字)のキーで即決定する。
/// グループ(Children 付き)行は Enter/→/クリックで中へ、←/BS で 1 つ上へ戻る。
/// プロンプト行に現在のグループ階層をパンくず表示する。
/// onEdit/onDelete を渡すと各項目に編集・削除ボタンを表示し、操作後 reload で一覧を再構築する
/// (表示中の階層位置は維持)。
/// </summary>
public partial class SelectionDialog : Window
{
    private const int MaxNumbered = 9;

    public string? SelectedValue { get; private set; }

    /// <summary>編集・削除ボタンの表示有無(DataTemplate からバインド)。</summary>
    public Visibility EditButtonsVisibility { get; }

    /// <summary>並べ替え(↑↓)ボタンの表示有無(DataTemplate からバインド)。</summary>
    public Visibility ReorderButtonsVisibility { get; }

    private readonly bool _numbered;
    private readonly bool _letterSelect;
    private readonly Func<IReadOnlyList<SelectionEntry>>? _reload;
    private readonly Action<Window, string>? _onEdit;
    private readonly Action<Window, string>? _onDelete;
    private readonly Func<string, int, bool>? _onReorder;
    private readonly string _basePrompt;

    private IReadOnlyList<SelectionEntry> _root;
    private readonly List<SelectionEntry> _groupStack = new();
    private readonly List<int> _indexStack = new();

    public SelectionDialog(string title, string prompt, IReadOnlyList<SelectionEntry> entries,
        bool numbered = false,
        bool letterSelect = false,
        Func<IReadOnlyList<SelectionEntry>>? reload = null,
        Action<Window, string>? onEdit = null,
        Action<Window, string>? onDelete = null,
        Func<string, int, bool>? onReorder = null)
    {
        InitializeComponent();
        // 文字入力欄はなくキー選択のみ。日本語入力 ON でも数字・ドライブ文字キーが効くよう IME を無効化する。
        Ime.Disable(this);
        Title = title;
        _basePrompt = prompt;
        _numbered = numbered;
        _letterSelect = letterSelect;
        _reload = reload;
        _onEdit = onEdit;
        _onDelete = onDelete;
        _onReorder = onReorder;
        _root = entries;

        var editable = reload != null && onEdit != null && onDelete != null;
        EditButtonsVisibility = editable ? Visibility.Visible : Visibility.Collapsed;
        // 並べ替えは reload で一覧を再構築できることが前提。
        ReorderButtonsVisibility = onReorder != null && reload != null ? Visibility.Visible : Visibility.Collapsed;

        RefreshList();
        HelpText.Text = BuildHelpText(ContainsGroup(entries));
        Loaded += (_, _) => FocusSelectedItem();
    }

    /// <summary>
    /// 選択中の行(ListBoxItem)へフォーカスする。ListBox 本体にフォーカスすると最初の↓が
    /// 「選択行へフォーカスを移す」だけで消費され、カーソルが1つ進まないため、必ず選択行の
    /// コンテナにフォーカスを当てる(メインウィンドウの一覧と同じ対策)。
    /// </summary>
    private void FocusSelectedItem()
    {
        var index = List.SelectedIndex;
        if (index < 0)
        {
            List.Focus();
            return;
        }
        List.ScrollIntoView(List.Items[index]);
        List.UpdateLayout();   // 仮想化されたコンテナを実体化させる
        if (List.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem item)
            item.Focus();
        else
            List.Focus();
    }

    private string BuildHelpText(bool hierarchical)
    {
        var parts = new List<string>();
        if (_numbered)
            parts.Add("1〜9:番号で選択");
        if (_letterSelect)
            parts.Add("A〜Z:ドライブ文字で選択");
        parts.Add("↑↓:選択");
        parts.Add("Enter/クリック:決定");
        if (hierarchical)
            parts.Add("→:グループを開く");
        if (hierarchical)
            parts.Add("←/BS:戻る");
        if (_onReorder != null)
            parts.Add("Ctrl+↑↓:並べ替え");
        parts.Add("Esc:キャンセル");
        return string.Join("  ", parts);
    }

    private static bool ContainsGroup(IReadOnlyList<SelectionEntry> entries) =>
        entries.Any(e => e.IsGroup || (e.Children is { } c && ContainsGroup(c)));

    /// <summary>現在表示中の階層の選択肢(グループ未進入時はルート)。</summary>
    private IReadOnlyList<SelectionEntry> CurrentEntries =>
        _groupStack.Count == 0 ? _root : _groupStack[^1].Children!;

    /// <summary>現在の階層を一覧へ反映し、パンくず付きプロンプトを更新する。</summary>
    private void RefreshList(int selectIndex = 0)
    {
        var entries = CurrentEntries;
        List.ItemsSource = _numbered
            ? entries.Select((e, i) =>
                i < MaxNumbered ? e with { Number = $"{i + 1}. " } : e).ToList()
            : entries;

        if (List.Items.Count > 0)
            List.SelectedIndex = Math.Clamp(selectIndex, 0, List.Items.Count - 1);

        PromptText.Text = _groupStack.Count == 0
            ? _basePrompt
            : $"{_basePrompt}  [{string.Join(" › ", _groupStack.Select(g => g.Display))}]";

        // 階層移動・再読込でも選択行にフォーカスを当て直す(最初の↓を無駄にしない)。
        // 構築時(未 Loaded)は Loaded ハンドラー側で当てる。
        if (IsLoaded)
            FocusSelectedItem();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        switch (e.Key)
        {
            case Key.Up when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                MoveSelected(-1);
                e.Handled = true;
                break;
            case Key.Down when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                MoveSelected(1);
                e.Handled = true;
                break;
            case Key.Enter:
                ActivateSelection();
                e.Handled = true;
                break;
            case Key.Right:
                if (List.SelectedItem is SelectionEntry { IsGroup: true })
                {
                    ActivateSelection();
                    e.Handled = true;
                }
                break;
            case Key.Left:
            case Key.Back:
                if (_groupStack.Count > 0)
                {
                    LeaveGroup();
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                DialogResult = false;
                Close();
                e.Handled = true;
                break;
            default:
                if (_numbered && TryGetDigitIndex(e.Key, out var digit)
                    && digit < List.Items.Count)
                {
                    List.SelectedIndex = digit;
                    ActivateSelection();
                    e.Handled = true;
                }
                else if (_letterSelect && TryMatchLetterIndex(e.Key, out var letter))
                {
                    List.SelectedIndex = letter;
                    ActivateSelection();
                    e.Handled = true;
                }
                break;
        }
    }

    /// <summary>1〜9 のキー(上段/テンキー)を 0 始まりのインデックスへ変換する。</summary>
    private static bool TryGetDigitIndex(Key key, out int index)
    {
        if (key is >= Key.D1 and <= Key.D9)
        {
            index = key - Key.D1;
            return true;
        }
        if (key is >= Key.NumPad1 and <= Key.NumPad9)
        {
            index = key - Key.NumPad1;
            return true;
        }
        index = -1;
        return false;
    }

    /// <summary>A〜Z キーを、Value 先頭文字(ドライブ文字)が一致する最初の項目へ対応付ける。</summary>
    private bool TryMatchLetterIndex(Key key, out int index)
    {
        index = -1;
        if (key is < Key.A or > Key.Z)
            return false;
        var c = (char)('A' + (key - Key.A));
        for (var i = 0; i < List.Items.Count; i++)
        {
            if (List.Items[i] is SelectionEntry entry && entry.Value.Length > 0
                && char.ToUpperInvariant(entry.Value[0]) == c)
            {
                index = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>行のシングルクリックで決定する(ボタン上のクリックは Button が処理するため反応しない)。</summary>
    private void List_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsWithinItem(e.OriginalSource as DependencyObject))
            ActivateSelection();
    }

    private static bool IsWithinItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ListBoxItem)
                return true;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    /// <summary>選択中の行を実行する(グループは中へ、項目は決定)。</summary>
    private void ActivateSelection()
    {
        if (List.SelectedItem is not SelectionEntry entry)
            return;
        if (entry.IsGroup)
            EnterGroup(entry);
        else
            Commit(entry);
    }

    private void EnterGroup(SelectionEntry displayed)
    {
        // 番号付き表示のクローンではなく元エントリを積む(パンくず表示と reload 後の復元に使う)
        var original = CurrentEntries.FirstOrDefault(x => x.Value == displayed.Value) ?? displayed;
        _indexStack.Add(List.SelectedIndex);
        _groupStack.Add(original);
        RefreshList();
    }

    private void LeaveGroup()
    {
        if (_groupStack.Count == 0)
            return;
        _groupStack.RemoveAt(_groupStack.Count - 1);
        var index = _indexStack[^1];
        _indexStack.RemoveAt(_indexStack.Count - 1);
        RefreshList(index);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveRow(sender, -1, e);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveRow(sender, 1, e);

    /// <summary>↑↓ ボタン: その行を選択してから上下に移動する(選択は移動先へ追従)。</summary>
    private void MoveRow(object sender, int delta, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SelectionEntry entry)
        {
            var index = IndexOfValue(entry.Value);
            if (index >= 0)
                List.SelectedIndex = index;
            MoveSelected(delta);
        }
        e.Handled = true;
    }

    /// <summary>選択中の行を delta だけ上下へ移動し、選択を移動先へ追従させる。</summary>
    private void MoveSelected(int delta)
    {
        if (_onReorder is null || List.SelectedItem is not SelectionEntry entry)
            return;
        var index = List.SelectedIndex;
        if (_onReorder(entry.Value, delta))
            ReloadKeepingLocation(index + delta);
    }

    /// <summary>現在表示中の一覧で Value が一致する行の位置(無ければ -1)。</summary>
    private int IndexOfValue(string value)
    {
        for (var i = 0; i < List.Items.Count; i++)
            if (List.Items[i] is SelectionEntry e && e.Value == value)
                return i;
        return -1;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SelectionEntry entry || _onEdit is null)
            return;
        _onEdit(this, entry.Value);
        ReloadKeepingLocation();
        e.Handled = true;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SelectionEntry entry || _onDelete is null)
            return;
        _onDelete(this, entry.Value);
        ReloadKeepingLocation();
        e.Handled = true;
    }

    /// <summary>一覧を再取得し、表示中の階層位置を Value で辿り直す(消えた階層はその手前まで)。
    /// targetIndex 指定時はその位置を選択する(並べ替えで移動先へ追従させる用)。</summary>
    private void ReloadKeepingLocation(int? targetIndex = null)
    {
        if (_reload is null)
            return;
        var keepIndex = targetIndex ?? List.SelectedIndex;
        var groupValues = _groupStack.Select(g => g.Value).ToList();
        var oldIndexes = _indexStack.ToList();
        _root = _reload();
        _groupStack.Clear();
        _indexStack.Clear();
        var level = _root;
        for (var i = 0; i < groupValues.Count; i++)
        {
            if (level.FirstOrDefault(x => x.IsGroup && x.Value == groupValues[i]) is not { } group)
                break;
            _indexStack.Add(oldIndexes[i]);
            _groupStack.Add(group);
            level = group.Children!;
        }
        RefreshList(Math.Max(0, keepIndex));
    }

    private void Commit(SelectionEntry entry)
    {
        SelectedValue = entry.Value;
        DialogResult = true;
        Close();
    }
}
