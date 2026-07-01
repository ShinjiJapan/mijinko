using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// 転送先に同名ファイルがあったときの処理を選ばせるダイアログ(だいなファイラー風)。
/// 上書き / 新しいものだけ転送 / 名前を変更して転送 / 転送しない から選択し、
/// 「すべてのファイルに適用」で残りの衝突へ同じ選択を一括適用できる。
/// </summary>
public partial class OverwriteDialog : Window
{
    public OverwriteDialog(TransferConflict conflict)
    {
        InitializeComponent();

        PathText.Text = conflict.ExistingPath;
        SrcInfo.Text = Format(conflict.SourceModifiedUtc, conflict.SourceSize);
        DestInfo.Text = Format(conflict.ExistingModifiedUtc, conflict.ExistingSize);

        var dir = Path.GetDirectoryName(conflict.ExistingPath)!;
        var original = Path.GetFileName(conflict.ExistingPath);
        RenameBox.Text = FileTransferService.MakeUniqueName(dir, original);

        Loaded += (_, _) =>
        {
            // 既存を失わない「名前を変更して転送」を既定にし、変更しやすいよう拡張子を除いて選択する。
            RenameRadio.IsChecked = true;
            RenameBox.Focus();
        };
    }

    /// <summary>選ばれた処理。</summary>
    public ConflictAction SelectedAction =>
        OverwriteRadio.IsChecked == true ? ConflictAction.Overwrite :
        NewerRadio.IsChecked == true ? ConflictAction.NewerOnly :
        RenameRadio.IsChecked == true ? ConflictAction.Rename :
        ConflictAction.Skip;

    /// <summary>「名前を変更して転送」のときの新しいファイル名。</summary>
    public string NewName => RenameBox.Text.Trim();

    /// <summary>残りの衝突にも同じ選択を適用するか。</summary>
    public bool ApplyToAll => ApplyAllCheck.IsChecked == true;

    private static string Format(System.DateTime utc, long size)
        => $"{utc.ToLocalTime():yyyy/MM/dd HH:mm:ss}      {size:N0} Bytes";

    private void RenameBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        RenameRadio.IsChecked = true;
        var stem = Path.GetFileNameWithoutExtension(RenameBox.Text);
        RenameBox.Select(0, stem.Length > 0 ? stem.Length : RenameBox.Text.Length);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAction == ConflictAction.Rename)
        {
            var name = NewName;
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "変更後の名前が不正です。", "名前の変更",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                RenameBox.Focus();
                return;
            }
        }
        DialogResult = true;
        Close();
    }
}
