# テキスト編集機能(I キー)

カーソル位置のテキストファイルを**アクティブペイン領域**にオーバーレイ表示して編集する機能(AvalonEdit)。拡張子別シンタックスハイライト・自動保存・編集中の逆ペインプレビューに対応。メモ機能(`mem:feature-memo`)と同方式のオーバーレイだが、メモは「反対ペイン・共通1ファイル」なのに対し、エディターは「アクティブ側・任意の実ファイル」を扱う。

## Core
- `TextFileIo`(`src/Filer.Core/TextFileIo.cs`)= テキストの読み書き。`Read(path)`→`TextContent(Text, Encoding, HasBom)`。文字コードは `TextEncodingDetector.Detect`(BOM 優先、無 BOM は UTF-8 妥当なら UTF-8、でなければ Shift-JIS/CP932)で判定し BOM は本文へ含めない。`Write(path, text, encoding, hasBom)`= **開いたときの文字コード・BOM を維持**して上書き(UTF-8 のみ `UTF8Encoding(hasBom)` で BOM 有無を切替)。テスト: `TextFileIoTests`(7件)。
- `FilePreview.IsEditable(kind)`= Text/Markdown/Code/Html が true(画像・PDF・None は不可)。`FilePreview.HasRenderedPreview(kind)`= Markdown/Html/Code が true(編集中の逆ペインプレビュー可否)。テスト: `FilePreviewTests` に追加。

## ハイライト(App)
- `EditorHighlighting.ForPath(path, isDark)`(`src/Filer.App/EditorHighlighting.cs`)= 拡張子→`IHighlightingDefinition`。`.md/.markdown`→`MarkdownHighlighting.ForTheme`、`.cls/.apex`→`ApexHighlighting.ForTheme`、その他は AvalonEdit 組み込み `GetDefinitionByExtension`(近い言語へ寄せる別名: ts/tsx/jsx/json→.js、kt→.java、go/rs→.cpp、xaml/csproj→.xml)。対応無しは null=ハイライトなし。
- `ApexHighlighting`(`src/Filer.App/ApexHighlighting.cs`)+ 埋め込み `Assets/apex.xshd`(csproj に `EmbeddedResource`)= Apex キーワード/型/SOQL([SELECT/FIND ...] のみ)/文字列/コメント/数値/アノテーション。`MarkdownHighlighting` と同じく名前付き色を実行時にテーマ配色へ差し替え。プレビュー(highlight.js apex)はそのまま使えないため AvalonEdit 用に新規作成したもの。

## App(MainWindow)
- XAML: `EditorHost`(Border オーバーレイ、`Grid.Column`/`ColumnSpan`、`Panel.ZIndex=7`=メモ(6)/ターミナル(5)より上)。中に `EditorBox`(AvalonEdit、`ShowLineNumbers`、IME 有効化)+ `EditorHeaderText`。`EditorBox.TextArea` にも IME 有効化(コンストラクタ、MemoBox と同様)。
- キー: `entry.edit`(既定 **I**、カテゴリ 基本操作、HelpLabel「編集」)で開く。`editor.preview`(既定 **F2**、HelpLabel null=エディター中フッターのみ表示)で逆ペインプレビュー。設定 Z で変更可。
- `OpenEditor`(I)= アクティブのカーソル項目が実ファイル(書庫内不可)かつ `IsEditable` のとき。`ResetPaneLayout`→ターミナル/メモを閉じ→`TextFileIo.Read`→`EditorBox.Text`(`_editorLoaded` を Text 設定後に立て初回 TextChanged を保存対象外に)→`EditorHighlighting.ForPath` 適用→**アクティブ側**(`_editorOnLeft=IsLeftActive`)に OnePane 表示しフォーカス。`_editorEncoding`/`_editorHasBom`/`_editorKind` を保持。
- `CloseEditor`(Esc / メモ・ターミナル起動時)= `FlushEditor`(保存)→非表示→一覧へフォーカス。
- `CycleEditorView`(F1)= OnePane ⇄ FullScreen。`view.toggleFullscreen` のディスパッチは **エディター→メモ→ターミナル→ファイルペイン** の順。
- `PreviewFromEditor`(F2、`HasRenderedPreview` のときのみ)= `FlushEditor` で先に保存→逆ペイン領域(`_editorOnLeft?RightPane:LeftPane`)へ `new PreviewWindow(..., startInPaneRegion:true)` を `ShowDialog`→閉じたら `EditorBox.Focus()`。
- 保存: `EditorBox_TextChanged`→500ms デバウンス→`FlushEditor`(`TextFileIo.Write`、失敗時はセッション中1回だけダイアログ通知。`OnClosed` でも書き出す)。
- 編集中のキー処理: `OnPreviewKeyDown` のメモ分岐の**前**で `EditorVisible && EditorBox.IsKeyboardFocusWithin` なら Esc=閉じる・F1=全画面・F2=プレビューだけ処理し、それ以外は奪わずエディターへ委ねて return。フッター: `UpdateKeyHelp` のエディター分岐(`_editorKeyHelp`、`BuildEditorHelp` がファイル種別に応じ組み立て)。
- `FocusPaneSide` にエディター分岐を追加(覆われた側はファイルペインでなく `EditorBox.Focus()`)。

