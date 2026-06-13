using System.Windows;

namespace Filer.App;

/// <summary>1行テキスト入力ダイアログ(リネーム等に使用)。</summary>
public partial class InputDialog : Window
{
    public InputDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    public string InputText => Input.Text;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
