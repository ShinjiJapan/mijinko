using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Filer.App.ExternalTools;

namespace Filer.App;

/// <summary>
/// インストール済みアプリ(ストアアプリ+スタートメニュー登録アプリ)を一覧から選ぶダイアログ。
/// 選択結果は <see cref="SelectedApp"/>(AUMID と表示名)。列挙は時間がかかるため非同期で読み込む。
/// </summary>
public partial class AppPickerDialog : Window
{
    private List<InstalledApp> _apps = new();

    /// <summary>選択されたアプリ(キャンセルなら null)。</summary>
    public InstalledApp? SelectedApp { get; private set; }

    public AppPickerDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _apps = (await ListAsync()).ToList();
        AppList.ItemsSource = new ListCollectionView(_apps) { Filter = FilterApp };
        StatusText.Text = _apps.Count == 0
            ? "アプリを取得できませんでした。AUMID を手入力してください。"
            : $"{_apps.Count} 件。検索で絞り込み、ダブルクリックまたは OK で選択。";
        SearchBox.Focus();
    }

    /// <summary>shell:AppsFolder の列挙は STA スレッドで実行する(Shell COM の都合)。</summary>
    private static Task<IReadOnlyList<InstalledApp>> ListAsync()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<InstalledApp>>();
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(InstalledAppLister.List()); }
            catch (System.Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private bool FilterApp(object item)
    {
        var text = SearchBox.Text.Trim();
        if (text.Length == 0) return true;
        var app = (InstalledApp)item;
        return app.Name.Contains(text, System.StringComparison.OrdinalIgnoreCase) ||
               app.Aumid.Contains(text, System.StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        (AppList.ItemsSource as ICollectionView)?.Refresh();

    private void AppList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AppList.SelectedItem is InstalledApp) Accept();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        if (AppList.SelectedItem is not InstalledApp app) return;
        SelectedApp = app;
        DialogResult = true;
    }
}
