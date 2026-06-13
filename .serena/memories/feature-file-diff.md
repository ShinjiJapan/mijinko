# 2ファイル diff 表示(Shift+C / file.diff)

アプリ内蔵の side-by-side 行差分ビュー。VSCode 等の外部ツール不要。既存 `PreviewWindow` の WebView2 + 仮想ホスト基盤を踏襲。`mem:architecture-overview` の補完。

## Core 層(src/Filer.Core、UI 非依存・TDD)
- `LineDiff.cs` — 行単位差分。`DiffRowKind`{Equal,Modified,Deleted,Inserted} / `DiffRow`(record Kind/LeftNo/LeftText/RightNo/RightText、番号1始まり・無い側はnull) / `Compute(left,right)`。**①共通の先頭(prefix)・末尾(suffix)行をトリム→②中央のみ差分**。中央は `leftLen*rightLen <= MaxProduct`(=4,000,000、約2000×2000行・LCS表16MB)なら末尾基準 LCS DP、超過なら **全置換フォールバック**(中央左を全 Deleted→中央右を全 Inserted)で O(n*m) のメモリ/時間爆発を回避。最後に**連続 Delete 直後の連続 Insert を行ごとに対にして Modified へ畳む**(`FoldModified`)=side-by-side で左右の変更行が揃う(全置換も Modified 行群になる)。トリムにより「大きいが大部分共通」ファイルは高速・高精度、「大きく全く別物」は安全にフォールバック。テスト `LineDiffTests`(11、5000行・3000×3000別物も含む)。
- `InlineDiff.cs` — **行内(文字単位)差分**。`InlineSegment`(record Text/Changed) / `Compute(left,right)`→`(左区間列, 右区間列)`。文字単位 LCS で共通=Changed:false・左のみ=削除(左 Changed:true)・右のみ=追加(右 Changed:true)、同フラグ連続文字は1区間に統合(`SegmentBuilder`)。`MaxProduct=1,000,000` 超過は全体を1変更区間にフォールバック。**変更行(Modified)の語内強調に使う**。テスト `InlineDiffTests`(7)。
- `TextEncodingDetector.cs`(新規 static、**ContentSearcher から抽出し共通化**) — 先頭サンプルからバイナリ/文字コード判定。`SampleSize=64KB` / `IsBinary(ReadOnlySpan<byte>)`(NUL あり=バイナリ。ただしテキスト BOM(UTF-8/16/32)があればテキスト) / `Detect(ReadOnlySpan<byte>)`(BOM→UTF-8/16/32、無ければ `IsValidUtf8` 真→UTF-8 / 偽→`ShiftJis`(CP932、CodePagesEncodingProvider 登録)) / `ShiftJis`。`ContentSearcher.GrepFile` も同クラスを使用(旧 private DetectEncoding/IsBinary/IsValidUtf8/ShiftJis を委譲・重複排除)。テスト `TextEncodingDetectorTests`(6)。
- `DiffSource.cs` — `Read(path, maxBytes=DefaultMaxBytes)`→`DiffFileContent`(`DiffContentKind`{Text,Binary,TooLarge} / Lines)。`DefaultMaxBytes=10MB`。**サイズ超過→TooLarge(読み込まない)**、`TextEncodingDetector.IsBinary`→Binary、それ以外は `TextEncodingDetector.Detect` で**文字コード自動判定(BOM/UTF-8/Shift-JIS)**しデコード。改行は CRLF/LF/CR で分割・末尾改行の空行なし。テスト `DiffSourceTests`(8、Shift-JIS/TooLarge 含む)。
- `DiffTargetResolver.cs` — `Resolve(activeMarkedPaths, leftSelectedPath, rightSelectedPath)`→`DiffResolution`(`DiffTargets?`(Left/RightPath) か `Error?` 日本語)。ルール: **アクティブのマーク==2→その2件**(一覧順=左/右)、**0件→左ペインカーソル vs 右ペインカーソル**、1/3件以上→Error。検証: 同一パス・ディレクトリ・不存在→Error。テスト `DiffTargetResolverTests`(7)。
- `DiffHtmlRenderer.cs` — `ToHtmlDocument(rows,leftName,rightName,ThemeColors)`→side-by-side 2カラム table HTML(行番号 td.n + 本文 td.s、列クラス l/r)。色分け: tr.deleted td.s.l=赤 / tr.inserted td.s.r=緑 / tr.modified td.s=黄、`IsDark` で明暗切替(`ThemeColors` レコードは変更せず CSS 内定義)。**変更行は `InlineDiff.Compute` で文字単位比較し、変わった文字だけを `<span class="chg">` で強調**(左=delStrong 赤 / 右=insStrong 緑、`SegmentCell`、span 内も `Esc`)。`WebUtility.HtmlEncode` でエスケープ。末尾 `<script>` は Esc/Enter→`postMessage('close')` / F1→`postMessage('cycle-view')`。案内文書: `BinaryNoticeDocument`(バイナリ・同一/相違)/`SizeLimitNoticeDocument`(上限MB)/共通 private `NoticeDocument`。テスト `DiffHtmlRendererTests`(12)。

