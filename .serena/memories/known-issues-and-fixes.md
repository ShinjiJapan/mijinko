# 既知の不具合と修正履歴

## [修正済] 大量ファイルのフォルダを開くとキー操作可能まで長時間待たされる(2026-06)
- **症状**: G:\oldPhoto(7,131ファイル)等を開くとUIが数秒〜長時間固まる。フォルダ列挙自体は29msで、I/Oは無関係。
- **根本原因**: `PaneViewModel.Refresh`がバインド済みListViewの`ObservableCollection`へ1件ずつ`Add`し、件数分のCollectionChanged通知をUIスレッドが逐次処理していた(検索結果一覧で既に解決済みだったのと同じ問題)。さらに1件Addごとに`RefreshColumns`がLoadedキューへ積まれ二重に重かった。
- **修正**: `Filer.Core.BulkObservableCollection<T>`(ReplaceAll/AddRange=Reset一括通知)を新設し`PaneViewModel.Entries`に適用。`FileSearchDialog`のprivate RangeCollectionも同クラスへ統合。

## [修正済] 大量ファイルのフォルダで一覧表示までの待ちが残る(2026-06)
- **症状**: 上記修正後も表示完了まで7千件で約400ms・4.6万件で約650ms(中規模100件前後は約150ms)。
- **原因の内訳**(`FILER_PERFLOG`+dotnet-traceで実測):
  1. `MainWindow.RefreshColumns`が全行をリフレクション+`FormattedText`生成で実測(O(N)。4.6万件で約200ms)。`MeasureText`が呼び出し毎に`new Typeface`→DWriteフォントハンドルがファイナライザに滞留(トレースで解放に十数秒)。
  2. WPFのレイアウト+可視行コンテナ再生成の固定費 約150ms。
  3. **このPCには常駐UIAクライアントがいて**、WPFが`ItemsControlAutomationPeer`の全項目分の子ピア更新・通知を行う(O(N)・約100〜200ms/移動)。アプリ側で軽量ピアに差し替えると約100ms稼げるがSelectionPattern(UIAテスト)とスクリーンリーダー対応を失うため**不採用**。
- **修正**: ①`RefreshColumns`は`ColumnWidthCandidates.Select`で表示幅スコア上位3件のみ実測(リフレクション廃止・型付きgetter)+`Typeface`キャッシュ。②**2段階表示**(`ListChunking`+`PaneViewModel.Refresh(chunked)`)=ナビゲーション時は先頭128件を即表示し残りをLoaded優先度で一括追加。
- **結果**: 初回描画(paint)は件数によらず**約170〜200ms**(=中規模フォルダ並みの体感)。全件反映は4.6万件で約600ms(キーはその後処理されるが部分表示を操作することはない)。タイトルの件数は`PaneViewModel.EntryCount`(モデル全件)で表示。
- **計測手法**: `FILER_PERFLOG=<パス>`で readSort/refresh/paint/layout/renderIdle をログ。`agent\tmp\verify-clean.ps1`=**UIA完全不使用**のクリーン計測(UIAクライアントが付くとピア処理で数百ms/移動 水増しされるため。ナビは履歴差し替え+H+数字キー)。`verify-trace2.ps1`=dotnet-trace採取、`analyze_trace2.py`=speedscopeのCPU_TIME/UNMANAGEDリーフを実フレームへ帰属して集計。

## [修正済] 互換ジャンクション("My Music"等)を開くと強制終了
- **症状**: ユーザープロファイル直下の `My Music`/`My Pictures`/`My Documents`/`My Videos` を Enter で開くとアプリがクラッシュ。
- **根本原因**: これらは旧Windows互換用のジャンクション(Hidden+System+ReparsePoint)。実体は開けず `DirectoryLister.Read` が `DirectoryNotFoundException`/`UnauthorizedAccessException` を投げる。Enter(Open)経路が `Run()`(try/catch)を通らず未処理例外でWPFアプリ終了。スペースは無関係。
- **修正**: `MainWindow.OnPreviewKeyDown` の Enter/Back/F5 を `Run(...)` 経由に。開けないディレクトリはエラーダイアログ通知し現在地に留まる。`PaneState.Load` は `_reader.Read` を最初に呼ぶため例外時も状態不変。
- **テスト**: `PaneStateTests.Open_WhenTargetUnreadable_PreservesStateAndThrows` + `JunctionNavigationIntegrationTests`(実FS)。

