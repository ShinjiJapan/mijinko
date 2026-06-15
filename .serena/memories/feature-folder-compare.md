# フォルダー(ツリー)比較(Ctrl+Shift+C / folder.compare)

左右ペインのフォルダーを再帰比較し、結果をツリーで side-by-side 表示する。`mem:feature-file-diff`(2ファイル diff)と同じ WebView2 + 仮想ホスト基盤(`PreviewWebHost`)を踏襲。`mem:architecture-overview` の補完。

## Core 層(src/Filer.Core、UI 非依存・TDD)
- `FolderCompare.cs` — 比較の型とロジック。
  - `FolderCompareKind`{Same,Modified,LeftOnly,RightOnly} / `FolderCompareOptions`(record CompareSize=true/CompareDate=false/CompareContent=false/Recursive=true/**ShowSame=true**。ShowSame は表示専用=比較ロジックは無視しレンダラー側で使用) / `CompareDirEntry`(record Name/IsDirectory/Size/LastModifiedUtc) / `FolderCompareNode`(record Name/IsDirectory/Kind/LeftPath?/RightPath?/LeftSize?/RightSize?/Children。片側のみは無い側 null) / `FolderCompareSummary`(record Same/Modified/LeftOnly/RightOnly=ファイルのみ集計)。
  - `IFolderCompareSource`(抽象: `List(dir)`→直下項目(".."除外) / `ContentEquals(l,r)`→バイト一致)。テストはインメモリ差し替え。
  - `FolderComparer`(static): `Compare(leftRoot,rightRoot,options,source,token)`=名前単位で大小無視対応付け→ディレクトリ群→ファイル群の順に名前で安定整列。**比較基準が全偽ならサイズ比較へフォールバック**(差異が常に同一になる事故防止)。判定`FilesDiffer`=サイズ/更新日時/内容のいずれか有効基準で差→Modified。両ディレクトリは Recursive 時に再帰し配下に1つでも差異あれば Modified。種別不一致(同名で dir vs file)は Modified。片側のみ(`OneSided`)は LeftOnly/RightOnly、ディレクトリは配下を同状態で再帰展開。`Summarize(nodes)`=ファイル状態別件数。`FilterDifferencesOnly(nodes)`=Same を除去した差異のみツリー(ShowSame=OFF 用。配下全同一のディレクトリは丸ごと除外)。
- `FileSystemFolderCompareSource.cs` — 実 FS 実装。`List`=DirectoryInfo 列挙(**リパースポイントは潜らず除外**=リンク越し無限ループ防止)、`ContentEquals`=サイズ先比較→64KB バッファでストリーム逐次バイト比較(ハッシュ不要のペア比較)。
- `FolderCompareTargetResolver.cs` — `FolderCompareTargets`(Left/RightPath)+`FolderCompareResolution`(Targets?/Error?)。`Resolve(leftPath,rightPath)`=同一パス(大小無視)・不存在・ファイル指定を日本語エラー、OK なら Targets。
- `FolderComparePreferenceStore.cs` — `FolderCompareOptions` を JSON 永続化(`%APPDATA%\Filer\folder-compare-prefs.json`)。Load は未保存/破損で既定値。
- `FolderCompareHtmlRenderer.cs` — ツリー→side-by-side 2カラム HTML(行番号なし)。`ToHtmlDocument(nodes,leftName,rightName,ThemeColors[,fullscreenGestures,transferGestures])`。色分け: modified=黄(両セル)/leftonly=赤(左)/rightonly=緑(右)/same=無色。インデント(深さ×4 空白)+📁/📄+名前+サイズ。**変更ファイル行のみ `tr.clickable`+data-l/data-r** で行クリック→`diff\t左\t右`(区切り=`DiffSeparator`=タブ)を postMessage(差分を開く)。Esc/Enter→close、表示切替キー→cycle-view、**転送キー(`transferGestures`=既定T)→transfer**(JS式 `KeyChordJs.MatchExpression`)。空ツリーは「差異はありません」。`Esc`=HtmlEncode。
- `FolderCompareTransfer.cs` — **「転送して閉じる」のファイル収集**(UI 非依存)。`FolderCompareSide`{Left,Right}+`FolderCompareTransferItem`(RelativePath/FullPath)。`Collect(nodes,side,includeDifferences,includeSame)`=木を再帰し**ファイルのみ**を相対パス付きで収集。その側に実体が無い(LeftOnly の右等)は対象外。同一=「重複」(includeSame)、それ以外(変更/その側のみ)=「差分」(includeDifferences)。テスト FolderCompareTransferTests(5)。

## App 層(src/Filer.App)
- `FolderCompareOptionsDialog.xaml(.cs)` — 比較前のオプション選択。チェックボックス(サイズ/更新日時/内容/再帰/同一表示。アクセスキー _S/_D/_C/_R/_I)。既定は前回値。`Options` を返す。`DialogButton` スタイル使用。Ime.Disable。
- `FolderCompareTransferDialog.xaml(.cs)` — **「転送して閉じる」ダイアログ**(ファイル検索の転送イメージ)。左ペイン[差分][重複]・右ペイン[差分][重複]の4チェックボックス(件数表示。既定は左右とも差分のみ)。`FolderCompareTransferSelection`(LeftDifferences/LeftSame/RightDifferences/RightSame+Any)を返す。`DialogButton`・Ime.Disable。
- `FolderCompareWindow.xaml(.cs)` — 全画面(`DiffWindow` と同型)。`(leftPath,rightPath,FolderCompareOptions,FrameworkElement? paneRegion,KeyBindingMap,MainViewModel)`。`LoadAsync`=`FileSystemFolderCompareSource`で`Task.Run`比較(内容比較が重いため背景。CTS で閉じたらキャンセル)→比較結果`_nodes`/集計`_summary`を保持(`_ready`)→ShowSame=false なら`FilterDifferencesOnly`→`ToHtmlDocument`(色=`ThemeManager.CurrentMarkdownColors()`)→`PreviewDir`へ`foldercmp-{guid}.html`(`CleanupOldPages`)→filer.preview 経由 Navigate。InfoText に「変更/左のみ/右のみ/同一」件数+「[T] 転送して閉じる」。`OnWebViewMessage`: close→Close / cycle-view→`CycleView` / **transfer→`ShowTransferDialog`** / `diff\t…`→`OpenDiff`(=`DiffWindow` を同 paneRegion で ShowDialog)。`OnPreviewKeyDown`: Esc/Enter=閉じる・表示切替キーは`KeyChordWpf.Resolve`・**T(修飾なし)=転送**。`ShowTransferDialog`=`FolderCompareTransferDialog`(_summary)→OK で各側`TransferSide`(=`FolderCompareTransfer.Collect`→`FileInfo`で`FileEntry`化(消えた項目除外)→`_main.TransferComparisonToPane`)→Close。WebView2 失敗はフォールバックせず MessageBox。
- `MainViewModel.ResolveFolderCompareTargets()` — `Left.DirectoryPath` vs `Right.DirectoryPath` を `FolderCompareTargetResolver.Resolve`(=左ペイン現在フォルダー vs 右ペイン現在フォルダー)。
- `MainViewModel.TransferComparisonToPane(side,label,baseDir,entries)` — `_searchResults.Register`(検索結果と共有の`SearchResultsReader`)で仮想一覧化し、`side`に応じ`Left`/`Right`ペインを`NavigateTo`(search://…)。ラベル例「比較(左): 差分+重複」。
- `MainWindow`: `_folderComparePrefs`(FolderComparePreferenceStore、`%APPDATA%\Filer\folder-compare-prefs.json`)。`ShowFolderCompare`(BuildActions `["folder.compare"]`)=解決→`FolderCompareOptionsDialog`(前回値)→OK で Save→`FolderCompareWindow` を反対ペイン領域指定+`Vm`渡しで ShowDialog。失敗は MessageBox。

## キー
`KeyBindings.cs` に `new("folder.compare","フォルダー(ツリー)比較","ファイル操作",new[]{"Ctrl+Shift+C"},"フォルダー比較")`。file.diff(Shift+C)と非衝突。設定(Z)で変更可。
**「転送して閉じる」= T 固定**(ウィンドウローカル。グローバル設定には登録しない=`terminal.open`の既定 T と衝突するため。ファイル検索ダイアログの T と同じ流儀。WebView フォーカス時は HTML 側 JS が postMessage、WPF フォーカス時は OnPreviewKeyDown が処理)。

## 制約 / 仕様
- 比較対象は**左右ペインの現在フォルダー**(カーソル位置は不問)。サブフォルダーを比較したいときはそのフォルダーへ移動して実行。
- 「変更」判定はオプション(サイズ/更新日時/内容)の論理和。内容比較はサイズ先判定後にバイト比較(重い→背景実行)。全基準 OFF はサイズ比較にフォールバック。
- リパースポイントは潜らない。書庫(zip)内フォルダーは非対応(実フォルダー前提)。
- 変更ファイル行クリックで `DiffWindow`(2ファイル diff)を開く。`LineDiff` 等は diff 側を再利用。
- **転送して閉じる(T)**: 各ペインへ「差分」「重複」を仮想一覧(`SearchResultsReader`、検索結果と同じ基盤)として転送。差分=変更+その側のみ、重複=同一。相対パスで一覧化、".."=比較した基準フォルダー。
- テスト: FolderComparerTests(15)/FolderCompareTransferTests(5)/FolderCompareTargetResolverTests(4)/FolderComparePreferenceStoreTests(3)/FolderCompareHtmlRendererTests(5)。
