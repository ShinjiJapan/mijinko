using System.Collections.Generic;
using System.Windows;

namespace Filer.App;

/// <summary>
/// お気に入り 1 件のラベル・グループ・パスを入力するダイアログ(登録と編集で兼用)。
/// グループは自由入力(「仕事/CLI」の / 区切りで階層化・空=ルート直下)に加え、
/// 編集可能 ComboBox の ▼ で既存グループから選択できる。
/// </summary>
public partial class FavoriteEditDialog : Window
{
    public FavoriteEditDialog(string title, string path, string label, string group, IReadOnlyList<string> groups)
    {
        InitializeComponent();
        Title = title;
        PathInput.Text = path;
        LabelInput.Text = label;
        GroupInput.ItemsSource = groups;
        GroupInput.Text = group;
        Loaded += (_, _) =>
        {
            LabelInput.Focus();
            LabelInput.SelectAll();
        };
    }

    public string PathText => PathInput.Text.Trim();

    public string LabelText => LabelInput.Text.Trim();

    /// <summary>グループパス(前後の空白と / を除去。空=ルート直下)。</summary>
    public string GroupText => GroupInput.Text.Trim().Trim('/').Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PathInput.Text))
        {
            MessageBox.Show(this, "パスを入力してください。", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
        Close();
    }
}