## [修正済] Tabペイン切替を繰り返すとカーソル上下が不安定
- **症状**: Tabでペイン切替後、最初の↑↓キーが効かず(カーソルが動かない)、繰り返すと不安定。
- **根本原因**: `list.Focus()` が **ListViewコンテナ**にフォーカスしていたため、フォーカス取得直後の最初の矢印キーが「選択項目へフォーカスを移す」操作で消費され、選択(カーソル)が進まない。Tabのたびに再発。
- **調査手法**: ウィンドウTitleにアクティブ側パスとカーソル位置 `[pos/total]` を表示する `MainViewModel.StatusText` を追加し、`process.MainWindowTitle` を読んで挙動を計測(UIAのGetSelectionは不安定だった)。SendKeysは `Microsoft.VisualBasic.Interaction.AppActivate($pid)` で前面化してから送ると届く。起動直後の最初の1キーは合成入力がフォアグラウンド遷移中に失われるハーネス由来(アプリ不具合ではない)。
- **修正(根治)**: 矢印系(Up/Down/PageUp/PageDown/Home/End)を **`OnPreviewKeyDown`(ウィンドウレベル)で明示処理**し、`PaneViewModel.MoveCursor/MoveCursorTo/MoveToTop/MoveToBottom` でモデルのカーソルを直接動かす(`e.Handled=true` でListViewネイティブ処理を抑止)。フォーカス確定状況に依存せず初回キーから確実に動く。`MainWindow.ActiveList/ScrollActiveIntoView/PageStep` を追加。あわせて `FocusSelectedItem`(選択行コンテナへフォーカス)+ `Activated` で別アプリ復帰時もコンテナでなく選択行にフォーカス。
- **検証**: Title計測で Up/Down/Home/End/PageDown と Tab往復後の初回↓がすべて即時反映を確認。Coreテスト22件合格。

## [修正済] VSCode統合ターミナルから起動したFilerで V(VSCode起動)が無反応(2026-06)
- **症状**: `v` を押してもVSCodeが開かない。エラーダイアログも出ず「何も起きない」。
- **根本原因**: FilerをVSCodeの統合ターミナル/拡張ホストから起動すると環境変数 `ELECTRON_RUN_AS_NODE=1` を継承する。`ExternalToolLauncher.Launch` は `UseShellExecute=false` で `Code.exe` を起動するため子プロセスが親の環境を継承し、**Code.exe が GUI ではなく純粋な Node.js として動作**。引数のパスをモジュールとして読もうとして `Cannot find module '<path>'` で即終了する。`Process.Start` 自体は成功するので Filer 側に例外は出ず、`Run()` のエラーダイアログも出ない=無反応に見える。キーバインド(`-> action tool:vscode`)・設定・Code.exe解決はすべて正常だった。
- **調査手法**: `FILER_KEYLOG` でアクション発火を確認(正常)→ 新規Code.exeプロセスが立たないことを高頻度ポーリングで確認 → 同等の `Process.Start(Code.exe,"<path>",UseShellExecute=false)` をPowerShellで再現し `Cannot find module` を観測 → `$env:ELECTRON_RUN_AS_NODE=1` を確認。
- **修正(根治)**: `Filer.Core.ChildProcessEnvironment.Scrub(IDictionary)` を新設し、子プロセス起動前に `ELECTRON_RUN_AS_NODE` を除去。`ExternalToolLauncher` の `UseShellExecute=false` 経路で `psi.Environment` に適用。`UseShellExecute=true`(PATH解決フォールバック)経路はEnvironment編集不可のため非適用だが、Code.exeは既知の場所解決で false 経路を通る。
- **テスト**: `ChildProcessEnvironmentTests`(除去/無関係変数保持)2件。実機E2E: `ELECTRON_RUN_AS_NODE=1` 環境で publish 起動→V→新規Code.exe 9プロセス起動を確認(修正前0)。

## [未対応・要検討] Hidden+System ファイルを既定で表示
- `DirectoryLister` が隠し/システム属性も列挙するため互換ジャンクション等が一覧に出る。Explorer同様、既定非表示+トグル表示が望ましい(silent除外はしない方針)。

