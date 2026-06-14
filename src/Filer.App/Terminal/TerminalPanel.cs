using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Filer.Core;

namespace Filer.App.Terminal;

/// <summary>
/// 組み込みターミナルパネル。上部にタブ列(+ 新規 / ˅ 種類選択 / 各タブに ×)、
/// 下にアクティブタブのターミナルを表示する。VSCode のターミナルと同様に
/// タブの追加・切り替え・削除ができる。全タブが閉じたら <see cref="AllTabsClosed"/> を発火する。
/// </summary>
public sealed class TerminalPanel : DockPanel
{
    private static readonly Brush TabBarBg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly Brush TabActiveBg = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
    private static readonly Brush TabInactiveBg = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));
    private static readonly Brush TabHoverBg = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42));
    private static readonly Brush FgActive = Brushes.White;
    private static readonly Brush FgInactive = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
    private static readonly Brush FgDim = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    private readonly Func<string> _cwdProvider;
    private readonly Func<TerminalProfile?> _defaultProfileProvider;
    // 設定キー割り当て。terminal.html の JS 判定(表示切替・一覧へフォーカス戻し)へ埋め込む。
    private readonly KeyBindingMap _keyMap;
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal };
    private readonly Grid _sessionHost = new();
    private readonly List<TerminalSessionView> _views = new();
    private int _activeIndex = -1;

    private static Task<CoreWebView2Environment>? _envTask;

    /// <summary>最後のタブが閉じられた(パネルを畳んでよい)。</summary>
    public event Action? AllTabsClosed;

    /// <summary>ターミナル内のフォーカス戻しキー(設定値)によるファイラー一覧へのフォーカス戻し要求。</summary>
    public event Action? FocusListRequested;

    /// <summary>ターミナル内の表示切替キーによる表示形態の切替要求。</summary>
    public event Action? CycleViewRequested;

    /// <summary>新しいタブを開いた(その種別を既定として記憶するため通知)。</summary>
    public event Action<TerminalProfile>? ProfileOpened;

    public int TabCount => _views.Count;

    public TerminalPanel(Func<string> cwdProvider, Func<TerminalProfile?> defaultProfileProvider,
        KeyBindingMap keyMap)
    {
        _cwdProvider = cwdProvider;
        _defaultProfileProvider = defaultProfileProvider;
        _keyMap = keyMap;
        Background = TabActiveBg;

        var addButton = MakeIconButton("+", "新しいターミナル (T)", 15);
        addButton.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            var profile = _defaultProfileProvider();
            if (profile != null) OpenNewTab(profile, _cwdProvider());
        };

        var pickButton = MakeIconButton("˅", "種類を選んで新しいターミナル (Shift+T)", 13);
        pickButton.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            ShowProfileMenu(pickButton);
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(addButton);
        buttons.Children.Add(pickButton);

        var bar = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(_tabStrip);

        var barBorder = new Border { Background = TabBarBg, Padding = new Thickness(4, 2, 4, 0), Child = bar };
        DockPanel.SetDock(barBorder, Dock.Top);
        Children.Add(barBorder);
        Children.Add(_sessionHost);
    }

    /// <summary>共有の WebView2 環境(プレビューと同じ userDataFolder)。</summary>
    private static Task<CoreWebView2Environment> GetEnvironmentAsync() =>
        _envTask ??= CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Filer", "WebView2"));

    /// <summary>指定シェルの新しいタブを開いてアクティブにする。</summary>
    public async void OpenNewTab(TerminalProfile profile, string workingDirectory)
    {
        ProfileOpened?.Invoke(profile);   // この種別を次回の既定として記憶する
        var view = new TerminalSessionView(profile, workingDirectory);
        view.SessionExited += CloseTab;
        view.FocusListRequested += () => FocusListRequested?.Invoke();
        view.CycleViewRequested += () => CycleViewRequested?.Invoke();
        _views.Add(view);
        _sessionHost.Children.Add(view);
        Activate(_views.Count - 1);

        try
        {
            await view.InitializeAsync(await GetEnvironmentAsync(), TerminalAssets.EnsureExtracted(
                _keyMap.GesturesFor("view.toggleFullscreen"), _keyMap.GesturesFor("terminal.focusBack")));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),
                $"ターミナルを起動できません(WebView2 ランタイムが必要です)。\n{ex.Message}",
                "ターミナル", MessageBoxButton.OK, MessageBoxImage.Warning);
            CloseTab(view);
        }
    }

    /// <summary>アクティブなタブのターミナルへフォーカスを移す。</summary>
    public void FocusActiveTerminal()
    {
        if (_activeIndex >= 0 && _activeIndex < _views.Count)
            _views[_activeIndex].FocusTerminal();
    }

    /// <summary>全タブのシェルを終了して破棄する(アプリ終了時)。AllTabsClosed は発火しない。</summary>
    public void CloseAll()
    {
        foreach (var view in _views)
        {
            view.SessionExited -= CloseTab;   // 破棄に伴う Exited で再入させない
            view.DisposeSession();
        }
        _views.Clear();
        _sessionHost.Children.Clear();
        _activeIndex = -1;
        RebuildTabStrip();
    }

    /// <summary>タブを閉じる(シェル終了時の自動クローズ・×ボタン共用)。</summary>
    private void CloseTab(TerminalSessionView view)
    {
        var index = _views.IndexOf(view);
        if (index < 0) return;   // 破棄済みセッションの Exited 再入
        view.SessionExited -= CloseTab;
        view.DisposeSession();
        _views.RemoveAt(index);
        _sessionHost.Children.Remove(view);

        if (_views.Count == 0)
        {
            _activeIndex = -1;
            RebuildTabStrip();
            AllTabsClosed?.Invoke();
            return;
        }
        Activate(Math.Min(index, _views.Count - 1));
    }

    /// <summary>指定タブを表示・フォーカスし、タブ列の見た目を更新する。</summary>
    private void Activate(int index)
    {
        _activeIndex = index;
        for (var i = 0; i < _views.Count; i++)
            _views[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        RebuildTabStrip();
        _views[index].FocusTerminal();
    }

    /// <summary>タブ列を現状から作り直す(見出し=シェル種別名、×で閉じる)。</summary>
    private void RebuildTabStrip()
    {
        _tabStrip.Children.Clear();
        for (var i = 0; i < _views.Count; i++)
        {
            var view = _views[i];
            var isActive = i == _activeIndex;

            var title = new TextBlock
            {
                Text = view.Profile.Name,
                Foreground = isActive ? FgActive : FgInactive,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var close = new TextBlock
            {
                Text = "×",
                Foreground = FgDim,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "タブを閉じる",
            };
            close.MouseLeftButtonDown += (_, e) => { e.Handled = true; CloseTab(view); };

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(title);
            content.Children.Add(close);

            var tab = new Border
            {
                Background = isActive ? TabActiveBg : TabInactiveBg,
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 2, 0),
                Cursor = Cursors.Hand,
                Child = content,
                SnapsToDevicePixels = true,
            };
            var tabIndex = i;
            tab.MouseLeftButtonDown += (_, e) => { e.Handled = true; Activate(tabIndex); };
            if (!isActive)
            {
                tab.MouseEnter += (_, _) => tab.Background = TabHoverBg;
                tab.MouseLeave += (_, _) => tab.Background = TabInactiveBg;
            }
            _tabStrip.Children.Add(tab);
        }
    }

    /// <summary>˅ ボタン: 利用できるシェル種別のメニューを出し、選んだ種別で新しいタブを開く。</summary>
    private void ShowProfileMenu(UIElement placementTarget)
    {
        var menu = new ContextMenu { PlacementTarget = placementTarget };
        foreach (var profile in TerminalProfiles.Detect())
        {
            var item = new MenuItem { Header = profile.Name };
            var captured = profile;
            item.Click += (_, _) => OpenNewTab(captured, _cwdProvider());
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>タブバー右側の小ボタン(+/˅)。Border ベースでダークテーマに合わせる。</summary>
    private static Border MakeIconButton(string text, string toolTip, double fontSize)
    {
        var border = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 0, 6, 0),
            Cursor = Cursors.Hand,
            ToolTip = toolTip,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                Foreground = FgInactive,
                FontSize = fontSize,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        border.MouseEnter += (_, _) => border.Background = TabHoverBg;
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        return border;
    }
}
