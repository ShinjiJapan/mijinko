using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Filer.App;

/// <summary>
/// インクリメンタルサーチダイアログ(E)。入力のたびに検索コールバックを呼び、
/// 呼び出し側(MainWindow)がアクティブペインのカーソルを一致項目へ動かす。
/// Enter・閉じる=確定、Esc・×=キャンセル(呼び出し側がカーソルを元の位置へ戻す)。
/// </summary>
public partial class IncrementalSearchDialog : Window
{
    private readonly Func<string, bool, bool> _search;           // (query, prefixOnly) → 一致したか
    private readonly Func<string, bool, bool, bool> _searchNext; // (query, prefixOnly, backward) → 一致したか

    public IncrementalSearchDialog(Func<string, bool, bool> search, Func<string, bool, bool, bool> searchNext)
    {
        InitializeComponent();
        _search = search;
        _searchNext = searchNext;
        Loaded += (_, _) =>
        {
            PlaceOverOwner();
            Query.Focus();
        };
    }

    private bool PrefixOnly => PrefixCheck.IsChecked == true;

    /// <summary>オーナーウィンドウの右下寄り(一覧に被りにくい位置)へ表示する。</summary>
    private void PlaceOverOwner()
    {
        if (Owner is null || PresentationSource.FromVisual(Owner) is not { } source)
            return;
        var bottomRight = Owner.PointToScreen(new Point(Owner.ActualWidth, Owner.ActualHeight));
        var dip = source.CompositionTarget.TransformFromDevice.Transform(bottomRight);
        Left = dip.X - ActualWidth - 32;
        Top = dip.Y - ActualHeight - 64;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                ShowFound(_searchNext(Query.Text, PrefixOnly, false));
                e.Handled = true;
                return;
            case Key.Up:
                ShowFound(_searchNext(Query.Text, PrefixOnly, true));
                e.Handled = true;
                return;
            case Key.Escape:
                DialogResult = false;
                e.Handled = true;
                return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void Query_TextChanged(object sender, TextChangedEventArgs e) => RunSearch();

    private void Prefix_Changed(object sender, RoutedEventArgs e) => RunSearch();

    private void RunSearch()
    {
        if (!IsLoaded) return;   // InitializeComponent 中の発火は無視
        ShowFound(_search(Query.Text, PrefixOnly));
    }

    /// <summary>「一致なし」表示を更新する(クエリが空のときは出さない)。</summary>
    private void ShowFound(bool found)
    {
        NoMatchText.Visibility = !found && Query.Text.Length > 0
            ? Visibility.Visible
            : Visibility.Hidden;
    }

    private void SearchUp_Click(object sender, RoutedEventArgs e) =>
        ShowFound(_searchNext(Query.Text, PrefixOnly, true));

    private void SearchDown_Click(object sender, RoutedEventArgs e) =>
        ShowFound(_searchNext(Query.Text, PrefixOnly, false));

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
