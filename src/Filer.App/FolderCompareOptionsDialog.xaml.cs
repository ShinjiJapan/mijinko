using System.IO;
using System.Windows;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// フォルダー比較の前に表示するオプションダイアログ。比較基準(サイズ/更新日時/内容)・再帰・同一表示を
/// チェックボックスで指定する。既定値は前回値(<see cref="FolderComparePreferenceStore"/>)。
/// </summary>
public partial class FolderCompareOptionsDialog : Window
{
    /// <summary>OK 時に確定したオプション。</summary>
    public FolderCompareOptions Options { get; private set; }

    public FolderCompareOptionsDialog(string leftPath, string rightPath, FolderCompareOptions initial)
    {
        InitializeComponent();
        Ime.Disable(this);   // アクセスキーが日本語入力 ON でも効くよう IME を無効化する。
        Options = initial;

        TargetText.Text = $"左: {leftPath}\n右: {rightPath}";
        CompareSize.IsChecked = initial.CompareSize;
        CompareDate.IsChecked = initial.CompareDate;
        CompareContent.IsChecked = initial.CompareContent;
        Recursive.IsChecked = initial.Recursive;
        ShowSame.IsChecked = initial.ShowSame;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Options = new FolderCompareOptions(
            CompareSize: CompareSize.IsChecked == true,
            CompareDate: CompareDate.IsChecked == true,
            CompareContent: CompareContent.IsChecked == true,
            Recursive: Recursive.IsChecked == true,
            ShowSame: ShowSame.IsChecked == true);
        DialogResult = true;
        Close();
    }
}