## App 層(src/Filer.App)
- `PreviewWebHost.cs`(static、internal) — Preview/Diff 共有。`Host="filer.preview"` / `FilerLocalDir()` / `PreviewDir()` / `CreateEnvironmentAsync()` / `GetPaneRegionRect(FrameworkElement?)`。PreviewWindow も委譲済み。
- `DiffWindow.xaml(.cs)` — 全画面。`(leftPath,rightPath,FrameworkElement? paneRegion)`。`LoadDiffAsync`=両ファイル `DiffSource.Read`→**どちらか TooLarge なら `SizeLimitNoticeDocument`**、どちらか Binary なら `BinaryNoticeDocument`(`FilesEqual` バイト比較)、両 Text は `LineDiff.Compute`→`ToHtmlDocument`(色=`ThemeManager.CurrentMarkdownColors()`)→`PreviewDir` へ `diff-{guid}.html`(`CleanupOldPages` で旧掃除)→ filer.preview 経由 Navigate。`OnWebViewMessage` close→Close / cycle-view→`CycleView`(全画面⇄ペイン領域 enum `ViewPlacement`)。`OnPreviewKeyDown` Esc/Enter=閉じる・F1=表示切替。WebView2 失敗はフォールバックせず MessageBox。
- `MainViewModel.ResolveDiffTargets()` — `Active.Marked`(FullPath)・`Left/Right.SelectedItemPath` を `DiffTargetResolver.Resolve` へ。
- `MainWindow.ShowDiff()`(BuildActions `["file.diff"]`) — 解決→成功なら `DiffWindow` を反対ペイン領域指定で ShowDialog、失敗は `MessageBox`(理由)。

## キー
`KeyBindings.cs` に `new("file.diff","2ファイルの差分を表示","ファイル操作",new[]{"Shift+C"},"差分")`。Shift+C 既定(file.copy="C" と非衝突)。設定(Z)で変更可。

## 制約 / 仕様
- **サイズ上限 10MB**(超過は案内表示)。文字コードは BOM/UTF-8/Shift-JIS を自動判定(内容検索 grep と同じ `TextEncodingDetector`)。BOM無し UTF-16・EUC-JP 等は非対応。
- バイナリ(NUL 含む)は「バイナリのため行差分は表示できません(同一/相違)」表示。書庫内ファイルは未対応(`DiffSource.Read` は実ファイル前提=zip 仮想パスは Resolver で「見つかりません」)。
- 語内強調は文字単位 LCS のため語(単語)単位ではない。
- LineDiff は prefix/suffix トリム方式のため、**大きいファイルで変更が全体に散在**する場合は中央積が上限を超えて全置換フォールバック(全行 Modified 表示)になりうる。精密化が必要なら Myers 差分への置換が将来課題。
