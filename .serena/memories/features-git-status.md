# Git ステータス連携(状態バッジ+ブランチ表示)

ファイル一覧でリポジトリ配下の変更を**バッジ**で示し、パンくず(フォルダパス)直後に現在ブランチを `(branch)` 形式で表示する機能。`git status --porcelain=v2 --branch -z` を1回起動して取得。

## Core(`src/Filer.Core/GitStatus.cs`, テスト `GitStatusTests.cs` 21件)
- `GitEntryState`(enum) — None/Untracked/Added/Modified/Conflicted。**値の大小=ディレクトリ集約時の優先度**(Conflicted が最強)。
- `GitStatusSnapshot` — 1リポジトリ分。`Branch`/`Ahead`/`Behind`、`BranchDisplay`(例 "main ↑1 ↓2"。ahead/behind は非0のみ付与・両0や未取得は空)、`StateOf(relativePath, isDirectory)`(\\→/ 正規化・前後 / トリム・大小無視。ファイルとディレクトリで別辞書を引く)。
- `GitStatusParser.Parse(output)` — NUL区切り出力を解析。`# branch.head`→ブランチ名、`# branch.ab +A -B`→ahead=A/behind=B。種別 `1`(通常変更。XY=`A.` のみ Added、他は Modified)、`2`(リネーム。パスは固定9/10フィールド目を `Split(' ', n)` で取得。**次の NUL トークンがリネーム元なのでループ index を1進めて読み飛ばす**)、`u`(unmerged→Conflicted)、`?`(untracked)、`!`(ignored=対象外)。`Register` で項目登録+全祖先ディレクトリへ `Merge`(優先度の高い状態で上書き)。未追跡ディレクトリは `dir/` と末尾スラッシュで1件報告される→ディレクトリ辞書へ。
- `GitRepositoryLocator.FindRoot(startDir, gitMarkerExists)` — startDir から親へ辿り `.git` を持つ最初のフォルダーを返す。存在判定は注入(テスト可)。

## App
- `GitStatusService`(`src/Filer.App/GitStatusService.cs`) — `QueryAsync(directoryPath)`→`Result(RepositoryRoot, Snapshot)?`。`GitRepositoryLocator.FindRoot`(`.git` の Directory/File 両対応)→git.exe を `--no-optional-locks status --porcelain=v2 --branch -z`(UTF-8・CreateNoWindow)で起動。ExitCode≠0 は null。**git 未インストール(Win32Exception)時は静的 `_gitUnavailable` を立てて以後プロセス起動を省略**(フォールバックではなく機能無効化)。
- `EntryViewModel`:
  - `GitState`(ObservableProperty)。`OnGitStateChanged` で派生プロパティ3つへ通知。
  - **バッジ表示プロパティ**: `GitBadge`(M/A/?/! の1文字、None は空)、`GitBadgeBrush`(背景色。**テーマ非依存の固定色**=Modified `#2F7BD6` 青/Added `#3FA34D` 緑/Untracked `#7A7A7A` 灰/Conflicted `#D13438` 赤。static Freeze 済み共有 Brush、None は Transparent)、`HasGitBadge`。文字は白固定。
- `PaneViewModel`:
  - `GitBranchText`(ObservableProperty) — パンくず直後に出すブランチ文字列。`RefreshGitStatus` で `BranchDisplay` が非空なら `$"({branch})"`、空/リポジトリ外は空文字。**括弧は VM 側で付与**(空のとき "()" にならず Style の空文字 Collapsed 判定を保つため)。
  - `Refresh()` 末尾で `RefreshGitStatus()`(async void。世代カウンタ `_gitGeneration` で移動が重なったら古い結果を破棄)。`Directory.Exists` ごと `Task.Run` でバックグラウンド実行(検索仮想一覧・書庫内・ネットワークパスで固まらせない)。
  - `ApplyGitStates()` — 取得済み `_gitResult` を表示中の全行へ適用。`Path.GetRelativePath(RepositoryRoot, CurrentPath)` でフォルダー接頭辞を作り `prefix + vm.Name` で照合。".." と未取得・リポジトリ外は None。2段階表示(chunked)の追加分にも BeginInvoke 内で再適用。

## UI(MainWindow.xaml)
- **行の Foreground 色分けは廃止**(テーマによりディレクトリ色と被るため。特に Beige はディレクトリが茶でGit暖色と区別困難だった)。代わりに**名前列セル内のバッジ**で表示。
- `NameCellTemplate`(DataTemplate) — 名前列の `CellTemplate`。`Grid`(ColumnDefinitions: 固定19px + *)で、カラム0に角丸 Border バッジ(`GitBadgeBrush` 背景・`GitBadge` 白文字・Width/Height 15)、カラム1に `DisplayName` の TextBlock。**バッジ枠は固定幅で常に確保**(状態なしは透明背景+空文字で見えない→名前の開始位置は全行で揃う)。名前 TextBlock の文字色は ListViewItem からの**継承**(ディレクトリ色・マーク色)に従う。配置は アイコン列 → 名前列(バッジ+名前)の順。
- 名前列は `DisplayMemberBinding` をやめ `CellTemplate="{StaticResource NameCellTemplate}"` に(Width 219)。両ペイン共通。
- **ブランチ表示の配置**: パンくずバー(左右各 DockPanel)で、Breadcrumb の ItemsControl と `GitBranchTextStyle` の TextBlock を **StackPanel(Orientation=Horizontal)** にまとめ、パス直後にブランチ `(master)` が並ぶよう配置(MarkSummary は Dock=Right で右端のまま)。`GitBranchTextStyle` は `Git.Branch` 色・空文字なら Visibility=Collapsed・左 Margin 4。**注意: WrapPanel でまとめるとブランチが折り返して隠れる(VMの値は正しいのに非表示になる)→ StackPanel 必須**。
- テーマ色 `Git.Branch` のみ全7テーマに残す(**Git.Modified/Added/Untracked/Conflicted のテーマブラシは廃止**=バッジ固定色に統一)。

## 検証
- 実機(Beige テーマ)で**左右両ペイン**をリポジトリ配下(左=filer, 右=filer\src)にして、両方ともパス直後に紫 `(master)` が出ることをピクセル測定(各ペイン紫53px)+画像で確認。バッジは .serena/src=青M・tests=灰?・.gitignore/CLAUDE.md=青M、位置=アイコン(x29)→バッジ(x58, 名前列左端)→名前。`git status` 実出力と一致。

## 引き継ぎ / 状態(2026-06-13 時点)
- **全変更は未コミット**(working tree)。`dotnet test` 377件 全成功。`publish/` へ Release 発行済み(デプロイ済み)。
- 検証で `%APPDATA%\Filer\session.json` を書き換えた(左=filer, 右=filer\src)。実害なし(次回起動時の表示が変わるだけ)。
- **PrintWindow/GDI+ キャプチャが不安定**(画像 Read が空/壊れることが多い)。対策: `MemoryStream`→`File.WriteAllBytes` で保存、ピクセル判定はライブ Bitmap 上で直接行うと安定。色判定の固定色基準値は本メモリ App 節を参照。
- **次の機能候補**(memo/機能追加.MD に一覧): Git 差分プレビュー、フォルダー比較、一括リネーム、ファイル内容 grep など。※ごみ箱送り(D=ごみ箱/Shift+D=完全削除)は実装済み。Git 連携の書き換え系(stage/commit)は意図的に未実装(ターミナル/外部ツールに委譲する方針)。
