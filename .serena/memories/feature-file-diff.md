# 2ファイル diff 表示(Shift+C / file.diff)

アプリ内蔵の side-by-side 行差分ビュー。VSCode 等の外部ツール不要。既存 `PreviewWindow` の WebView2 + 仮想ホスト基盤を踏襲。`mem:architecture-overview` の補完。

## Core 層(src/Filer.Core、UI 非依存・TDD)
- `LineDiff.cs` — 行単位 LCS 差分。`DiffRowKind`{Equal,Modified,Deleted,Inserted} / `DiffRow`(record Kind/LeftNo/LeftText/RightNo/RightText、番号1始まり・無い側はnull) / `Compute(left,right)`。アルゴリズム=末尾基準 LCS DP 表で Equal/Delete/Insert 列を作り、**連続 Delete 直後の連続 Insert を行ごとに対にして Modified へ畳む**(`FoldModified`)=side-by-side で左右の変更行が揃う。余りは純 Deleted/Inserted。テスト `LineDiffTests`(8)。
- `InlineDiff.cs` — **行内(文字単位)差分**。`InlineSegment`(record Text/Changed) / `Compute(left,right)`→`(左区間列, 右区間列)`。文字単位 LCS で共通=Changed:false・左のみ=削除(Changed:true 左側)・右のみ=追加(Changed:true 右側)、同フラグ連続文字は1区間にまとめる(`SegmentBuilder`)。`MaxProduct=1,000,000`(左長×右長)超過は O(n*m) 回避で全体を1変更区間にフォールバック。**変更行(Modified)の語内強調に使う**。テスト `InlineDiffTests`(7)。
- `DiffSource.cs` — `ReadLines(path)`→`(bool IsBinary, string[] Lines)`。先頭8KBに NUL あればバイナリ(行は空)。BOM 検出は `StreamReader(UTF8, detectEncodingFromByteOrderMarks:true)`。改行は CRLF/LF/CR で分割、末尾改行の空行は付けない。テスト `DiffSourceTests`(5)。
- `DiffTargetResolver.cs` — `Resolve(activeMarkedPaths, leftSelectedPath, rightSelectedPath)`→`DiffResolution`(`DiffTargets?`(Left/RightPath) か `Error?` 日本語)。ルール: **アクティブのマーク==2→その2件**(一覧順=左/右)、**0件→左ペインカーソル vs 右ペインカーソル**、1/3件以上→Error。検証: 同一パス・ディレクトリ・不存在→Error。テスト `DiffTargetResolverTests`(7)。
- `DiffHtmlRenderer.cs` — `ToHtmlDocument(rows,leftName,rightName,ThemeColors)`→side-by-side 2カラム table HTML(行番号 td.n + 本文 td.s、列クラス l/r)。色分け: tr.deleted td.s.l=赤 / tr.inserted td.s.r=緑 / tr.modified td.s=黄、差分背景色は `IsDark` で明暗切替(`ThemeColors` レコードは変更せず CSS 内で定義)。**変更行は `InlineDiff.Compute` で文字単位比較し、変わった文字だけを `<span class="chg">` で強調**(左=`delStrong` 赤 / 右=`insStrong` 緑、modified-黄の上に重ねる。`SegmentCell` がセグメント描画、span 内も `Esc`)。`WebUtility.HtmlEncode` でエスケープ。末尾 `<script>` は MarkdownRenderer と同じ Esc/Enter→`postMessage('close')` / F1→`postMessage('cycle-view')`。`BinaryNoticeDocument(left,right,identical,colors)`=バイナリ時の案内文書。テスト `DiffHtmlRendererTests`(11)。

## App 層(src/Filer.App)
- `PreviewWebHost.cs`(新規 static、internal) — Preview/Diff 共有の下回り。`Host="filer.preview"` / `FilerLocalDir()` / `PreviewDir()`(`%LOCALAPPDATA%\Filer\preview`) / `CreateEnvironmentAsync()`(WebView2 env, userDataFolder=…\WebView2) / `GetPaneRegionRect(FrameworkElement?)`。**PreviewWindow も同ヘルパーへ小リファクタ済み**(旧 private GetFilerLocalDir/GetPreviewDir/GetPaneRegionRect/env生成を委譲)。
- `DiffWindow.xaml(.cs)` — 全画面(WindowStyle=None/Maximized)。ヘッダー(InfoText=左⇔右)+ `wv2:WebView2 x:Name=DiffView`。コンストラクタ `(leftPath,rightPath,FrameworkElement? paneRegion)`。`LoadDiffAsync`=両ファイル `DiffSource.ReadLines`→バイナリなら `BinaryNoticeDocument`(`FilesEqual` でバイト比較)、テキストは `LineDiff.Compute`→`DiffHtmlRenderer.ToHtmlDocument`(色は `ThemeManager.CurrentMarkdownColors()`)→`PreviewDir` へ `diff-{guid}.html` 書出し(`CleanupOldPages` で旧 diff-* 掃除)→ filer.preview 経由 Navigate。`OnWebViewMessage` で close→Close / cycle-view→`CycleView`(全画面⇄ペイン領域、enum `ViewPlacement`)。`OnPreviewKeyDown` で Esc/Enter=閉じる・F1=表示切替。WebView2 失敗はフォールバックせず MessageBox。
- `MainViewModel.ResolveDiffTargets()` — `Active.Marked`(FullPath)・`Left/Right.SelectedItemPath` を `DiffTargetResolver.Resolve` へ。
- `MainWindow.ShowDiff()`(BuildActions `["file.diff"]`) — 解決→成功なら `DiffWindow` を反対ペイン領域指定で ShowDialog、失敗は `MessageBox`(理由)。

## キー
`KeyBindings.cs` `KeyBindingActions.All` に `new("file.diff","2ファイルの差分を表示","ファイル操作",new[]{"Shift+C"},"差分")`。Shift+C 既定(file.copy="C" と非衝突)。設定(Z)で変更可。

## 制約
v1 はテキスト差分のみ。バイナリ/画像は「バイナリのため行差分は表示できません(同一/相違)」表示。書庫内ファイルは未対応(`DiffSource.ReadLines` は実ファイル前提)。語内強調は文字単位 LCS のため、長い共通部分を含む語では文字レベルで断片化することがある(語単位ではない)。
