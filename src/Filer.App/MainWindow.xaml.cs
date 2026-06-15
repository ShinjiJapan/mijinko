using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Filer.App.Terminal;
using Filer.App.ViewModels;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// 2画面ファイラーのメインウィンドウ。キーボード操作を ViewModel の操作へ振り分ける。
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        // メインウィンドウに文字入力欄はない(入力は別ダイアログ)。日本語入力 ON のままでも
        // ショートカットが効くよう IME を無効化する(詳細は Ime.Disable 参照)。
        Ime.Disable(this);
        // 起動時・別アプリから戻った時など、ウィンドウがアクティブ化されたら
        // ListView コンテナではなく選択行にフォーカスを戻す(最初の矢印キーを活かす)。
        Activated += (_, _) => FocusActiveList();

        // 名前以外のカラムは内容に応じた Auto 幅、名前カラムが残り幅を埋める。
        // ペイン幅変化では残り幅(名前)だけ追従。一覧の中身が入れ替わったとき
        // (ディレクトリ移動・更新)は Auto 列を再計測してから名前へ残り幅を割り当てる。
        foreach (var list in new[] { LeftList, RightList })
        {
            var captured = list;
            captured.SizeChanged += (_, _) => StretchNameColumn(captured);
            ((INotifyCollectionChanged)captured.Items).CollectionChanged += (_, _) =>
                Dispatcher.BeginInvoke(() => RefreshColumns(captured), DispatcherPriority.Loaded);
        }

        // アプリ外へのファイル drag&drop(コピー)とダブルクリック(Enter 相当)。詳細・グリッド両方に配線。
        foreach (var list in new[] { LeftList, RightList, LeftGrid, RightGrid })
        {
            list.PreviewMouseLeftButtonDown += List_PreviewMouseLeftButtonDown;
            list.PreviewMouseMove += List_PreviewMouseMove;
            list.PreviewMouseLeftButtonUp += (_, _) => _dragArmed = false;
            list.MouseDoubleClick += List_MouseDoubleClick;
        }

        // キー割り当てとフッターヘルプは設定(ViewModel)から構築する。
        _builtinActions = BuildActions();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            RebuildKeyBindings();
            if (PerfLogPath is not null)
            {
                // 計測時はフォルダー移動全般(ドライブ/お気に入り/履歴/親移動含む)でレイアウト時間を記録する。
                vm.Left.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PaneViewModel.CurrentPath)) LogRenderIdle($"nav L {vm.Left.CurrentPath}");
                };
                vm.Right.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PaneViewModel.CurrentPath)) LogRenderIdle($"nav R {vm.Right.CurrentPath}");
                };
            }
        };

        // 別アプリへ移った隙に Ctrl を離した等で表示が固まらないよう、非アクティブ化で非 Ctrl 表示へ戻す。
        Deactivated += (_, _) => UpdateKeyHelp();

        // メモへフォーカスが出入りしたら(キー押下を待たずに)フッターを切り替える。
        // ターミナル(WebView2)はフォーカスが WPF の IsKeyboardFocusWithin に安定して
        // 反映されないため _terminalFocused フラグで追跡する(FocusTerminalPanel/FocusActiveList)。
        MemoHost.IsKeyboardFocusWithinChanged += (_, _) => UpdateKeyHelp();

        // AvalonEdit の実フォーカスは内部の TextArea が受けるため、IME 有効化と
        // 無効化除外(Ime.AllowInput)を TextArea 側にも設定する。これがないと
        // Ime.Disable のフォーカスハンドラがメモ欄の IME を切ってしまう。
        InputMethod.SetIsInputMethodEnabled(MemoBox.TextArea, true);
        Ime.SetAllowInput(MemoBox.TextArea, true);
        InputMethod.SetIsInputMethodEnabled(EditorBox.TextArea, true);
        Ime.SetAllowInput(EditorBox.TextArea, true);

        // エディターへフォーカスが出入りしたらフッターをエディター用キーへ切り替える。
        EditorHost.IsKeyboardFocusWithinChanged += (_, _) => UpdateKeyHelp();
    }

    /// <summary>ターミナル(WebView2)にフォーカスがあるか。フッター表示の状態判定に使う。</summary>
    private bool _terminalFocused;

    // ---- キー割り当て。設定(settings.json)の上書きを反映してディスパッチする ----

    /// <summary>(キー, 修飾) → アクション Id。設定から構築する。</summary>
    private readonly Dictionary<(Key, ModifierKeys), string> _keyToAction = new();

    /// <summary>
    /// ターミナルにフォーカスがある間でもファイラー側(WPF)で処理してよいアクション Id(これ以外は端末へ委ねる)。
    /// 表示切替・一覧へフォーカス戻し(terminal.focusBack)は terminal.html の JS が設定キーで横取りするためここには含めない。
    /// </summary>
    private static readonly HashSet<string> TerminalContextActions = new()
    {
        "terminal.collapse",
    };

    /// <summary>組み込みアクション Id → 実行処理(固定)。</summary>
    private readonly Dictionary<string, Action> _builtinActions;

    /// <summary>有効なアクション Id → 実行処理(組み込み + 外部ツール。設定変更で作り直す)。</summary>
    private Dictionary<string, Action> _actions = new();

    /// <summary>現在のキー割り当て表(コマンドパレットの一覧・ジェスチャ表示に使う)。</summary>
    private KeyBindingMap? _keyMap;

    private string _normalKeyHelp = "", _ctrlKeyHelp = "", _shiftKeyHelp = "";
    private string _memoKeyHelp = "", _terminalKeyHelp = "", _editorKeyHelp = "";

    /// <summary>メモ編集中(MemoBox フォーカス)に効くキー。view.toggleFullscreen は設定から動的に引く。</summary>
    private static readonly IReadOnlyList<KeyHelp.ContextHelpEntry> MemoHelpEntries = new[]
    {
        new KeyHelp.ContextHelpEntry(null, "Escape", "閉じる"),
        new KeyHelp.ContextHelpEntry("view.toggleFullscreen", null, "全画面切替"),
    };

    /// <summary>ターミナルフォーカス中に効くキー。いずれも設定マップから動的に引く
    /// (表示切替・一覧へフォーカス戻しは terminal.html 側 JS が同じ設定キーで横取りする)。</summary>
    private static readonly IReadOnlyList<KeyHelp.ContextHelpEntry> TerminalHelpEntries = new[]
    {
        new KeyHelp.ContextHelpEntry("terminal.focusBack", null, "一覧へ"),
        new KeyHelp.ContextHelpEntry("terminal.collapse", null, "たたむ"),
        new KeyHelp.ContextHelpEntry("view.toggleFullscreen", null, "表示切替"),
    };

    /// <summary>設定からキー割り当て表・ツール実行処理・フッターヘルプを作り直す(設定変更後にも呼ぶ)。</summary>
    private void RebuildKeyBindings()
    {
        var toolActions = Vm.Settings.Tools.Select(KeyBindingActions.ForTool).ToList();
        var map = KeyBindingMap.Build(Vm.Settings.KeyBindingOverrides, toolActions);
        _keyMap = map;

        // アクション実行表 = 組み込み + 各外部ツール起動。
        _actions = new Dictionary<string, Action>(_builtinActions);
        foreach (var tool in Vm.Settings.Tools)
        {
            var id = tool.Id;
            _actions[KeyBindingActions.ToolPrefix + id] = () => Run(() => Vm.LaunchTool(id));
        }

        // (キー,修飾) → アクション Id。
        _keyToAction.Clear();
        foreach (var action in map.Actions)
            foreach (var gesture in map.GesturesFor(action.Id))
                if (KeyChordWpf.TryToWpf(gesture, out var key, out var mods))
                    _keyToAction[(key, mods)] = action.Id;

        _normalKeyHelp = KeyHelp.BuildNormal(map);
        _ctrlKeyHelp = KeyHelp.BuildCtrl(map);
        _shiftKeyHelp = KeyHelp.BuildShift(map);
        _memoKeyHelp = KeyHelp.BuildContext(map, MemoHelpEntries);
        _terminalKeyHelp = KeyHelp.BuildContext(map, TerminalHelpEntries);
        UpdateKeyHelp();
    }

    /// <summary>フッターのキー操作説明を現在の状態に合わせて切り替える。
    /// テキストエディター(メモ)・ターミナルなどキー操作が変わる画面アクティブ時は、
    /// その状態で使えるキーの一覧を表示する。それ以外は修飾キー(Ctrl/Shift)に応じた一覧。</summary>
    private void UpdateKeyHelp()
    {
        if (EditorVisible && EditorBox.IsKeyboardFocusWithin)
        {
            KeyHelpText.Text = _editorKeyHelp;
            return;
        }
        if (MemoVisible && MemoBox.IsKeyboardFocusWithin)
        {
            KeyHelpText.Text = _memoKeyHelp;
            return;
        }
        if (TerminalVisible && _terminalFocused)
        {
            KeyHelpText.Text = _terminalKeyHelp;
            return;
        }
        KeyHelpText.Text =
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? _ctrlKeyHelp :
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? _shiftKeyHelp :
            _normalKeyHelp;
    }

    private Point _dragStart;
    private bool _dragArmed;

    /// <summary>列ヘッダーの余白(枠線・パディング・ソート▲の分)。</summary>
    private const double HeaderPadding = 24;
    /// <summary>データセルの左右パディング相当。</summary>
    private const double CellPadding = 14;

    /// <summary>
    /// 名前以外のカラム(アイコン/拡張子/サイズ/更新日時)を、ヘッダーと実データの最大幅に
    /// 合わせて明示的に設定する。GridView の Width="Auto" は仮想化リストでは行が未生成の
    /// 初回計測時にヘッダー幅で固定され、データ行に追従しないため、自前で実測してフィットさせる。
    /// 設定後、名前カラムへ残り幅を割り当てる。
    /// </summary>
    private void RefreshColumns(ListView list)
    {
        if (list.View is not GridView grid || grid.Columns.Count < 2) return;
        var dpi = VisualTreeHelper.GetDpi(list).PixelsPerDip;
        for (var i = 0; i < grid.Columns.Count; i++)
        {
            if (i == 1) continue;                         // 列1=名前は残り幅で別途設定
            var col = grid.Columns[i];

            // DisplayMemberBinding を持たない列(アイコン=16px画像)は固定幅。
            if (col.DisplayMemberBinding is not System.Windows.Data.Binding binding)
            {
                col.Width = 28;
                continue;
            }

            // ヘッダー幅(ソート▲のぶん余裕を持たせる)。
            var width = MeasureText(col.Header as string, list, dpi) + HeaderPadding;

            // データ最大幅。全行の FormattedText 実測は数万件で数百 ms かかるため、
            // 表示幅スコア上位の候補文字列だけを実測する。
            if (ColumnValueGetters.TryGetValue(binding.Path.Path, out var getter))
                foreach (var s in ColumnWidthCandidates.Select(EnumerateColumnValues(list, getter)))
                {
                    var w = MeasureText(s, list, dpi) + CellPadding;
                    if (w > width) width = w;
                }

            col.Width = width;
        }
        StretchNameColumn(list);
    }

    /// <summary>Auto 幅カラムのバインド先プロパティ名 → 値の取得関数(リフレクション回避)。</summary>
    private static readonly Dictionary<string, Func<EntryViewModel, string>> ColumnValueGetters = new()
    {
        [nameof(EntryViewModel.DisplayExtension)] = vm => vm.DisplayExtension,
        [nameof(EntryViewModel.DisplaySize)] = vm => vm.DisplaySize,
        [nameof(EntryViewModel.DisplayDate)] = vm => vm.DisplayDate,
    };

    private static IEnumerable<string> EnumerateColumnValues(ListView list, Func<EntryViewModel, string> getter)
    {
        foreach (var item in list.Items)
            if (item is EntryViewModel vm)
                yield return getter(vm);
    }

    /// <summary>MeasureText 用の Typeface キャッシュ(毎回 new すると DWrite ハンドルが大量に増えるため)。</summary>
    private static Typeface? _measureTypeface;

    /// <summary>指定文字列を ListView のフォントで描画したときの幅(DIP)。</summary>
    private static double MeasureText(string? text, ListView list, double dpi)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        _measureTypeface ??= new Typeface(list.FontFamily, list.FontStyle, list.FontWeight, list.FontStretch);
        var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, _measureTypeface, list.FontSize, Brushes.Black, dpi);
        return ft.Width;
    }

    private static void StretchNameColumn(ListView list)
    {
        if (list.View is not GridView grid || grid.Columns.Count < 2) return;
        var others = 0.0;
        for (var i = 0; i < grid.Columns.Count; i++)
            if (i != 1)                                          // 列1(名前)以外の幅合計
                others += double.IsNaN(grid.Columns[i].Width) ? grid.Columns[i].ActualWidth : grid.Columns[i].Width;
        var available = list.ActualWidth - others - SystemParameters.VerticalScrollBarWidth - 4;
        if (available > 80)
            grid.Columns[1].Width = available;
    }

    /// <summary>行の上で押下したらドラッグ開始候補とする(ヘッダー/スクロールバー上は除外)。</summary>
    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragArmed = IsOverItem(e.OriginalSource as DependencyObject);
        _dragStart = e.GetPosition(null);
    }

    /// <summary>しきい値を超えて移動したらアプリ外へのファイルドラッグ(コピー)を開始する。</summary>
    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragArmed = false;
        StartFileDrag((ListView)sender);
    }

    private void StartFileDrag(ListView list)
    {
        var pane = (string?)list.Tag == "Left" ? Vm.Left : Vm.Right;
        string[] files;
        try
        {
            files = BuildDragFiles(pane);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ドラッグの準備に失敗しました",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (files.Length == 0) return;

        var data = new DataObject(DataFormats.FileDrop, files);
        DragDrop.DoDragDrop(list, data, DragDropEffects.Copy);   // 移動ではなくコピー
    }

    /// <summary>ドラッグ対象(マーク群 or カーソル項目)の実パス一覧。書庫内項目は一時フォルダーへ抽出する。</summary>
    private static string[] BuildDragFiles(PaneViewModel pane)
    {
        var files = new List<string>();
        string? tempDir = null;
        foreach (var entry in pane.Targets)
        {
            if (ArchivePath.TrySplit(entry.FullPath, out _, out _))
            {
                tempDir ??= CreateDragTempDir();
                ArchiveExtractor.ExtractTo(entry.FullPath, tempDir);
                files.Add(Path.Combine(tempDir, entry.Name));
            }
            else
            {
                files.Add(entry.FullPath);
            }
        }
        return files.ToArray();
    }

    private static string CreateDragTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Filer", "drag_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>指定要素が ListViewItem の配下か(スクロールバーなどは除外)。</summary>
    private static bool IsOverItem(DependencyObject? source)
    {
        while (source is not null and not ListViewItem)
        {
            if (source is System.Windows.Controls.Primitives.ScrollBar) return false;
            source = VisualTreeHelper.GetParent(source);
        }
        return source is ListViewItem;
    }

    /// <summary>パンくずの区切りをクリックしたら、そのペインをアクティブ化してその階層へ移動する。</summary>
    private void Breadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: BreadcrumbSegment segment } button) return;

        var isLeft = (string?)button.Tag == "Left";
        ActivatePane(isLeft);
        Run(() => (isLeft ? Vm.Left : Vm.Right).NavigateTo(segment.Path));
        FocusActiveList();
    }

    /// <summary>+ ボタン: そのペインをアクティブ化し、現在フォルダで新しいタブを開く。</summary>
    private void AddTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        ActivatePane(tag == "Left");
        Run(Vm.Active.AddTab);
        FocusActiveList();
    }

    /// <summary>タブの × ボタン: 該当タブを閉じる(最後の1枚は閉じない)。</summary>
    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TabViewModel tab }) return;
        e.Handled = true;   // ListBoxItem の選択・親への伝播を抑止
        var pane = Vm.Left.Tabs.Contains(tab) ? Vm.Left : Vm.Right;
        ActivatePane(ReferenceEquals(pane, Vm.Left));
        Run(() => pane.CloseTab(tab));
        FocusActiveList();
    }

    /// <summary>タブ列をクリックしたら、そのペインをアクティブ化する。</summary>
    private void TabStrip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { Tag: string tag })
            ActivatePane(tag == "Left");
    }

    /// <summary>指定側ペインをアクティブにする(既にアクティブなら何もしない)。手動切替時はペイン配置を通常へ戻す。</summary>
    private void ActivatePane(bool isLeft)
    {
        if (Vm.IsLeftActive == isLeft) return;
        SetActivePaneFlags(isLeft);
        ResetPaneLayout();   // ペイン切替で全画面状態を解除し 50/50 に戻す
    }

    /// <summary>アクティブ側フラグだけを設定する(ペイン配置はそのまま。表示切替の全画面制御から使う)。</summary>
    private void SetActivePaneFlags(bool isLeft)
    {
        Vm.IsLeftActive = isLeft;
        Vm.Left.IsActive = isLeft;
        Vm.Right.IsActive = !isLeft;
    }

    /// <summary>ペインの ListView がフォーカスを得たらアクティブ側を切り替える。</summary>
    private void Pane_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ActivatePane((string?)((ListView)sender).Tag == "Left");
    }

    /// <summary>Ctrl / Shift の解放でフッターのキー操作説明を戻す。</summary>
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift) UpdateKeyHelp();
    }

    /// <summary>キー入力調査用ログの出力先(環境変数 FILER_KEYLOG にパス指定で有効)。</summary>
    private static readonly string? KeyLogPath = Environment.GetEnvironmentVariable("FILER_KEYLOG");

    /// <summary>フォルダー移動時間の調査用ログの出力先(環境変数 FILER_PERFLOG にパス指定で有効)。</summary>
    private static readonly string? PerfLogPath = Environment.GetEnvironmentVariable("FILER_PERFLOG");

    /// <summary>ここからレイアウト完了(Loaded)・描画後アイドル(ContextIdle)までの時間を記録する。</summary>
    private void LogRenderIdle(string label)
    {
        if (PerfLogPath is null) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
            File.AppendAllText(PerfLogPath, $"{DateTime.Now:HH:mm:ss.fff} {label} paint=+{sw.ElapsedMilliseconds}ms\n")));
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            File.AppendAllText(PerfLogPath, $"{DateTime.Now:HH:mm:ss.fff} {label} layout=+{sw.ElapsedMilliseconds}ms\n")));
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
            File.AppendAllText(PerfLogPath, $"{DateTime.Now:HH:mm:ss.fff} {label} renderIdle=+{sw.ElapsedMilliseconds}ms\n")));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (KeyLogPath is not null)
            File.AppendAllText(KeyLogPath,
                $"Key={e.Key} Sys={e.SystemKey} Ime={e.ImeProcessedKey} Mod={Keyboard.Modifiers} Src={e.OriginalSource?.GetType().Name}\n");
        // キー送信元でターミナルフォーカスを確定させる(WebView2 のフォーカスは
        // IsKeyboardFocusWithin に安定して出ないため。マウス操作後もこれで自己補正される)。
        _terminalFocused = e.OriginalSource is Microsoft.Web.WebView2.Wpf.WebView2;
        UpdateKeyHelp();   // Ctrl 押下開始でフッターを Ctrl 系へ切り替える

        // Alt 併用時は実キーが SystemKey、IME 処理時は ImeProcessedKey 側に入る。
        // 修飾キー単独はジェスチャにしない。
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        if (KeyChordWpf.IsModifier(key))
            return;

        var modifiers = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);

        // ターミナル(WebView2)にフォーカスがある間は、端末専用キー(表示をたたむ)だけ処理し、
        // それ以外のキー(矢印・Tab・ファンクション等。既定では端末へ届かずファイラーが奪っていた)は
        // 一切処理せず端末へ委ねる。表示切替・一覧へフォーカス戻しは terminal.html 側の JS が設定キーで処理する。
        if (e.OriginalSource is Microsoft.Web.WebView2.Wpf.WebView2)
        {
            if (_keyToAction.TryGetValue((key, modifiers), out var termId) &&
                TerminalContextActions.Contains(termId) &&
                _actions.TryGetValue(termId, out var termAction))
            {
                termAction();
                e.Handled = true;
            }
            return;
        }

        // テキスト編集中(EditorBox にフォーカス)は文字入力を優先し、Esc=閉じる・表示切替キー=全画面切替・
        // プレビューキー(逆ペインにプレビュー)だけ処理する。それ以外のキーは奪わずエディターへ委ねる。
        if (EditorVisible && EditorBox.IsKeyboardFocusWithin)
        {
            if (key == Key.Escape)
            {
                CloseEditor();
                e.Handled = true;
            }
            else if (_keyToAction.TryGetValue((key, modifiers), out var edId))
            {
                if (edId == "view.toggleFullscreen")
                {
                    CycleEditorView();
                    e.Handled = true;
                }
                else if (edId == "editor.preview" && FilePreview.HasRenderedPreview(_editorKind))
                {
                    PreviewFromEditor();
                    e.Handled = true;
                }
            }
            return;
        }

        // メモ入力中(MemoBox にフォーカス)は文字入力を優先し、Esc=閉じる・表示切替キー=全画面切替だけ処理する。
        // それ以外のキー(C/M/D 等のファイラー操作)は奪わずメモへ委ねる。
        // AvalonEdit の実フォーカスは内部 TextArea のため OriginalSource では判定せず IsKeyboardFocusWithin で見る。
        if (MemoVisible && MemoBox.IsKeyboardFocusWithin)
        {
            if (key == Key.Escape)
            {
                CloseMemo();
                e.Handled = true;
            }
            else if (_keyToAction.TryGetValue((key, modifiers), out var memoId) &&
                     memoId == "view.toggleFullscreen")
            {
                CycleMemoView();
                e.Handled = true;
            }
            return;
        }

        // グリッド(サムネイル)表示中の Escape は詳細表示へ戻す(Ctrl+G と同じ復帰)。
        if (key == Key.Escape && modifiers == ModifierKeys.None && ActiveIsGrid)
        {
            ToggleGridView();
            e.Handled = true;
            return;
        }

        Action? action = null;
        var resolved = _keyToAction.TryGetValue((key, modifiers), out var actionId) &&
            _actions.TryGetValue(actionId, out action);
        if (KeyLogPath is not null)
            File.AppendAllText(KeyLogPath, resolved
                ? $"  -> action {actionId}\n"
                : $"  -> no action (bindings={_keyToAction.Count})\n");
        if (resolved)
        {
            try
            {
                action!();
            }
            catch (Exception ex) when (KeyLogPath is not null)
            {
                File.AppendAllText(KeyLogPath, $"EXCEPTION in {actionId}: {ex}\n");
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// アクション Id → 実行処理の対応表。どのキーで起動するかは設定(RebuildKeyBindings)が決める。
    /// </summary>
    private Dictionary<string, Action> BuildActions() => new()
    {
        // カーソル移動はウィンドウレベルで明示処理し、モデルを直接動かす。
        // ListView のネイティブ矢印処理に依存しないため、フォーカス確定状況に
        // 関わらず最初の1キーから確実にカーソルが動く。
        // グリッド表示中は↑↓を行単位移動に切り替える(詳細表示は端でループする1行移動)。
        ["cursor.up"] = () => CursorVertical(GridDirection.Up, -1),
        ["cursor.down"] = () => CursorVertical(GridDirection.Down, 1),
        ["cursor.pageUp"] = () => { if (ActiveIsGrid) GridMovePage(-1); else { Vm.Active.MoveCursor(-PageStep()); ScrollActiveIntoView(); } },
        ["cursor.pageDown"] = () => { if (ActiveIsGrid) GridMovePage(1); else { Vm.Active.MoveCursor(PageStep()); ScrollActiveIntoView(); } },
        ["cursor.bottom"] = () => { Vm.Active.MoveToBottom(); ScrollActiveIntoView(); },
        ["mark.toggleAll"] = () => Vm.Active.ToggleMarkAll(),     // 全選択 ⇔ 全選択解除
        ["mark.toggle"] = () => Vm.Active.ToggleMarkAndAdvance(),

        // Tab: 相手側の領域へ切替。メモ/ターミナルが覆っていればそちらへ(裏ペインへは移らない)。
        ["pane.switchOrTerminal"] = () => FocusPaneSide(!Vm.IsLeftActive),
        // 修飾なしの ←→: ペインの外側方向(相手ペイン側)へはペイン移動、内側方向へは親フォルダーへ。
        // 左ペイン: ←=親 / →=右へ。右ペイン: →=親 / ←=左へ。
        // ペイン移動先がメモ/ターミナルに覆われていれば FocusPaneSide が裏ペインへ移らずそちらへ向ける。
        ["pane.left"] = () =>
        {
            if (ActiveIsGrid) { GridMoveCursor(GridDirection.Left); return; }   // グリッドは←で1つ前のタイルへ
            if (Vm.IsLeftActive) { Run(Vm.Active.GoToParent); FocusActiveList(); }
            else FocusPaneSide(left: true);
        },
        ["pane.right"] = () =>
        {
            if (ActiveIsGrid) { GridMoveCursor(GridDirection.Right); return; }   // グリッドは→で1つ次のタイルへ
            if (Vm.IsLeftActive) FocusPaneSide(left: false);
            else { Run(Vm.Active.GoToParent); FocusActiveList(); }
        },
        ["view.toggleFullscreen"] = () =>
        {
            // 表示の通常⇄全画面トグル。エディター→メモ→ターミナル→ファイルペインの順に対象を選ぶ。
            if (EditorVisible) CycleEditorView();
            else if (MemoVisible) CycleMemoView();
            else if (TerminalVisible) CycleTerminalView();
            else CyclePaneLayout();
        },
        ["view.toggleGrid"] = ToggleGridView,             // 詳細 ⇔ サムネイルグリッド表示
        ["view.gridSize"] = ToggleGridSize,               // グリッドのタイルサイズ 小 ⇔ 大 を切替
        ["view.reload"] = ReloadActivePreservingScroll,   // カーソル項目とスクロール位置を保持して再読込

        ["tab.new"] = () => { Run(Vm.Active.AddTab); FocusActiveList(); },
        ["tab.close"] = () => { Run(Vm.Active.CloseActiveTab); FocusActiveList(); },
        ["tab.prev"] = () => { Vm.Active.ActivatePrevTab(); FocusActiveList(); },
        ["tab.next"] = () => { Vm.Active.ActivateNextTab(); FocusActiveList(); },

        ["entry.activate"] = () => ActivateCurrent(Vm.Active),
        ["entry.openWith"] = () =>
        {
            if (Vm.Active.HasItems && !Vm.Active.Current.IsParent)
                Run(Vm.OpenSelectedWithAssociation);   // Windows の関連付けで開く
        },
        ["entry.openInOther"] = OpenCurrentInOtherPane,   // Ctrl+Enter: 反対ペインでフォルダー・書庫を開く
        ["nav.parent"] = () => { Run(Vm.Active.GoToParent); FocusActiveList(); },
        ["nav.sameAsOther"] = () =>
        {
            Run(() => Vm.NavigateActiveTo(Vm.Inactive.DirectoryPath));  // 相手ペインと同じパスへ
            FocusActiveList();
        },

        ["file.copy"] = () => RunTransfer(FileTransferKind.Copy),   // コピーは確認なしで実行(非同期+進捗)
        ["file.move"] = () => ConfirmOrRun(Vm.Settings.ConfirmMove,
            $"{Describe(Vm.Active)} を\n{Vm.Inactive.DirectoryPath}\nへ移動しますか?",
            () => RunTransfer(FileTransferKind.Move)),
        ["file.delete"] = () => ConfirmOrRun(Vm.Settings.ConfirmRecycle,
            $"{Describe(Vm.Active)} をごみ箱へ送りますか?", () => RunDelete(DeleteKind.Recycle)),
        ["file.deletePermanent"] = () => ConfirmOrRun(Vm.Settings.ConfirmPermanentDelete,
            $"{Describe(Vm.Active)} を完全に削除しますか?\nごみ箱には入らず、元に戻せません。",
            () => RunDelete(DeleteKind.Permanent)),
        ["file.rename"] = () => RenameInteractive(Vm.Active),
        ["file.bulkRename"] = () => BulkRenameInteractive(Vm.Active),
        ["folder.create"] = CreateFolderInteractive,
        ["archive.zip"] = CompressInteractive,           // X: 選択項目を ZIP 圧縮
        ["path.copy"] = CopyPathToClipboard,             // カーソル項目のフルパスをコピー
        ["file.diff"] = ShowDiff,                        // 2ファイルの差分を side-by-side 表示
        ["folder.compare"] = ShowFolderCompare,          // 左右フォルダーを再帰比較してツリー表示

        ["sort.select"] = ShowSortDialog,                // ソート方法・昇降順の選択
        ["search.incremental"] = ShowIncrementalSearch,  // 名前のインクリメンタルサーチ
        ["search.file"] = ShowFileSearchDialog,          // サブフォルダーを含むファイル検索
        ["filter.show"] = ShowFilter,                    // 表示の絞り込み(*.jpg だけ表示 等)
        ["filter.clear"] = ClearFilter,                  // Esc: 確定済みフィルターを解除
        ["drive.select"] = ShowDriveSelector,
        ["favorite.add"] = AddFavoriteInteractive,
        ["favorite.select"] = ShowFavoriteSelector,
        ["history.select"] = ShowHistorySelector,        // 開いたフォルダーの履歴から移動

        ["entry.edit"] = OpenEditor,                     // I: カーソル位置のテキストファイルをエディターで開く
        ["memo.toggle"] = ToggleMemo,                    // メモ(反対ペイン)の表示/非表示
        ["terminal.open"] = OpenOrFocusTerminal,         // ターミナルを開く/フォーカス
        ["terminal.pick"] = OpenTerminalWithPicker,      // 種類を選んでターミナル
        ["terminal.focusBack"] = FocusActiveList,        // ターミナル中: 一覧へフォーカスを戻す
        ["terminal.collapse"] = CollapseTerminal,        // ターミナル中: 表示をたたむ(セッション保持)

        ["settings.open"] = ShowSettingsDialog,
        ["palette.show"] = ShowCommandPalette,            // すべてのコマンドを検索して実行
    };

    /// <summary>
    /// 指定側の領域へフォーカスを移す(Tab・←/→ のペイン切替で共用)。
    /// メモ/ターミナルがその側を覆っている(片側表示でその側、または全画面表示)ときは、
    /// 裏のファイルペインへ移らずオーバーレイへフォーカスする(見えていない裏ペインを操作対象にしない)。
    /// メモから一覧へ戻すのは Ctrl+Tab / Esc。タブ切替は Ctrl+←/→。
    /// </summary>
    private void FocusPaneSide(bool left)
    {
        // F2 で片側を全画面化中は、隠れている反対ペインへは移らずアンカー側に留める。
        if (_paneStep == 1 && left != _paneAnchorLeft)
        {
            SetActivePaneFlags(_paneAnchorLeft);
            FocusActiveList();
            return;
        }
        if (EditorVisible && (_editorView == EditorView.FullScreen || _editorOnLeft == left))
        {
            EditorBox.Focus();
            return;
        }
        if (MemoVisible && (_memoView == MemoView.FullScreen || _memoOnLeft == left))
        {
            MemoBox.Focus();
            return;
        }
        if (TerminalVisible && (_terminalView == TerminalView.FullScreen || _terminalOnLeft == left))
        {
            FocusTerminalPanel();
            return;
        }
        SetActivePaneFlags(left);
        FocusActiveList();
    }

    /// <summary>Z: 設定ダイアログ(キー割り当て・外部ツール)を開き、OK なら保存して即時反映する。</summary>
    private void ShowSettingsDialog()
    {
        var dialog = new SettingsDialog(Vm.Settings) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } result)
        {
            Run(() => Vm.UpdateSettings(result));
            RebuildKeyBindings();
            ThemeManager.Apply(result.Theme);   // 外観テーマを即時反映(System は再解決)
            PaneListView.LightweightAutomation = result.LightweightListAutomation;   // UIA軽量化を即時反映
        }
        FocusActiveList();
    }

    /// <summary>
    /// Enter / ダブルクリック共通の決定操作。ディレクトリ・書庫(.zip)は中へ移動、
    /// ファイルは画像/テキスト/Markdown をアプリ内プレビューする。
    /// </summary>
    private void ActivateCurrent(PaneViewModel active)
    {
        if (!active.HasItems) return;   // フィルターで全件隠れている等、対象なし
        if (active.Current.IsDirectory || active.Current.IsArchive)
        {
            Run(active.Open);      // ディレクトリ・書庫(.zip)へ移動。開けなくても落とさず通知
            FocusActiveList();     // 移動後も選択項目へフォーカスを戻す
            LogRenderIdle("afterOpen");
        }
        else
        {
            PreviewCurrent(active);
        }
    }

    /// <summary>Ctrl+Enter: カーソル位置のフォルダー・書庫を反対ペインで開く(アクティブ側はそのまま)。</summary>
    private void OpenCurrentInOtherPane()
    {
        if (!Vm.Active.HasItems) return;
        var current = Vm.Active.Current;
        if (current.IsParent || (!current.IsDirectory && !current.IsArchive)) return;   // フォルダー・書庫のみ対象
        Run(() => Vm.Inactive.NavigateTo(current.FullPath));
        FocusActiveList();
    }

    /// <summary>行のダブルクリックを Enter と同じ決定操作にする(対象ペインをアクティブ化)。</summary>
    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // ヘッダー・スクロールバー・余白上は無視し、行の上だけ反応する。
        if (!IsOverItem(e.OriginalSource as DependencyObject)) return;

        var isLeft = (string?)((ListView)sender).Tag == "Left";
        ActivatePane(isLeft);
        ActivateCurrent(isLeft ? Vm.Left : Vm.Right);
        e.Handled = true;
    }

    /// <summary>Enter: カーソル位置のファイルを画像/テキストとしてアプリ内プレビューする。対応外は何もしない。</summary>
    private void PreviewCurrent(PaneViewModel active)
    {
        var path = active.SelectedItemPath;
        // 実ファイルまたは書庫内ファイルのみ対象。
        if (!System.IO.File.Exists(path) && !Filer.Core.ArchivePath.TrySplit(path, out _, out _)) return;
        var kind = Filer.Core.FilePreview.ClassifyByExtension(path);
        if (kind == Filer.Core.PreviewKind.None) return;
        Run(() =>
        {
            // ペイン領域表示は反対側ペインへ重ねる(自身の一覧を見ながらプレビューするため)
            var paneRegion = Vm.IsLeftActive ? RightPane : LeftPane;
            var window = new PreviewWindow(Vm, paneRegion, KeyMap()) { Owner = this };
            window.ShowDialog();
        });
        FocusActiveList();   // プレビュー内でカーソル移動した場合も選択行へ復帰
    }

    /// <summary>
    /// Shift+C: 2ファイルの差分を side-by-side で表示する。対象は「アクティブで2件マーク」か
    /// 「左右ペインのカーソル項目」。解決できなければ理由をダイアログで通知する。
    /// </summary>
    private void ShowDiff()
    {
        var resolution = Vm.ResolveDiffTargets();
        if (resolution.Targets is not { } targets)
        {
            MessageBox.Show(this, resolution.Error, "差分", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Run(() =>
        {
            // ペイン領域表示は反対側ペインへ重ねる(自分の一覧を見ながら差分を見るため)。
            var paneRegion = Vm.IsLeftActive ? RightPane : LeftPane;
            var window = new DiffWindow(targets.LeftPath, targets.RightPath, paneRegion, KeyMap()) { Owner = this };
            window.ShowDialog();
        });
        FocusActiveList();
    }

    /// <summary>
    /// Ctrl+Shift+C: 左右ペインのフォルダーを再帰比較してツリー表示する。
    /// 比較前にオプションダイアログ(前回値が既定)を出し、対象が解決できなければ理由を通知する。
    /// </summary>
    private void ShowFolderCompare()
    {
        var resolution = Vm.ResolveFolderCompareTargets();
        if (resolution.Targets is not { } targets)
        {
            MessageBox.Show(this, resolution.Error, "フォルダー比較", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new FolderCompareOptionsDialog(targets.LeftPath, targets.RightPath, _folderComparePrefs.Load())
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true) return;
        _folderComparePrefs.Save(dialog.Options);

        Run(() =>
        {
            var paneRegion = Vm.IsLeftActive ? RightPane : LeftPane;
            var window = new FolderCompareWindow(
                targets.LeftPath, targets.RightPath, dialog.Options, paneRegion, KeyMap(), Vm) { Owner = this };
            window.ShowDialog();
        });
        FocusActiveList();
    }

    /// <summary>L: ドライブ選択 UI を表示し、選んだドライブのルートへアクティブ側を移動する。</summary>
    private void ShowDriveSelector()
    {
        var entries = Vm.GetDrives()
            .Select(d => new SelectionEntry(DescribeDrive(d), d.RootPath))
            .ToList();
        PickAndNavigate("ドライブ選択", "移動先のドライブを選択(ドライブ文字キーで即移動):", entries, letterSelect: true);
    }

    /// <summary>Q: アクティブ側カーソル項目のフルパスをクリップボードへコピーする(".." は現在フォルダー)。</summary>
    private void CopyPathToClipboard()
    {
        var path = Vm.Active.SelectedItemPath;
        Run(() => Clipboard.SetText(path));
    }

    /// <summary>各列ヘッダーの基底テキスト(添字は GridView の列番号。0=アイコン列は対象外)。</summary>
    private static readonly string[] HeaderBaseTexts = { "", "名前", "拡張子", "サイズ", "更新日時" };

    /// <summary>GridView の列番号を対応するソートキーへ。アイコン列など対象外は null。</summary>
    private static SortKey? ColumnSortKey(int index) => index switch
    {
        1 => SortKey.Name,
        2 => SortKey.Extension,
        3 => SortKey.Size,
        4 => SortKey.Date,
        _ => null,
    };

    /// <summary>
    /// 一覧ヘッダーのクリック: その列でソートする。
    /// 同じ列を再クリックしたら昇順⇔降順をトグル、別の列なら昇順から開始する。
    /// </summary>
    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column }) return;
        var list = (ListView)sender;
        if (list.View is not GridView grid) return;
        if (ColumnSortKey(grid.Columns.IndexOf(column)) is not { } key) return;

        var isLeft = (string?)list.Tag == "Left";
        ActivatePane(isLeft);
        var pane = isLeft ? Vm.Left : Vm.Right;
        var descending = pane.SortKey == key && !pane.SortDescending;
        Run(() => pane.SetSort(key, descending));
        FocusActiveList();
    }

    /// <summary>両ペインのヘッダーに現在のソート列・昇降順を ▲▼ で反映する。</summary>
    private void UpdateSortIndicators()
    {
        ApplySortIndicators(LeftList, Vm.Left);
        ApplySortIndicators(RightList, Vm.Right);
    }

    private static void ApplySortIndicators(ListView list, PaneViewModel pane)
    {
        if (list.View is not GridView grid) return;
        for (var i = 1; i < grid.Columns.Count && i < HeaderBaseTexts.Length; i++)
        {
            var text = HeaderBaseTexts[i];
            if (ColumnSortKey(i) == pane.SortKey)
                text += pane.SortDescending ? " ▼" : " ▲";
            grid.Columns[i].Header = text;
        }
    }

    /// <summary>
    /// E: インクリメンタルサーチ。入力のたびにアクティブ側のカーソルを一致項目へ動かす。
    /// 起点はダイアログを開いた時のカーソル位置。Enter・閉じる=確定、Esc・×=元の位置へ戻す。
    /// </summary>
    private void ShowIncrementalSearch()
    {
        var pane = Vm.Active;
        var anchor = pane.SelectedIndex;

        var dialog = new IncrementalSearchDialog(
            search: (query, prefixOnly) =>
            {
                // クエリが空に戻ったら起点位置へ戻す(「一致なし」にはしない)。
                if (string.IsNullOrWhiteSpace(query))
                    return MoveToMatch(pane, anchor);
                return MoveToMatch(pane,
                    IncrementalSearch.FindFrom(SnapshotEntries(pane), query, prefixOnly, anchor));
            },
            searchNext: (query, prefixOnly, backward) => MoveToMatch(pane,
                IncrementalSearch.FindNext(SnapshotEntries(pane), query, prefixOnly, pane.SelectedIndex, backward)))
        { Owner = this };

        if (dialog.ShowDialog() != true)
            MoveToMatch(pane, anchor);   // キャンセルは検索開始時の位置へ戻す
        FocusActiveList();
    }

    /// <summary>
    /// Shift+F: フィルター表示。入力のたびにアクティブ側を絞り込む(例 "*.jpg" だけ表示)。
    /// Enter・適用=確定、Esc・×=開いた時点のフィルターへ戻す。空欄で解除。
    /// フォルダー移動するとフィルターは自動で解除される。
    /// </summary>
    private void ShowFilter()
    {
        var pane = Vm.Active;
        var original = pane.Filter;   // 開いた時点のフィルター(キャンセルで戻す)
        var initialCount = pane.Entries.Count(e => !e.IsParent);   // 現在の表示件数(".." を除く)

        pane.FilterEditing = true;    // 開いた瞬間からパンくず横に「絞り込み:」を出す
        var dialog = new FilterDialog(
            initial: original,
            initialCount: initialCount,
            apply: pattern =>
            {
                pane.SetFilter(pattern);
                ScrollActiveIntoView();
                return pane.Entries.Count(e => !e.IsParent);   // 一致件数(".." を除く)
            })
        { Owner = this };

        if (dialog.ShowDialog() != true)
            pane.SetFilter(original);   // キャンセルは開いた時点のフィルターへ戻す
        pane.FilterEditing = false;
        FocusActiveList();
    }

    /// <summary>Esc: アクティブ側に絞り込みが掛かっていれば解除する(無ければ何もしない)。</summary>
    private void ClearFilter()
    {
        if (!Vm.Active.HasFilter) return;
        Run(() => Vm.Active.SetFilter(string.Empty));
        FocusActiveList();
    }

    /// <summary>
    /// F: ファイル検索(だいなファイラー風)。基準ディレクトリ配下を再帰検索し、
    /// 「転送して閉じる」=結果をアクティブペインへ仮想一覧として表示、
    /// 「ジャンプ」=選択項目のあるフォルダーへ移動してカーソルを合わせる。
    /// </summary>
    /// <summary>非管理者起動時のみ生成する高速検索(昇格ヘルパー)プロキシ。管理者起動なら null。</summary>
    private ElevatedSearchProxy? _searchProxy;

    /// <summary>
    /// 高速検索プロキシを取得する(非管理者起動かつ設定で有効なときのみ)。常駐ヘルパーを温存するため使い回す。
    /// 管理者起動、または設定 OFF のときは null(高速検索ボタンを出さない)。設定で OFF にされたら
    /// 常駐ヘルパーを片付ける。
    /// </summary>
    private ElevatedSearchProxy? GetOrCreateSearchProxy()
    {
        if (IsRunningAsAdministrator() || !Vm.Settings.EnableElevatedFastSearch)
        {
            _searchProxy?.Dispose();   // 設定 OFF で常駐ヘルパーを終了させる
            _searchProxy = null;
            return null;
        }
        _searchProxy ??= new ElevatedSearchProxy();
        return _searchProxy;
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private void ShowFileSearchDialog()
    {
        var dialog = new FileSearchDialog(SearchBaseDirectory(), GetOrCreateSearchProxy()) { Owner = this };
        dialog.ShowDialog();

        if (dialog.JumpTarget is { } target)
        {
            Run(() =>
            {
                var dir = Path.GetDirectoryName(target.FullPath);
                if (string.IsNullOrEmpty(dir)) return;
                Vm.NavigateActiveTo(dir);   // 書庫内の仮想パスも ArchiveAwareReader が解決する
                MoveToMatch(Vm.Active, IndexOfPath(Vm.Active, target.FullPath));
            });
        }
        else if (dialog.TransferRequested)
        {
            Run(() =>
            {
                var pattern = dialog.PatternText.Length > 0 ? dialog.PatternText : "*";
                var path = Vm.RegisterSearchResults(
                    $"検索結果: {pattern}", dialog.SearchedBaseDirectory, dialog.Results);
                Vm.NavigateActiveTo(path);
            });
        }
        FocusActiveList();
    }

    /// <summary>
    /// ファイル検索の初期基準ディレクトリ。現在フォルダーが実在すればそれ、
    /// 書庫内なら書庫のあるフォルダー、検索結果の仮想一覧なら ".." の戻り先を使う。
    /// </summary>
    private string SearchBaseDirectory()
    {
        var dir = Vm.Active.DirectoryPath;
        if (Directory.Exists(dir)) return dir;
        if (ArchivePath.TrySplit(dir, out var zip, out _) && Path.GetDirectoryName(zip) is { } zipDir)
            return zipDir;
        if (Vm.Active.Entries.FirstOrDefault()?.Entry is { IsParent: true } parent &&
            Directory.Exists(parent.FullPath))
            return parent.FullPath;
        return dir;
    }

    /// <summary>ペイン内でフルパスが一致する行のインデックス(なければ -1)。</summary>
    private static int IndexOfPath(PaneViewModel pane, string fullPath)
    {
        for (var i = 0; i < pane.Entries.Count; i++)
            if (string.Equals(pane.Entries[i].Entry.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>検索用にアクティブペインの表示中エントリ一覧を取り出す。</summary>
    private static IReadOnlyList<FileEntry> SnapshotEntries(PaneViewModel pane) =>
        pane.Entries.Select(vm => vm.Entry).ToList();

    /// <summary>一致位置へカーソルを動かして表示範囲へスクロールする。一致なし(-1)は動かさない。</summary>
    private bool MoveToMatch(PaneViewModel pane, int index)
    {
        if (index < 0) return false;
        pane.MoveCursorTo(index);
        ScrollActiveIntoView();
        return true;
    }

    /// <summary>S: ソート方法・昇降順を選び、アクティブ側に適用する。</summary>
    /// <summary>
    /// コマンドパレットを開く。現在のキー割り当てから実行可能な全コマンド(組み込み + 外部ツール)を
    /// 一覧化し、選ばれたアクションをそのまま実行する。
    /// </summary>
    private void ShowCommandPalette()
    {
        if (_keyMap is null) return;

        var items = new List<CommandPaletteItem>();
        foreach (var action in _keyMap.Actions)
        {
            if (action.Id == "palette.show") continue;       // パレット自身は出さない
            if (!_actions.ContainsKey(action.Id)) continue;  // 実行処理が無いものは除外
            items.Add(new CommandPaletteItem(
                action.Id, action.DisplayName, action.Category, GestureDisplay(action.Id)));
        }

        var dialog = new CommandPaletteDialog(items) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedId is { } id &&
            _actions.TryGetValue(id, out var run))
        {
            run();   // 選ばれたコマンドを実行(各処理が必要に応じて FocusActiveList する)
        }
        else
        {
            FocusActiveList();
        }
    }

    /// <summary>現在の設定キー割り当て(未構築なら構築する)。プレビュー/差分/ターミナルへ渡す。</summary>
    private KeyBindingMap KeyMap()
    {
        if (_keyMap is null) RebuildKeyBindings();
        return _keyMap!;
    }

    /// <summary>アクションに割り当てられたジェスチャを表示用文字列にする(複数は空白区切り。無ければ空)。</summary>
    private string GestureDisplay(string actionId)
    {
        if (_keyMap is null) return "";
        var parts = new List<string>();
        foreach (var gesture in _keyMap.GesturesFor(actionId))
            if (KeyChord.TryParse(gesture, out var chord))
                parts.Add(chord.DisplayText);
        return string.Join(" ", parts);
    }

    private void ShowSortDialog()
    {
        var pane = Vm.Active;
        var dialog = new SortDialog(pane.SortKey, pane.SortDescending) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            Run(() => pane.SetSort(dialog.SelectedKey, dialog.Descending));
            FocusActiveList();
        }
    }

    // ---- 組み込みターミナル(T / Shift+T / 表示切替キー)。ペイン領域にオーバーレイ表示する ----

    /// <summary>ターミナルの表示形態。</summary>
    private enum TerminalView { OnePane, FullScreen }

    private TerminalPanel? _terminalPanel;
    private TerminalView _terminalView = TerminalView.OnePane;
    /// <summary>1画面表示時にどちら側の列を占有するか(初回オープン時に非アクティブ側へ決定)。</summary>
    private bool _terminalOnLeft;

    /// <summary>ターミナルがオープン中(タブが1つ以上ある)。たたんで非表示中でもセッションが生きていれば true。</summary>
    private bool TerminalOpen => _terminalPanel is { TabCount: > 0 };

    /// <summary>ターミナルが画面に表示されている(オープン中かつオーバーレイが可視)か。</summary>
    private bool TerminalVisible => TerminalOpen && TerminalHost.Visibility == Visibility.Visible;

    /// <summary>前回開いたターミナル種別を記憶し、次回の既定にする(永続化)。</summary>
    private readonly TerminalPreferenceStore _terminalPrefs = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Filer", "terminal-prefs.json"));

    /// <summary>前回のフォルダー比較オプションを記憶し、次回の既定にする(永続化)。</summary>
    private readonly FolderComparePreferenceStore _folderComparePrefs = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Filer", "folder-compare-prefs.json"));

    /// <summary>
    /// 新しいタブの作業フォルダー。カーソルがサブフォルダー上ならそのフォルダー、
    /// ファイルや ".." 上なら現在のディレクトリ(書庫内なら書庫のあるフォルダーへフォールバック)。
    /// </summary>
    private string TerminalCwd()
    {
        var path = Vm.Active.TargetFolderPath;
        return ArchivePath.TrySplit(path, out var archivePath, out _)
            ? Path.GetDirectoryName(archivePath)!
            : path;
    }

    /// <summary>既定のシェル種別(前回開いた種別。無ければ検出順の先頭)。シェルが無ければ null。</summary>
    private TerminalProfile? DefaultTerminalProfile(IReadOnlyList<TerminalProfile> profiles)
    {
        if (profiles.Count == 0) return null;
        var last = _terminalPrefs.LoadLastProfileName();
        return profiles.FirstOrDefault(p => p.Name == last) ?? profiles[0];
    }

    /// <summary>T: ターミナルを開く/フォーカスする。たたまれていれば再表示し、なければ前回の種別で起動する。</summary>
    private void OpenOrFocusTerminal()
    {
        if (TerminalOpen)
        {
            if (!TerminalVisible) ApplyTerminalView();   // F4 でたたまれていたら再表示(セッションは生存)
            FocusTerminalPanel();
            return;
        }
        var profile = DefaultTerminalProfile(TerminalProfiles.Detect());
        if (profile is null)
        {
            MessageBox.Show(this, "利用できるシェルが見つかりません。", "ターミナル",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        OpenTerminalTab(profile);
    }

    /// <summary>Shift+T: ターミナルの種類を選んで新しいタブを開く(閉じていれば開く)。</summary>
    private void OpenTerminalWithPicker()
    {
        var profiles = TerminalProfiles.Detect();
        if (profiles.Count == 0) return;
        var entries = profiles.Select((p, i) => new SelectionEntry(p.Name, i.ToString())).ToList();
        var dialog = new SelectionDialog("ターミナルの種類", "開くターミナルの種類を選択:",
            entries, numbered: true) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedValue is null) return;

        OpenTerminalTab(profiles[int.Parse(dialog.SelectedValue)]);
    }

    /// <summary>指定シェルでターミナルタブを開く。初回は1画面表示にして配置側を決める。</summary>
    private void OpenTerminalTab(TerminalProfile profile)
    {
        ResetPaneLayout();   // ペイン全画面中だと列幅0でオーバーレイが潰れるため通常へ戻す
        if (EditorVisible) CloseEditor();   // 同じ領域のオーバーレイ重なりを避ける(編集内容は保存)
        EnsureTerminalPanel();
        if (!TerminalOpen)
        {
            _terminalOnLeft = !Vm.IsLeftActive;          // 初回は非アクティブ側に置く
            _terminalView = TerminalView.OnePane;
        }
        _terminalPanel!.OpenNewTab(profile, TerminalCwd());
        ApplyTerminalView();                              // タブが増えたので表示を反映
        _terminalFocused = true;                          // 新タブは ready 後に端末へフォーカスする
        UpdateKeyHelp();
    }

    /// <summary>表示切替キー: ターミナル表示を 1画面 ⇄ 全画面 でトグルする。</summary>
    private void CycleTerminalView()
    {
        if (!TerminalOpen) return;   // ターミナル未オープン時は無視
        _terminalView = _terminalView == TerminalView.FullScreen
            ? TerminalView.OnePane
            : TerminalView.FullScreen;
        ApplyTerminalView();
        FocusTerminalPanel();
    }

    /// <summary>F4: ターミナル表示をたたむ(セッションは終了せず保持。T で再表示できる)。一覧へフォーカスを戻す。</summary>
    private void CollapseTerminal()
    {
        if (!TerminalOpen) return;
        TerminalHost.Visibility = Visibility.Collapsed;
        FocusActiveList();
    }

    /// <summary>ターミナルパネルを(未生成なら)作り、オーバーレイのホストへ一度だけ載せる。</summary>
    private void EnsureTerminalPanel()
    {
        if (_terminalPanel is not null) return;
        _terminalPanel = new TerminalPanel(TerminalCwd,
            () => DefaultTerminalProfile(TerminalProfiles.Detect()), KeyMap());
        _terminalPanel.AllTabsClosed += OnTerminalAllClosed;
        _terminalPanel.FocusListRequested += FocusActiveList;
        _terminalPanel.CycleViewRequested += CycleTerminalView;
        _terminalPanel.ProfileOpened += p => _terminalPrefs.SaveLastProfileName(p.Name);
        TerminalHost.Child = _terminalPanel;
    }

    /// <summary>現在の表示形態をオーバーレイの列スパン・可視性へ反映する。</summary>
    private void ApplyTerminalView()
    {
        switch (_terminalView)
        {
            case TerminalView.OnePane:
                Grid.SetColumn(TerminalHost, _terminalOnLeft ? 0 : 2);
                Grid.SetColumnSpan(TerminalHost, 1);
                TerminalHost.Visibility = Visibility.Visible;
                break;
            case TerminalView.FullScreen:
                Grid.SetColumn(TerminalHost, 0);
                Grid.SetColumnSpan(TerminalHost, 3);   // 両ペイン＋スプリッターを覆う
                TerminalHost.Visibility = Visibility.Visible;
                break;
        }
    }

    /// <summary>全タブが閉じられたらオーバーレイを畳み、一覧へフォーカスを戻す(次回は1画面から)。</summary>
    private void OnTerminalAllClosed()
    {
        _terminalView = TerminalView.OnePane;
        TerminalHost.Visibility = Visibility.Collapsed;
        FocusActiveList();
    }

    // ---- メモ(U)。反対ペイン領域にオーバーレイ表示する。表示切替キーで1画面⇄全画面、Esc で閉じる ----

    /// <summary>メモの表示形態。</summary>
    private enum MemoView { OnePane, FullScreen }

    /// <summary>メモ本文の永続化。入力は随時保存し、次回起動時に復元する。</summary>
    private readonly MemoStore _memoStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Filer", "memo.txt"));

    private MemoView _memoView = MemoView.OnePane;
    /// <summary>1画面表示時にどちら側の列を占有するか(開いたとき非アクティブ側へ決定)。</summary>
    private bool _memoOnLeft;
    /// <summary>保存済みメモを TextBox へ読み込み済みか(初回オープン時のみ復元する)。</summary>
    private bool _memoLoaded;
    /// <summary>入力のたびに即書き込まず、少し待ってまとめて保存するためのデバウンスタイマー。</summary>
    private DispatcherTimer? _memoSaveTimer;

    /// <summary>メモが画面に表示されているか。</summary>
    private bool MemoVisible => MemoHost.Visibility == Visibility.Visible;

    /// <summary>U: メモの表示/非表示をトグルする。表示時は反対ペイン領域へ出してフォーカスする。</summary>
    private void ToggleMemo()
    {
        if (MemoVisible) { CloseMemo(); return; }

        ResetPaneLayout();                 // ペイン全画面中だと列幅0でオーバーレイが潰れるため通常へ戻す
        if (EditorVisible) CloseEditor();          // 同じ領域のオーバーレイ重なりを避ける(編集内容は保存)
        if (TerminalVisible) CollapseTerminal();   // 同じ領域のオーバーレイ重なりを避ける(セッションは保持)
        if (!_memoLoaded)
        {
            MemoBox.Text = _memoStore.Load();
            _memoLoaded = true;            // 初回ロード分の TextChanged を保存対象から外すため Text 設定後に立てる
        }
        _memoOnLeft = !Vm.IsLeftActive;    // 非アクティブ側に置く
        _memoView = MemoView.OnePane;
        ApplyMemoHighlighting();           // 現在のテーマ配色で Markdown ハイライトを設定
        UpdateMemoHeader();                // 全画面切替キーは設定で変わるので実際の割当を表示
        ApplyMemoView();
        MemoBox.Focus();
        MemoBox.CaretOffset = MemoBox.Text.Length;
    }

    /// <summary>現在のテーマ(背景輝度でライト/ダーク判定)に合わせて Markdown ハイライトを適用する。</summary>
    private void ApplyMemoHighlighting()
    {
        var isDark = ThemeManager.CurrentMarkdownColors().IsDark;
        MemoBox.SyntaxHighlighting = MarkdownHighlighting.ForTheme(isDark);
    }

    /// <summary>Esc / 再度の U: メモを閉じる(内容を保存し、一覧へフォーカスを戻す)。</summary>
    private void CloseMemo()
    {
        if (!MemoVisible) return;
        FlushMemo();
        MemoHost.Visibility = Visibility.Collapsed;
        _memoView = MemoView.OnePane;
        FocusActiveList();
    }

    /// <summary>表示切替キー: メモ表示を 1画面 ⇄ 全画面 でトグルする。</summary>
    private void CycleMemoView()
    {
        if (!MemoVisible) return;
        _memoView = _memoView == MemoView.FullScreen ? MemoView.OnePane : MemoView.FullScreen;
        ApplyMemoView();
        MemoBox.Focus();
    }

    /// <summary>ヘッダーに閉じる(Esc)と全画面切替の実際の割り当てキーを表示する。</summary>
    private void UpdateMemoHeader()
    {
        var full = GestureDisplay("view.toggleFullscreen");
        MemoHeaderText.Text = string.IsNullOrEmpty(full)
            ? "メモ  (Esc:閉じる)"
            : $"メモ  (Esc:閉じる  {full}:全画面)";
    }

    /// <summary>現在の表示形態をオーバーレイの列スパン・可視性へ反映する。</summary>
    private void ApplyMemoView()
    {
        switch (_memoView)
        {
            case MemoView.OnePane:
                Grid.SetColumn(MemoHost, _memoOnLeft ? 0 : 2);
                Grid.SetColumnSpan(MemoHost, 1);
                break;
            case MemoView.FullScreen:
                Grid.SetColumn(MemoHost, 0);
                Grid.SetColumnSpan(MemoHost, 3);   // 両ペイン＋スプリッターを覆う
                break;
        }
        MemoHost.Visibility = Visibility.Visible;
    }

    /// <summary>入力のたびに保存をデバウンス予約する(初回ロード中の変更は無視)。</summary>
    private void MemoBox_TextChanged(object? sender, EventArgs e)
    {
        if (!_memoLoaded) return;
        _memoSaveTimer ??= CreateMemoSaveTimer();
        _memoSaveTimer.Stop();
        _memoSaveTimer.Start();
    }

    private DispatcherTimer CreateMemoSaveTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => FlushMemo();
        return timer;
    }

    /// <summary>保留中の編集をメモファイルへ書き出す(閉じる・全画面切替・終了時にも呼ぶ)。</summary>
    private void FlushMemo()
    {
        if (!_memoLoaded) return;          // 一度も開いていなければ書かない
        _memoSaveTimer?.Stop();
        _memoStore.Save(MemoBox.Text);
    }

    // ---- テキストエディター(I)。アクティブペイン領域にファイルを重ねて編集する ----
    // 文字コード(BOM 含む)は開いたときのものを維持して自動保存する。編集中の指定キーで逆ペインにプレビューできる。

    private enum EditorView { OnePane, FullScreen }

    private EditorView _editorView = EditorView.OnePane;
    /// <summary>編集中ファイルのフルパス(未オープン時は null)。</summary>
    private string? _editorPath;
    /// <summary>開いたときの文字コードと BOM の有無(保存時に維持する)。</summary>
    private System.Text.Encoding _editorEncoding = System.Text.Encoding.UTF8;
    private bool _editorHasBom;
    /// <summary>編集中ファイルのプレビュー種別(逆ペインプレビュー可否の判定に使う)。</summary>
    private PreviewKind _editorKind = PreviewKind.None;
    /// <summary>1画面表示時にどちら側の列を占有するか(開いたときアクティブ側へ決定)。</summary>
    private bool _editorOnLeft;
    /// <summary>ファイル本文を EditorBox へ読み込み済みか(初回ロード分の TextChanged を保存対象から外す)。</summary>
    private bool _editorLoaded;
    /// <summary>入力のたびに即書き込まず、少し待ってまとめて保存するためのデバウンスタイマー。</summary>
    private DispatcherTimer? _editorSaveTimer;
    /// <summary>自動保存に失敗した旨を通知済みか(同一編集セッションで繰り返しダイアログを出さない)。</summary>
    private bool _editorSaveErrorNotified;

    /// <summary>エディターが画面に表示されているか。</summary>
    private bool EditorVisible => EditorHost.Visibility == Visibility.Visible;

    /// <summary>I: カーソル位置のテキストファイルをアクティブペイン領域で開く。テキスト系以外・書庫内は対象外。</summary>
    private void OpenEditor()
    {
        var active = Vm.Active;
        if (!active.HasItems || active.Current.IsParent) return;
        if (active.Current.IsDirectory || active.Current.IsArchive) return;

        var path = active.SelectedItemPath;
        // 書庫内ファイルは書き戻せないため編集不可。実ファイルのみ対象。
        if (Filer.Core.ArchivePath.TrySplit(path, out _, out _)) return;
        if (!File.Exists(path)) return;

        var kind = FilePreview.ClassifyByExtension(path);
        if (!FilePreview.IsEditable(kind)) return;

        ResetPaneLayout();                 // ペイン全画面中だと列幅0でオーバーレイが潰れるため通常へ戻す
        if (TerminalVisible) CollapseTerminal();
        if (MemoVisible) CloseMemo();

        TextFileIo.TextContent content;
        try { content = TextFileIo.Read(path); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ファイルを開けません", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _editorPath = path;
        _editorEncoding = content.Encoding;
        _editorHasBom = content.HasBom;
        _editorKind = kind;
        _editorSaveErrorNotified = false;

        _editorLoaded = false;             // 本文設定で起きる TextChanged を保存対象から外す
        EditorBox.Text = content.Text;
        _editorLoaded = true;
        EditorBox.SyntaxHighlighting = EditorHighlighting.ForPath(path, ThemeManager.CurrentMarkdownColors().IsDark);

        _editorOnLeft = Vm.IsLeftActive;   // アクティブ側に置く(プレビューは逆ペインへ出す)
        _editorView = EditorView.OnePane;
        BuildEditorHelp();
        UpdateEditorHeader();
        ApplyEditorView();
        EditorBox.Focus();
        EditorBox.CaretOffset = 0;
    }

    /// <summary>Esc / 再度の I: エディターを閉じる(内容を保存し、一覧へフォーカスを戻す)。</summary>
    private void CloseEditor()
    {
        if (!EditorVisible) return;
        FlushEditor();
        EditorHost.Visibility = Visibility.Collapsed;
        _editorView = EditorView.OnePane;
        _editorPath = null;
        FocusActiveList();
    }

    /// <summary>表示切替キー: エディター表示を 1画面 ⇄ 全画面 でトグルする。</summary>
    private void CycleEditorView()
    {
        if (!EditorVisible) return;
        _editorView = _editorView == EditorView.FullScreen ? EditorView.OnePane : EditorView.FullScreen;
        ApplyEditorView();
        EditorBox.Focus();
    }

    /// <summary>現在の表示形態をオーバーレイの列・列スパンへ反映する。</summary>
    private void ApplyEditorView()
    {
        switch (_editorView)
        {
            case EditorView.OnePane:
                Grid.SetColumn(EditorHost, _editorOnLeft ? 0 : 2);
                Grid.SetColumnSpan(EditorHost, 1);
                break;
            case EditorView.FullScreen:
                Grid.SetColumn(EditorHost, 0);
                Grid.SetColumnSpan(EditorHost, 3);   // 両ペイン＋スプリッターを覆う
                break;
        }
        EditorHost.Visibility = Visibility.Visible;
    }

    /// <summary>ヘッダーにファイル名と、閉じる/全画面/プレビューの実際の割り当てキーを表示する。</summary>
    private void UpdateEditorHeader()
    {
        var name = _editorPath is null ? "" : Path.GetFileName(_editorPath);
        var full = GestureDisplay("view.toggleFullscreen");
        var preview = FilePreview.HasRenderedPreview(_editorKind) ? GestureDisplay("editor.preview") : "";

        var keys = "Esc:閉じる";
        if (!string.IsNullOrEmpty(full)) keys += $"  {full}:全画面";
        if (!string.IsNullOrEmpty(preview)) keys += $"  {preview}:プレビュー";
        EditorHeaderText.Text = $"編集 — {name}  ({keys})";
    }

    /// <summary>エディター中フッターのキー説明を、現在のファイル種別に応じて組み立てる。</summary>
    private void BuildEditorHelp()
    {
        var entries = new List<KeyHelp.ContextHelpEntry>
        {
            new(null, "Escape", "閉じる"),
            new("view.toggleFullscreen", null, "全画面切替"),
        };
        if (FilePreview.HasRenderedPreview(_editorKind))
            entries.Add(new("editor.preview", null, "プレビュー(逆ペイン)"));
        _editorKeyHelp = KeyHelp.BuildContext(KeyMap(), entries);
        UpdateKeyHelp();
    }

    /// <summary>編集中の指定キー: 最新内容を保存し、逆ペイン領域にプレビューを重ねて表示する。</summary>
    private void PreviewFromEditor()
    {
        if (_editorPath is null || !FilePreview.HasRenderedPreview(_editorKind)) return;
        FlushEditor();   // プレビューはファイルから読むため、先に最新内容を書き出す
        Run(() =>
        {
            // エディターはアクティブ側にあるため、プレビューは逆ペイン領域へ重ねる。
            var paneRegion = _editorOnLeft ? RightPane : LeftPane;
            var window = new PreviewWindow(Vm, paneRegion, KeyMap(), startInPaneRegion: true) { Owner = this };
            window.ShowDialog();
        });
        EditorBox.Focus();
    }

    /// <summary>入力のたびに保存をデバウンス予約する(初回ロード中の変更は無視)。</summary>
    private void EditorBox_TextChanged(object? sender, EventArgs e)
    {
        if (!_editorLoaded) return;
        _editorSaveTimer ??= CreateEditorSaveTimer();
        _editorSaveTimer.Stop();
        _editorSaveTimer.Start();
    }

    private DispatcherTimer CreateEditorSaveTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => FlushEditor();
        return timer;
    }

    /// <summary>保留中の編集を、開いたときの文字コード・BOM を保ったままファイルへ書き出す。</summary>
    private void FlushEditor()
    {
        if (!_editorLoaded || _editorPath is null) return;
        _editorSaveTimer?.Stop();
        try
        {
            TextFileIo.Write(_editorPath, EditorBox.Text, _editorEncoding, _editorHasBom);
        }
        catch (Exception ex)
        {
            if (_editorSaveErrorNotified) return;   // 同一セッションで繰り返し通知しない
            _editorSaveErrorNotified = true;
            MessageBox.Show(this, ex.Message, "保存に失敗しました", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- ファイルペインの表示2状態(表示切替キー)。通常(50/50) ⇄ アクティブ側を全画面 をトグルする ----

    /// <summary>0=通常 / 1=アンカー側を全画面。</summary>
    private int _paneStep;
    /// <summary>全画面化の基準とするアクティブ側。</summary>
    private bool _paneAnchorLeft;

    /// <summary>表示切替キー(ターミナル未使用時): ファイルペインを 通常 ⇄ 全画面 でトグルする。</summary>
    private void CyclePaneLayout()
    {
        if (_paneStep == 0) { _paneAnchorLeft = Vm.IsLeftActive; _paneStep = 1; }
        else _paneStep = 0;
        ApplyPaneLayout();
    }

    /// <summary>現在のステップを列幅へ反映する。全画面時は表示側をアクティブにしてフォーカスする。</summary>
    private void ApplyPaneLayout()
    {
        var star = new GridLength(1, GridUnitType.Star);
        var zero = new GridLength(0);
        switch (_paneStep)
        {
            case 0:   // 通常: 50/50
                Col0.Width = star; ColSplit.Width = new GridLength(4); Col2.Width = star;
                PaneSplitter.Visibility = Visibility.Visible;
                break;
            case 1:   // アンカー側を全画面(反対側を隠す)
                Col0.Width = _paneAnchorLeft ? star : zero;
                Col2.Width = _paneAnchorLeft ? zero : star;
                ColSplit.Width = zero; PaneSplitter.Visibility = Visibility.Collapsed;
                SetActivePaneFlags(_paneAnchorLeft); FocusActiveList();
                break;
        }
    }

    /// <summary>ペイン配置を通常(50/50)へ戻す。ペイン切替やターミナル表示の前に呼ぶ。</summary>
    private void ResetPaneLayout()
    {
        if (_paneStep == 0) return;
        _paneStep = 0;
        ApplyPaneLayout();
    }

    /// <summary>終了時に全ターミナルのシェルを終了する。</summary>
    protected override void OnClosed(EventArgs e)
    {
        FlushMemo();                       // 未保存のメモを書き出す
        FlushEditor();                     // 未保存の編集を書き出す
        _terminalPanel?.CloseAll();
        _searchProxy?.Dispose();   // パイプ閉鎖 → 常駐ヘルパーが自己終了する
        base.OnClosed(e);
    }

    /// <summary>
    /// A: アクティブ側の対象フォルダーをお気に入りに登録する。
    /// 登録ダイアログでラベルと登録先グループ(既存選択・自由入力で新規作成)を指定できる。
    /// </summary>
    private void AddFavoriteInteractive()
    {
        var dialog = new FavoriteEditDialog("お気に入り登録",
            Vm.FavoriteTargetPath, "", "", Vm.GetFavoriteGroups()) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        if (!Vm.AddFavorite(dialog.PathText, dialog.LabelText, dialog.GroupText))
            MessageBox.Show(this, $"既に登録されています:\n{dialog.PathText}", "お気に入り登録",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>H: 開いたフォルダーの履歴(新しい順)を表示し、選んだフォルダーへアクティブ側を移動する。</summary>
    private void ShowHistorySelector()
    {
        var history = Vm.GetHistory();
        if (history.Count == 0)
        {
            MessageBox.Show(this, "フォルダー履歴がありません。", "履歴",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var entries = history.Select(p => new SelectionEntry(p, p)).ToList();
        PickAndNavigate("フォルダー履歴", "移動先のフォルダーを選択(新しい順):", entries, numbered: true);
    }

    /// <summary>
    /// 1: お気に入り選択 UI を表示し、選んだフォルダーへアクティブ側を移動する。
    /// グループは Enter/→ で中へ・←/BS で戻る。各項目・グループは編集・削除できる。
    /// </summary>
    private void ShowFavoriteSelector()
    {
        if (Vm.GetFavoritesTree().Count == 0)
        {
            MessageBox.Show(this, "お気に入りが登録されていません。\nA キーで現在のフォルダーを登録できます。",
                "お気に入り", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SelectionDialog("お気に入り選択", "移動先のフォルダーを選択:",
            BuildFavoriteEntries(), numbered: true,
            reload: BuildFavoriteEntries,
            onEdit: EditFavorite,
            onDelete: DeleteFavorite,
            onReorder: ReorderFavorite) { Owner = this };

        if (dialog.ShowDialog() == true && dialog.SelectedValue is { } path)
        {
            Run(() => Vm.NavigateActiveTo(path));
            FocusActiveList();
        }
    }

    /// <summary>SelectionEntry の Value でお気に入りグループを表す接頭辞(項目は素のパス)。</summary>
    private const string FavoriteGroupPrefix = "group:";

    /// <summary>お気に入りツリーを表示用エントリへ変換する(項目はラベル+パス、グループは名前+子一覧)。</summary>
    private IReadOnlyList<SelectionEntry> BuildFavoriteEntries() =>
        BuildFavoriteEntries(Vm.GetFavoritesTree(), "");

    private static IReadOnlyList<SelectionEntry> BuildFavoriteEntries(
        IReadOnlyList<FavoriteNode> nodes, string group) =>
        nodes.Select(n =>
        {
            if (!n.IsGroup)
                return new SelectionEntry(
                    string.IsNullOrWhiteSpace(n.Label) ? n.Path : $"{n.Label}  ({n.Path})", n.Path);
            var groupPath = FavoritesStore.JoinGroup(group, n.Label);
            return new SelectionEntry(n.Label, FavoriteGroupPrefix + groupPath,
                BuildFavoriteEntries(n.Children!, groupPath));
        }).ToList();

    /// <summary>お気に入りツリーから項目をパスで検索し、項目と所属グループパスを返す。</summary>
    private static (FavoriteNode Item, string Group)? FindFavorite(
        IReadOnlyList<FavoriteNode> nodes, string path, string group = "")
    {
        foreach (var n in nodes)
        {
            if (n.IsGroup)
            {
                if (FindFavorite(n.Children!, path, FavoritesStore.JoinGroup(group, n.Label)) is { } found)
                    return found;
            }
            else if (string.Equals(n.Path, path, System.StringComparison.OrdinalIgnoreCase))
            {
                return (n, group);
            }
        }
        return null;
    }

    /// <summary>お気に入りを編集する(項目=ラベル・グループ・パス、グループ=名前の変更)。</summary>
    private void EditFavorite(Window owner, string value)
    {
        if (value.StartsWith(FavoriteGroupPrefix, System.StringComparison.Ordinal))
        {
            var groupPath = value[FavoriteGroupPrefix.Length..];
            var name = groupPath.Split(FavoritesStore.GroupSeparator)[^1];
            var input = new InputDialog("グループ名の変更", "新しいグループ名:", name) { Owner = owner };
            if (input.ShowDialog() == true && input.InputText.Trim() != name
                && !Vm.RenameFavoriteGroup(groupPath, input.InputText))
                MessageBox.Show(owner, "グループ名を変更できませんでした。\n(空・「/」を含む・同じ階層に同名グループがある場合は使用できません)",
                    "グループ名の変更", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (FindFavorite(Vm.GetFavoritesTree(), value) is not { } found)
            return;
        var dialog = new FavoriteEditDialog("お気に入りの編集",
            found.Item.Path, found.Item.Label, found.Group, Vm.GetFavoriteGroups()) { Owner = owner };
        if (dialog.ShowDialog() != true)
            return;
        if (!string.Equals(dialog.PathText, value, System.StringComparison.OrdinalIgnoreCase)
            && FindFavorite(Vm.GetFavoritesTree(), dialog.PathText) is not null)
        {
            MessageBox.Show(owner, $"そのパスは既に登録されています:\n{dialog.PathText}",
                "お気に入りの編集", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Vm.UpdateFavorite(value, dialog.PathText, dialog.LabelText, dialog.GroupText);
    }

    /// <summary>お気に入りを確認の上で削除する(グループは中身ごと)。</summary>
    private void DeleteFavorite(Window owner, string value)
    {
        if (value.StartsWith(FavoriteGroupPrefix, System.StringComparison.Ordinal))
        {
            var groupPath = value[FavoriteGroupPrefix.Length..];
            var result = MessageBox.Show(owner,
                $"グループ「{groupPath}」を削除しますか?\nグループ内のお気に入りもすべて削除されます。",
                "グループ削除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.OK)
                Vm.RemoveFavoriteGroup(groupPath);
            return;
        }

        var confirm = MessageBox.Show(owner, $"お気に入りから削除しますか?\n{value}", "お気に入り削除",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.OK)
            Vm.RemoveFavorite(value);
    }

    /// <summary>お気に入り(項目/グループ)を同じ階層内で上下に移動する(ショートカット番号の変更)。動いたら true。</summary>
    private bool ReorderFavorite(string value, int delta) =>
        value.StartsWith(FavoriteGroupPrefix, System.StringComparison.Ordinal)
            ? Vm.MoveFavoriteGroup(value[FavoriteGroupPrefix.Length..], delta)
            : Vm.MoveFavorite(value, delta);

    private void PickAndNavigate(string title, string prompt, IReadOnlyList<SelectionEntry> entries,
        bool numbered = false, bool letterSelect = false)
    {
        var dialog = new SelectionDialog(title, prompt, entries, numbered, letterSelect) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedValue is { } path)
        {
            Run(() => Vm.NavigateActiveTo(path));
            FocusActiveList();
        }
    }

    private static string DescribeDrive(Filer.Core.DriveItem d)
    {
        if (!d.IsReady)
            return $"{d.RootPath}  (準備されていません)";
        var label = string.IsNullOrEmpty(d.VolumeLabel) ? "" : $" [{d.VolumeLabel}]";
        return $"{d.RootPath}{label}  空き {FormatBytes(d.FreeSpace)} / 全体 {FormatBytes(d.TotalSize)}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:N1} {units[unit]}";
    }

    /// <summary>
    /// アクティブ側リストの「選択中の行(ListViewItem)」へフォーカスする。
    /// ListView コンテナにフォーカスすると最初の矢印キーが選択項目への移動で消費され
    /// カーソルが動かないため、必ず選択行のコンテナにフォーカスを当てる。
    /// バインディング/レイアウト確定後に行うため Dispatcher で遅延実行する。
    /// </summary>
    /// <summary>ターミナルへフォーカスを移す。フォーカス状態フラグを立ててフッターを端末用へ切り替える。</summary>
    private void FocusTerminalPanel()
    {
        _terminalPanel!.FocusActiveTerminal();
        _terminalFocused = true;
        UpdateKeyHelp();
    }

    private void FocusActiveList()
    {
        _terminalFocused = false;   // 一覧へ戻る=ターミナルからフォーカスが外れる
        UpdateKeyHelp();
        UpdateSortIndicators();
        var list = ActiveList;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FocusSelectedItem(list)));
    }

    private static void FocusSelectedItem(ListView list)
    {
        var index = list.SelectedIndex;
        if (index < 0)
        {
            list.Focus();
            return;
        }
        list.ScrollIntoView(list.Items[index]);
        list.UpdateLayout();   // 仮想化されたコンテナを実体化させる
        if (list.ItemContainerGenerator.ContainerFromIndex(index) is ListViewItem item)
            item.Focus();
        else
            list.Focus();
    }

    /// <summary>指定側ペインの現在の表示モードに対応する一覧コントロール(詳細リスト or サムネイルグリッド)。</summary>
    private ListView ListFor(bool isLeft) =>
        (isLeft ? Vm.Left : Vm.Right).ViewMode == PaneViewMode.Grid
            ? (isLeft ? LeftGrid : RightGrid)
            : (isLeft ? LeftList : RightList);

    private ListView ActiveList => ListFor(Vm.IsLeftActive);

    /// <summary>アクティブ側がサムネイルグリッド表示か。</summary>
    private bool ActiveIsGrid => Vm.Active.ViewMode == PaneViewMode.Grid;

    /// <summary>アクティブ側グリッドのタイル外形幅(現在のタイルサイズに依存。仮想化パネルと同じ Core 値)。</summary>
    private double GridTileOuterWidth => Vm.Active.GridCellWidth;
    /// <summary>アクティブ側グリッドのタイル外形高さ(現在のタイルサイズに依存。仮想化パネルと同じ Core 値)。</summary>
    private double GridTileOuterHeight => Vm.Active.GridCellHeight;

    /// <summary>グリッドの現在の列数を表示幅から概算する。</summary>
    private int GridColumns(ListView grid)
    {
        var width = grid.ActualWidth - SystemParameters.VerticalScrollBarWidth - 4;
        return Math.Max(1, (int)(width / GridTileOuterWidth));
    }

    /// <summary>グリッドの1ページ(画面)に入る行数を表示高さから概算する。</summary>
    private int GridRows(ListView grid) =>
        Math.Max(1, (int)(grid.ActualHeight / GridTileOuterHeight));

    /// <summary>↑↓ をグリッドでは行単位、詳細表示では端ループの1行移動にする。</summary>
    private void CursorVertical(GridDirection gridDir, int listDelta)
    {
        if (ActiveIsGrid) { GridMoveCursor(gridDir); return; }
        Vm.Active.MoveCursorWrap(listDelta);
        ScrollActiveIntoView();
    }

    /// <summary>グリッドでカーソルを指定方向へ動かす(左右は端で回り込み・上下は行単位)。</summary>
    private void GridMoveCursor(GridDirection dir)
    {
        var grid = ActiveList;
        var next = GridNavigation.Move(Vm.Active.Entries.Count, GridColumns(grid), Vm.Active.SelectedIndex, dir);
        Vm.Active.MoveCursorTo(next);
        ScrollActiveIntoView();
    }

    /// <summary>グリッドで PageUp/Down ぶん(列数×表示行数)カーソルを動かす。</summary>
    private void GridMovePage(int dirSign)
    {
        var grid = ActiveList;
        var step = GridColumns(grid) * Math.Max(1, GridRows(grid) - 1);
        Vm.Active.MoveCursor(dirSign * step);
        ScrollActiveIntoView();
    }

    /// <summary>Ctrl+G: アクティブ側を 詳細 ⇔ サムネイルグリッド で切り替える。</summary>
    private void ToggleGridView()
    {
        Vm.Active.ToggleViewMode();
        FocusActiveList();   // 表示する側のコントロールへフォーカスを移す
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ScrollActiveIntoView));
    }

    /// <summary>Ctrl+Shift+G: サムネイルグリッドのタイルサイズを 小 ⇔ 大(画像256px) で切り替える。</summary>
    private void ToggleGridSize()
    {
        Vm.Active.ToggleGridSize();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ScrollActiveIntoView));
    }

    /// <summary>
    /// アクティブ側を再読込する。カーソル項目(PaneState 側で追従)に加え、
    /// 一覧のスクロール位置(垂直オフセット)も再読込前の値へ復元する。
    /// </summary>
    private void ReloadActivePreservingScroll()
    {
        var list = ActiveList;
        var scrollViewer = FindVisualChild<ScrollViewer>(list);
        var offset = scrollViewer?.VerticalOffset ?? 0;

        Run(Vm.Active.Reload);

        // 再読込で項目が作り直されるため、レイアウト確定後にオフセットを復元しフォーカスを戻す。
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var sv = scrollViewer ?? FindVisualChild<ScrollViewer>(list);
            sv?.ScrollToVerticalOffset(offset);
            list.UpdateLayout();   // 仮想化コンテナを実体化

            // 選択行が表示範囲内に実体化していればその行へ、なければリスト自体へフォーカス
            // (どちらでも矢印キーはモデル側で処理されるため動作する)。スクロール位置は変えない。
            var index = list.SelectedIndex;
            if (index >= 0 && list.ItemContainerGenerator.ContainerFromIndex(index) is ListViewItem item)
                item.Focus();
            else
                list.Focus();
        }));
    }

    /// <summary>ビジュアルツリーを下って最初の T を返す(無ければ null)。</summary>
    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    /// <summary>アクティブ側の選択行を表示範囲内へスクロールする。</summary>
    private void ScrollActiveIntoView()
    {
        var list = ActiveList;
        if (list.SelectedIndex >= 0)
            list.ScrollIntoView(list.Items[list.SelectedIndex]);
    }

    /// <summary>PageUp/Down の移動量。表示中の行数から概算する。</summary>
    private int PageStep()
    {
        const double rowHeight = 20.0;   // 行高の概算
        var rows = (int)(ActiveList.ActualHeight / rowHeight);
        return Math.Max(1, rows - 1);
    }

    private static string Describe(PaneViewModel pane)
    {
        var targets = pane.Targets;
        if (targets.Count == 0) return "(対象なし)";
        if (targets.Count == 1) return $"「{targets[0].Name}」";
        return $"{targets.Count} 件";
    }

    private void Confirm(string message, Action action)
    {
        if (MessageBox.Show(this, message, "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question)
            != MessageBoxResult.OK)
            return;
        Run(action);
    }

    /// <summary>needConfirm が true なら確認後に、false なら確認せず即座に実行する。</summary>
    private void ConfirmOrRun(bool needConfirm, string message, Action action)
    {
        if (needConfirm)
            Confirm(message, action);
        else
            Run(action);
    }

    private void RenameInteractive(PaneViewModel active)
    {
        if (!active.HasItems || active.Current.IsParent) return;
        var dialog = new InputDialog("名前の変更", "新しい名前:", active.Current.Name) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            Run(() => Vm.RenameCurrent(dialog.InputText));
    }

    /// <summary>Shift+R: マーク(無ければカーソル)の複数項目を連番/置換/正規表現で一括リネームする。</summary>
    private void BulkRenameInteractive(PaneViewModel active)
    {
        var targets = active.Targets
            .Where(e => !e.IsParent)
            .Select(e => (e.FullPath, e.Name))
            .ToArray();
        if (targets.Length == 0) return;

        var targetPaths = new HashSet<string>(targets.Select(t => t.FullPath), StringComparer.OrdinalIgnoreCase);
        var existingNames = active.Entries
            .Where(e => !e.Entry.IsParent && !targetPaths.Contains(e.Entry.FullPath))
            .Select(e => e.Entry.Name)
            .ToArray();

        var dialog = new BulkRenameDialog(targets, existingNames) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Renames.Count > 0)
            Run(() => Vm.BulkRename(dialog.Renames));
    }

    /// <summary>K: アクティブ側の現在フォルダー直下に新規フォルダーを作成する。</summary>
    private void CreateFolderInteractive()
    {
        var dialog = new InputDialog("フォルダー作成", "新しいフォルダー名:", "") { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            Run(() => Vm.CreateDirectoryInActive(dialog.InputText.Trim()));
    }

    /// <summary>X: マーク or カーソルの項目を ZIP 圧縮する。圧縮後ファイル名はダイアログで指定。</summary>
    private void CompressInteractive()
    {
        var targets = Vm.Active.Targets;
        if (targets.Count == 0) return;   // ".." のみ等、対象が無ければ何もしない
        var defaultName = Filer.Core.ZipArchiver.DefaultZipName(targets[0].Name);
        var dialog = new InputDialog("ZIP 圧縮", "圧縮後のファイル名:", defaultName) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
            Run(() => Vm.CompressTargets(dialog.InputText));
    }

    /// <summary>
    /// アクティブ側→非アクティブ側のコピー/移動を、進捗ダイアログ付きで非同期実行する。
    /// 計画作成(対象確定・検証)は UI スレッド、実バイトコピーは背景スレッドで行い UI を固めない。
    /// </summary>
    private void RunTransfer(FileTransferKind kind)
    {
        // Vm は DependencyProperty。背景スレッドからの参照は不可のため UI スレッドでローカルへ捕捉する。
        var vm = Vm;

        FileTransferPlan plan;
        try
        {
            plan = vm.BuildTransferPlan(kind);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "操作に失敗しました", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (plan.IsEmpty) return;

        var title = kind == FileTransferKind.Copy ? "コピー" : "移動";
        var dialog = new TransferProgressDialog(title, (progress, token) => vm.ExecuteTransfer(plan, kind, progress, token))
        {
            Owner = this
        };
        dialog.ShowDialog();

        vm.Active.Reload();
        vm.Inactive.Reload();
        FocusActiveList();

        if (dialog.Error is { } error)
            MessageBox.Show(this, error.Message, "操作に失敗しました", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>
    /// アクティブ側の対象を、進捗ダイアログ付きで非同期削除する(ごみ箱送り/完全削除)。
    /// 対象確定・検証は UI スレッド、実削除は背景スレッドで行い UI を固めない。
    /// </summary>
    private void RunDelete(DeleteKind kind)
    {
        var vm = Vm;   // Vm は DependencyProperty。背景スレッドから触れないようローカルへ捕捉する。

        FileDeletePlan plan;
        try
        {
            plan = vm.BuildDeletePlan(kind);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "操作に失敗しました", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (plan.IsEmpty) return;

        var title = kind == DeleteKind.Recycle ? "ごみ箱へ移動" : "完全削除";
        var dialog = new TransferProgressDialog(title, (progress, token) => vm.ExecuteDelete(plan, kind, progress, token))
        {
            Owner = this
        };
        dialog.ShowDialog();

        vm.Active.Reload();
        FocusActiveList();

        if (dialog.Error is { } error)
            MessageBox.Show(this, error.Message, "操作に失敗しました", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>ファイル操作を実行し、失敗はダイアログで通知する(握りつぶさない)。</summary>
    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "操作に失敗しました", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