## PreviewWindow
- コンストラクタに `bool startInPaneRegion=false` を追加。true なら `Loaded` で `_view=PaneRegion`+`ApplyPreviewView()`(既定は全画面)。編集中の逆ペインプレビューで使う。
- プレビュー→編集の相互遷移: `public bool EditRequested`。`TryHandleBoundAction` に `case "entry.edit" when FilePreview.IsEditable(_kind)` を追加し、編集可能テキストのプレビュー中に編集キー(既定 I)で `EditRequested=true`+`Close()`。`MainWindow.PreviewCurrent` は `ShowDialog` 後 `window.EditRequested` を見て true なら編集モードへ(false のときだけ `FocusActiveList`)。**プレビューの表示形態・位置を引き継ぐ**: `PreviewWindow.EditAsFullScreen`(`RequestEdit` 時に `_view==Maximized` を記録)を見て `OpenEditor(view, onLeft)` を呼ぶ(全画面なら `EditorView.FullScreen`、ペイン領域ならプレビュー側=`!Vm.IsLeftActive` の1ペイン)。`OpenEditor()` 無引数版は従来どおりアクティブ側 OnePane。プレビュー経由は閉じる際のフォーカス復元に負けないよう `OpenEditor` 末尾で `Dispatcher.BeginInvoke(Input)` により `EditorBox.Focus()` を遅延確定(同期 Focus だと Esc が効かない不具合になる)。`ShowCurrent` のヘッダー(InfoText)に編集可能なら `(I:編集)`(キーは `_keyMap.GesturesFor("entry.edit")` 追従)を併記。
  - **レンダリング表示(WebView2 フォーカス)中の編集キー転送が要点**: Markdown/Code のレンダリング表示は WebView2 にフォーカスがあり WPF の `OnPreviewKeyDown`/`TryHandleBoundAction` にキーが届かない(S が効くのは HTML 側 JS が `toggle-source` を転送しているため)。対策として `MarkdownRenderer.ToHtmlDocument`/`CodeRenderer.ToHtmlDocument` に `editGestures` 引数を追加し、JS の keydown で編集キー一致時に `postMessage('request-edit')`(`KeyChordJs.MatchExpression`、空なら `if (false)` で発火せず)。`PreviewWindow.EditGestures()`=編集可能種別のときだけ `entry.edit` のジェスチャを渡す。`OnWebViewMessage` の `case "request-edit"`→`RequestEdit()`(`EditRequested=true`+`Close()`)。テキスト/Code ソース表示は WPF フォーカスのため `TryHandleBoundAction` の `entry.edit` 経路でそのまま動く。テスト: `MarkdownRendererTests`/`CodeRendererTests` に編集キー埋め込みの確認を追加。
