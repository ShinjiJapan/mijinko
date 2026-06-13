using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// ソート方法(名前/拡張子/日付/サイズ)と昇降順を選ぶダイアログ。
/// N/E/D/S で方法、A/R で順番を選択し、Enter で決定、Esc でキャンセルする。
/// </summary>
public partial class SortDialog : Window
{
    public SortKey SelectedKey { get; private set; }
    public bool Descending { get; private set; }

    public SortDialog(SortKey current, bool descending)
    {
        InitializeComponent();
        // 文字入力欄はなくキー選択のみ。日本語入力 ON でも N/E/D/S 等が効くよう IME を無効化する。
        Ime.Disable(this);
        SelectedKey = current;
        Descending = descending;
        MethodRadio(current).IsChecked = true;
        (descending ? OrderDesc : OrderAsc).IsChecked = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        switch (e.Key)
        {
            case Key.N: MethodName.IsChecked = true; e.Handled = true; break;
            case Key.E: MethodExt.IsChecked = true; e.Handled = true; break;
            case Key.D: MethodDate.IsChecked = true; e.Handled = true; break;
            case Key.S: MethodSize.IsChecked = true; e.Handled = true; break;
            case Key.A: OrderAsc.IsChecked = true; e.Handled = true; break;
            case Key.R: OrderDesc.IsChecked = true; e.Handled = true; break;
            case Key.Enter: Commit(); e.Handled = true; break;
            case Key.Escape: DialogResult = false; Close(); e.Handled = true; break;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Commit();

    private void Commit()
    {
        SelectedKey =
            MethodExt.IsChecked == true ? SortKey.Extension :
            MethodDate.IsChecked == true ? SortKey.Date :
            MethodSize.IsChecked == true ? SortKey.Size :
            SortKey.Name;
        Descending = OrderDesc.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private RadioButton MethodRadio(SortKey key) => key switch
    {
        SortKey.Extension => MethodExt,
        SortKey.Date => MethodDate,
        SortKey.Size => MethodSize,
        _ => MethodName,
    };
}
