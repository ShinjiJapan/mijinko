using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// コマンドパレット。すべてのアクション(組み込み + 外部ツール)を一覧から検索して実行する。
/// 入力欄でフォーカスを保ったまま ↑↓ で候補移動、Enter で実行、Esc で閉じる。
/// 実行するアクション Id を <see cref="SelectedId"/> に入れて DialogResult=true で閉じる。
/// </summary>
public partial class CommandPaletteDialog : Window
{
    /// <summary>表示用の行(ジェスチャの有無で枠表示を切り替える)。</summary>
    private sealed class PaletteRow
    {
        public PaletteRow(CommandPaletteItem item) => Item = item;
        public CommandPaletteItem Item { get; }
        public string Title => Item.Title;
        public string Category => Item.Category;
        public string GestureText => Item.GestureText;
        public Visibility GestureVisibility =>
            string.IsNullOrEmpty(Item.GestureText) ? Visibility.Collapsed : Visibility.Visible;
    }

    private readonly IReadOnlyList<CommandPaletteItem> _all;

    /// <summary>実行対象に選ばれたアクション Id(キャンセル時は null)。</summary>
    public string? SelectedId { get; private set; }

    public CommandPaletteDialog(IReadOnlyList<CommandPaletteItem> items)
    {
        InitializeComponent();
        _all = items;
        ApplyFilter("");
        Loaded += (_, _) => Query.Focus();
    }

    private void Query_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter(Query.Text);

    private void ApplyFilter(string query)
    {
        var rows = CommandPaletteFilter.Filter(_all, query).Select(i => new PaletteRow(i)).ToList();
        List.ItemsSource = rows;
        if (rows.Count > 0)
            List.SelectedIndex = 0;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                return;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                return;
            case Key.PageDown:
                MoveSelection(10);
                e.Handled = true;
                return;
            case Key.PageUp:
                MoveSelection(-10);
                e.Handled = true;
                return;
            case Key.Enter:
                Commit();
                e.Handled = true;
                return;
            case Key.Escape:
                DialogResult = false;
                e.Handled = true;
                return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void MoveSelection(int delta)
    {
        var count = List.Items.Count;
        if (count == 0) return;
        var next = List.SelectedIndex + delta;
        next = next < 0 ? 0 : next >= count ? count - 1 : next;
        List.SelectedIndex = next;
        List.ScrollIntoView(List.SelectedItem);
    }

    private void List_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Commit();

    private void Commit()
    {
        if (List.SelectedItem is not PaletteRow row) return;
        SelectedId = row.Item.Id;
        DialogResult = true;
    }
}
