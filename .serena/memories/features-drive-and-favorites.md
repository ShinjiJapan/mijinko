# ドライブ移動・お気に入り機能

## キー
- **L**: ドライブ選択 UI(`SelectionDialog`)→ 選択したドライブのルートへアクティブ側を移動。**ドライブ文字キー(A〜Z)で即移動**(letterSelect モード)。
- **A**: お気に入り登録。**カーソルがサブフォルダー上ならそのフォルダー、ファイル/".." 上なら現在のフォルダー**が対象(`Vm.FavoriteTargetPath`)。`FavoriteEditDialog`(登録モード)でラベル+グループ+パスを指定して OK で登録。グループ欄は自由入力(「仕事/CLI」の / 区切りで階層自動作成・空=ルート直下)+▼ボタンで既存グループ(ContextMenu)から選択。重複パスは登録されず情報ダイアログ表示。A→Enter の2打でルート直下へ即登録。
- **1**(Key.D1 / Key.NumPad1): お気に入り選択 UI → 選択フォルダーへ移動。**グループ階層対応(だいなファイラー風)**: グループ行は名前+「›」表示、Enter/→/クリックで中へ、←/BS で1つ上へ、プロンプトにパンくず(`基本プロンプト  [仕事 › CLI]`)。番号 1〜9 は階層ごとに振り直し。各項目・グループに編集/削除ボタン。未登録時は案内ダイアログ。
- 選択系ダイアログ(ドライブ/お気に入り/履歴)は**シングルクリックで決定**(グループ行はクリックで中へ)。

## タブ・ペイン切替キー(MainWindow.OnPreviewKeyDown)
- **Tab**: アクティブペイン切替(`Vm.SwitchPane`)。
- **Ctrl+← / Ctrl+→**: アクティブ側の前/次タブへ(`active.ActivatePrevTab/ActivateNextTab`)。※旧 Ctrl+Tab/Ctrl+Shift+Tab は廃止。
- **Ctrl+T / Ctrl+W**: タブ追加 / アクティブタブを閉じる(`active.AddTab/CloseActiveTab`)。
- 修飾なし ←→: ペイン外側方向=ペイン移動、内側方向=親フォルダー(左:←親/→右、右:→親/←左)。

## Core 層(UI 非依存・テスト済み)
- `FavoriteNode(string Path, string Label, IReadOnlyList<FavoriteNode>? Children = null)`(record・`src/Filer.Core/FavoritesStore.cs`): ツリーの1ノード。**項目**=Path+Label(Children=null)、**グループ**=Label+Children(Path=空)。`IsGroup => Children is not null`。
- `FavoritesStore`(同ファイル): お気に入り階層ツリーを JSON 永続化。グループは「仕事/CLI」形式の**グループパス**で指定(`GroupSeparator`='/'。グループ名に / は使用不可)。同名グループは同一階層で統合・自動作成。項目パスは**ツリー全体で大小無視の重複排除**。
  - `GetTree()→IReadOnlyList<FavoriteNode>` / `GetGroupPaths()→IReadOnlyList<string>`(深さ優先) / `Add(path, label="", group="")→bool`(重複は false) / `Update(old, new, label, group)`(同グループなら位置保持・別グループは移動先末尾。対象無し・**newPath が別項目と重複なら何もしない**) / `Remove(path)`(空グループは残す) / `RenameGroup(groupPath, newName)→bool`(空・/含み・同階層同名は false) / `RemoveGroup(groupPath)`(子孫ごと削除) / `JoinGroup(parent, name)`(public static。App 層も使用)。
  - JSON: 項目=`{"Path","Label"}`、グループ=`{"Label","Children":[...]}`(Path/Children は null 時非出力)。**旧形式自動移行**: 文字列配列・Children なしフラット配列はルート直下の項目として読み、次回 Save で新形式へ。内部は可変 `Node` クラス+`NodeDto` で再帰読み書き。日本語非エスケープ・ディレクトリ自動作成は従来どおり。
- `DriveLister`/`IDriveProvider`/`DriveItem`(`src/Filer.Core/DriveLister.cs`): `DriveInfo.GetDrives()` ラップ。未準備は IsReady=false・サイズ0で含める。
- `PaneState.NavigateTo(path)` / `PaneState.TargetFolderPath`: 従来どおり。

## App 層
- `SelectionEntry(Display, Value, Children=null)`(`SelectionDialog.xaml.cs`): Children 付きはグループ。`ChevronVisibility`(グループ行の › 表示、DataTemplate からバインド)。
- `SelectionDialog.xaml(.cs)`: 階層ドリルダウン対応。`_root`+`_groupStack`/`_indexStack`(進入時の選択位置を記憶し戻り時に復元)。`ActivateSelection()`=グループなら `EnterGroup`(番号付きクローンでなく元エントリを Value 一致で積む)/項目なら Commit。→はグループ選択時のみ・←/BS は階層内のみ Handled。`RefreshList()` が番号振り直し+パンくずプロンプト更新。編集/削除後は `ReloadKeepingLocation()` で reload して **Value で階層を辿り直す**(グループ名変更で Value が変わった階層はその手前=親へ戻る仕様)。HelpText は階層有無で「→:グループを開く ←/BS:戻る」を出し分け(`ContainsGroup` 再帰判定)。numbered/letterSelect/シングルクリック決定は従来どおり。
- `FavoriteEditDialog.xaml(.cs)`: **登録/編集兼用**。`(title, path, label, group, groups)` の5引数。ラベル/グループ(TextBox+▼ボタン→既存グループの ContextMenu。`GroupPickButton.IsEnabled=groups.Count>0`)/パスの3フィールド。`GroupText`=Trim+前後の / 除去。パス空は OK 不可。
- `MainViewModel`: `GetFavoritesTree()` / `GetFavoriteGroups()` / `FavoriteTargetPath` / `AddFavorite(path,label,group)→bool` / `UpdateFavorite(old,new,label,group)` / `RemoveFavorite(path)` / `RenameFavoriteGroup(groupPath,newName)→bool` / `RemoveFavoriteGroup(groupPath)`。旧 `GetFavorites()`/`AddFavorite()`/3引数 Update は廃止。
- `MainWindow`: `FavoriteGroupPrefix="group:"`(SelectionEntry.Value でグループを表す接頭辞。項目は素のパス)。`BuildFavoriteEntries`(再帰。項目=`"ラベル  (パス)"`orパス、グループ=名前+Children+Value=`group:仕事/CLI`)/`FindFavorite`(ツリーをパスで再帰検索→(項目, グループパス))/`EditFavorite`(グループ→`InputDialog` で名前変更、失敗時警告。項目→`FavoriteEditDialog`、**パス変更先が既存項目と重複なら警告して中止**)/`DeleteFavorite`(グループ→「中身ごと削除」確認、項目→従来確認)。`AddFavoriteInteractive` は登録ダイアログ経由(成功時の MessageBox は廃止=ダイアログ自体が確認)。
- `App.xaml.cs`: お気に入りパス = `%APPDATA%\Filer\favorites.json`(変更なし)。

## テスト
`FavoritesStoreTests`(26: 階層・移動・リネーム・移行・重複ガード含む)。Core 合計 254 件。SelectionDialog のドリルダウンは UIA スモークテストで確認済み(agent/tmp/verify-fav2.ps1 方式: favorites.json バックアップ→テストグループ注入→1キー→→/←/数字キー→復元)。
