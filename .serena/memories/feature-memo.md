# メモ機能(U キー)

反対ペインに書き捨てメモ欄(TextBox)をオーバーレイ表示する機能。VSCode のように入力をリアルタイム保存し、次回起動時に復元する。単一の共通メモ(アプリ全体で1つ)。

## Core
- `MemoStore`(`src/Filer.Core/MemoStore.cs`)= 1つのテキストファイルへ本文を Load/Save するだけの単純な永続化。`Load()`=ファイルが無ければ空文字、`Save(text)`=全置き換え(フォルダー無ければ作成)。永続先は `%APPDATA%\Filer\memo.txt`。テスト: `MemoStoreTests`(6件)。

## App(MainWindow)
- XAML: `MemoHost`(Border オーバーレイ、`Grid.Column`/`ColumnSpan` でターミナルと同方式に列を覆う。`Panel.ZIndex=6`=ターミナル(5)より上)。中に `MemoBox`(TextBox、AcceptsReturn/Tab、`InputMethod.IsInputMethodEnabled=True`=メインウィンドウは IME 無効だがメモだけ日本語入力可、テーマ追従ブラシ)。
- キー: `memo.toggle`(`KeyBindingActions.All`、既定 **U**、カテゴリ ツール、HelpLabel「メモ」)。設定 Z で変更可。
- `ToggleMemo`(U)= 表示中なら `CloseMemo`、非表示なら `ResetPaneLayout`(ペイン全画面解除)→ TerminalVisible なら `CollapseTerminal`(重なり回避)→ 初回のみ `MemoStore.Load` を `MemoBox.Text` へ(`_memoLoaded` は Text 設定後に立て初回 TextChanged を保存対象外に)→ 非アクティブ側(`_memoOnLeft=!IsLeftActive`)に OnePane 表示しフォーカス。
- `CloseMemo`(Esc / 再 U)= `FlushMemo`(保存)→ 非表示 → 一覧へフォーカス。
- `CycleMemoView`(F1)= OnePane ⇄ FullScreen(全列 ColumnSpan=3)。`view.toggleFullscreen` のディスパッチは **メモ→ターミナル→ファイルペイン** の順で対象選択。
- 保存: `MemoBox_TextChanged` → 500ms デバウンス `DispatcherTimer` → `FlushMemo`。`CloseMemo`/`CycleMemoView`/`OnClosed` でも書き出す。
- メモ入力中のキー処理: `OnPreviewKeyDown` 冒頭(WebView2 分岐の後)で `e.OriginalSource` が `MemoBox` なら Esc=閉じる・F1(view.toggleFullscreen)=全画面切替だけ処理し、それ以外(C/M/D 等)は奪わずメモへ委ねて return。
- ペイン切替のオーバーレイ対応(Tab・←/→ 共通): `FocusPaneSide(bool left)` に集約。指定側がメモ/ターミナルに覆われている(片側表示でその側、または全画面表示)なら `MemoBox.Focus()`/`FocusActiveTerminal()`、でなければ `SetActivePaneFlags(left)`+`FocusActiveList()`。**裏の(覆われた)ファイルペインを操作対象にしない**のが要点。
  - `pane.switchOrTerminal`(Tab)= `FocusPaneSide(!Vm.IsLeftActive)`。
  - `pane.left`/`pane.right`(←/→)の「相手ペインへ切替」分岐 = `FocusPaneSide(left:true/false)`(親フォルダーへ行く内側方向は従来どおり `GoToParent`+`FocusActiveList`)。グリッド表示中は ←/→ がタイル移動になり切替自体起きない。
  - メモから一覧へ戻すのは Ctrl+Tab(WPF 既定で AcceptsTab の TextBox からフォーカスが抜ける)/ Esc。
  - この共通化前は Tab だけ対策・←/→ が未対策で、メモ/ターミナル表示中に ←/→ で裏ペインがアクティブ化する不具合があった(旧 `SwitchPaneOrFocusTerminal` は廃止)。

詳細なオーバーレイ列スパン方式は `mem:architecture-overview` のターミナル節(`ApplyTerminalView`)と同じ考え方。
