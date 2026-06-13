using System.IO;
using System.Linq;
using System.Windows;
using Filer.App.ViewModels;
using Filer.Core;

namespace Filer.App;

/// <summary>アプリ起動。初期パス・タブ構成を決めてメインウィンドウを表示する。</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // すべてのウィンドウ(メイン+各ダイアログ)のタイトルバーを、表示時にテーマ色へ合わせる。
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((s, _) => WindowThemeHelper.Apply((Window)s)));

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var favoritesPath = Path.Combine(appData, "Filer", "favorites.json");
        var historyPath = Path.Combine(appData, "Filer", "history.json");
        var settingsPath = Path.Combine(appData, "Filer", "settings.json");
        var sessionStore = new SessionStore(Path.Combine(appData, "Filer", "session.json"));

        // 既定の単一タブ。前回終了時のセッションがあれば、今も存在するタブだけを復元する。
        var left = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var right = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!Directory.Exists(right)) right = left;
        var leftPane = new SessionPane(new[] { left }, 0);
        var rightPane = new SessionPane(new[] { right }, 0);
        var isLeftActive = true;

        var session = sessionStore.Load();
        if (session is not null)
        {
            leftPane = SanitizePane(session.Left) ?? leftPane;
            rightPane = SanitizePane(session.Right) ?? rightPane;
            isLeftActive = session.IsLeftActive;
        }

        var viewModel = new MainViewModel(leftPane, rightPane, isLeftActive, favoritesPath, historyPath, settingsPath);

        // 保存済みの外観テーマを起動時に適用する(System なら Windows 設定へ解決)。
        ThemeManager.Apply(viewModel.Settings.Theme);
        // ファイル一覧のUIA軽量化設定を起動時に適用する。
        PaneListView.LightweightAutomation = viewModel.Settings.LightweightListAutomation;

        var window = new MainWindow { DataContext = viewModel };
        if (session?.Window is { } bounds)
            ApplyWindowBounds(window, bounds);
        window.Closed += (_, _) => sessionStore.Save(viewModel.CaptureSession(CaptureWindowBounds(window)));
        window.Show();
    }

    /// <summary>保存済みのウィンドウ位置・サイズを適用する。画面外(モニター構成変更後など)は位置を捨てて既定に任せる。</summary>
    private static void ApplyWindowBounds(Window window, WindowBounds b)
    {
        var virtualRect = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var saved = new Rect(b.Left, b.Top, b.Width, b.Height);

        if (b.Width > 0 && b.Height > 0)
        {
            window.Width = b.Width;
            window.Height = b.Height;
        }
        // 仮想画面と十分重なる場合だけ位置を復元(僅かなはみ出しは許容)。それ以外は既定位置のまま。
        if (virtualRect.IntersectsWith(saved))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = b.Left;
            window.Top = b.Top;
        }
        if (b.Maximized)
            window.WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// 現在のウィンドウ位置・サイズを取得する。最大化/最小化中は復元用の通常サイズ(RestoreBounds)を保存する。
    /// RestoreBounds が未確定(Rect.Empty=Infinity を含む)なら Left/Top/Width/Height で代替する
    /// (Infinity は JSON 化できず保存が失敗するため)。
    /// </summary>
    private static WindowBounds CaptureWindowBounds(Window window)
    {
        var maximized = window.WindowState == WindowState.Maximized;
        var restore = window.RestoreBounds;
        var r = (maximized || window.WindowState == WindowState.Minimized) && IsFinite(restore)
            ? restore
            : new Rect(window.Left, window.Top, window.Width, window.Height);
        return new WindowBounds(r.Left, r.Top, r.Width, r.Height, maximized);
    }

    /// <summary>位置・サイズがすべて有限値(NaN/Infinity を含まない)か。</summary>
    private static bool IsFinite(Rect r) =>
        double.IsFinite(r.Left) && double.IsFinite(r.Top) &&
        double.IsFinite(r.Width) && double.IsFinite(r.Height);

    /// <summary>保存されたタブのうち今も存在するフォルダーだけを残す。1つも無ければ null。</summary>
    private static SessionPane? SanitizePane(SessionPane pane)
    {
        var existing = pane.TabPaths.Where(Directory.Exists).ToList();
        if (existing.Count == 0)
            return null;
        var active = Math.Clamp(pane.ActiveTabIndex, 0, existing.Count - 1);
        return new SessionPane(existing, active);
    }
}