## [修正済] 日本語IMEオンだと英字キーのショートカットが効かず未確定文字欄が左上に出る
- **症状**: IMEが日本語入力モードだと S/H/Z 等の英字キーがショートカットにならず、IMEの未確定文字欄(変換ウィンドウ)が画面左上に開く。
- **根本原因**: IME有効な要素にフォーカスがあると英字キーは `e.Key == Key.ImeProcessed` になり(実キーは `e.ImeProcessedKey`)、IMEが変換を開始する。`InputMethod.IsInputMethodEnabledProperty` は**継承されない添付プロパティ**のため、Windowに `SetIsInputMethodEnabled(false)` してもフォーカスが ListViewItem 等の子要素へ移るとIMEが有効に戻る。さらに **WPF はフォーカス移動のたびにフォーカス要素のこのプロパティを見て `ImmAssociateContext` を自前で再実行する**ため、HWNDレベルで `ImmAssociateContext(hwnd, NULL)` してもフォーカス移動で上書きされ無効(実験で確認)。
- **修正(根治)**: `src/Filer.App/Ime.cs` の `Ime.Disable(Window)`。`Keyboard.GotKeyboardFocusEvent` をウィンドウに `handledEventsToo:true` で AddHandler し、**フォーカスを受けた要素自身へ `InputMethod.SetIsInputMethodEnabled(false)` を毎回設定**(WPFがその要素のフォーカス中はIMEを切る)。HwndHost(WebView2)は別HWNDが独自にIMEを扱うため対象外=ターミナル/プレビュー内の日本語入力は維持。適用先: MainWindow / SortDialog / SelectionDialog / PreviewWindow(いずれも文字入力欄なし)。文字入力欄のあるダイアログ(検索・設定・改名等)はIME必要なので非適用。
- **防御**: ディスパッチでの `Key.System→SystemKey / Key.ImeProcessed→ImeProcessedKey` 実キー解決(MainWindow/SettingsDialog/ToolEditDialog)は安全網として維持。
- **調査手法**: 環境変数 `FILER_KEYLOG=<パス>` で `OnPreviewKeyDown` の生イベント(Key/SystemKey/ImeProcessedKey/Modifiers/Source)をファイルへ追記するログを常設(`MainWindow.KeyLogPath`)。IMEオン状態の再現は `ImmGetDefaultIMEWnd`+`WM_IME_CONTROL(IMC_SETOPENSTATUS=1)` をFilerのスレッドへ送る(`agent\tmp\verify-ime.ps1`)。修正後は強制オンしてもキーが `Key=S` 素のまま届きアクション発火を確認。

## UI Automation 検証の落とし穴(agent\tmp の verify-*.ps1 群)
- **WPF ListView+GridView の行は UIA では `ControlType.DataItem`**(ListItem ではない)。FindAllの条件に注意。
- **オーナー付きモーダルダイアログが `RootElement.FindAll(Children, ProcessId)` に出ないことがある** → `GetForegroundWindow()`+`AutomationElement.FromHandle` で取るのが確実。タイトル確認は `GetWindowText`。
- **PowerShell 5.1 (powershell.exe) は BOMなしUTF-8 の .ps1 を誤読**して日本語リテラルが化けて構文エラー → 検証スクリプトは `pwsh` で実行する。
- SendKeys は AttachThreadInput+SetForegroundWindow で前面化してから。TextBoxへの日本語入力は SendKeys でなく UIA ValuePattern.SetValue が確実(IME非経由)。
- **前面化はターゲットスレッドへのAttachだけでは失敗することがある**(フォアグラウンドロック。VSCode等が前面のとき`SetForegroundWindow`が無視され、SendKeysが前面アプリへ誤送信される危険)。対策=`agent\tmp\verify-bigfolder.ps1`方式: ①現在の前面ウィンドウのスレッドとターゲットスレッドの**両方**へAttachThreadInput→BringWindowToTop→SetForegroundWindow、②**キー送信前に毎回`GetForegroundWindow`のPIDがFilerか確認し、違えば送信せず中断**(Send-IfFilerガード)。\n- GridView行(DataItem)のUIA Nameはアイテムの`ToString()`(EntryViewModelは未オーバーライドで全行同一文字列)。選択変化の検出はNameでなく**選択要素の`GetRuntimeId()`比較**で行う(`agent\tmp\verify-downkey.ps1`)。

## 関連: StatusText 機能
ウィンドウTitle = `Filer — <アクティブパス>  [pos/total]`。`MainViewModel.StatusText`(Left/Right の PropertyChanged を購読し、アクティブ側の SelectedIndex/CurrentPath 変化で更新)。

## UIA動作確認の注意(2026-06)
- WPFのモーダルダイアログ(Owner付きウィンドウ)はUIAツリーでは**デスクトップ(RootElement)直下ではなくオーナーウィンドウの子**に現れる。RootElementのChildrenを探すと見つからず「ダイアログが開かない」と誤判定する。`$main.FindFirst(Children/Descendants, Name=...)`で探すこと。
- `FILER_KEYLOG`はキーイベントに加えアクション解決結果(`-> action <id>` / `-> no action (bindings=N)`)も出力する(MainWindow.OnPreviewKeyDown)。
- 検証スクリプト例: `agent\tmp\test_filesearch.ps1`(F→検索→転送)/`agent\tmp\test_jump.ps1`(ジャンプ)。AttachThreadInputで最前面化→SendKeys、ダイアログ内はValuePattern/InvokePatternでフォーカス不要操作。
