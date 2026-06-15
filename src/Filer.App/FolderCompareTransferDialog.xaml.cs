using System.Windows;
using Filer.Core;

namespace Filer.App;

/// <summary>「転送して閉じる」で選んだ内容(左右ペインそれぞれの差分・重複)。</summary>
public sealed record FolderCompareTransferSelection(
    bool LeftDifferences, bool LeftSame, bool RightDifferences, bool RightSame)
{
    /// <summary>1つでも転送対象が選ばれているか。</summary>
    public bool Any => LeftDifferences || LeftSame || RightDifferences || RightSame;
}

/// <summary>
/// フォルダー比較の「転送して閉じる」ダイアログ。左右ペインそれぞれに「差分」「重複」を転送するか選ぶ。
/// 件数を各チェックボックスに表示する。
/// </summary>
public partial class FolderCompareTransferDialog : Window
{
    /// <summary>OK 時に確定した選択。</summary>
    public FolderCompareTransferSelection Selection { get; private set; } =
        new(false, false, false, false);

    public FolderCompareTransferDialog(FolderCompareSummary summary)
    {
        InitializeComponent();
        Ime.Disable(this);

        // 左の差分=変更+左のみ / 右の差分=変更+右のみ / 重複=同一(両側に存在)。
        LeftDiff.Content = $"差分 ({summary.Modified + summary.LeftOnly} 件)";
        LeftSame.Content = $"重複 ({summary.Same} 件)";
        RightDiff.Content = $"差分 ({summary.Modified + summary.RightOnly} 件)";
        RightSame.Content = $"重複 ({summary.Same} 件)";

        // 既定は差分のみ。
        LeftDiff.IsChecked = true;
        RightDiff.IsChecked = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Selection = new FolderCompareTransferSelection(
            LeftDiff.IsChecked == true, LeftSame.IsChecked == true,
            RightDiff.IsChecked == true, RightSame.IsChecked == true);
        DialogResult = true;
        Close();
    }
}
