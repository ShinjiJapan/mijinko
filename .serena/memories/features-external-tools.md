# 外部ツール連携(ユーザー定義リスト)

固定の VSCode/Git Bash 等のハードコードを廃止し、**ユーザーが自由に追加・削除・キー割り当てできる外部ツールリスト**に作り変えた。既定として VSCode(V)/Windows Terminal(G)/Git Bash(B)/SkimDown(Shift+K)の4ツールを初回投入。

## モデル(Core)
- `ExternalTool`(record, `src/Filer.Core/ExternalTool.cs`): `Id`(安定識別子=キーバインド参照キー)/`Label`(表示名)/`Kind`(`ExternalToolKind.Executable`|`StoreApp`)/`Target`(実行ファイルパス or AUMID)/`Arguments`(引数マクロテンプレート)/`Gestures`(割り当てキー)。
- `ExternalTools.Defaults()`: 既定4ツール。`ExternalTools.SkimDownAumid`=`45014okazuki.SkimDown_r82gs1ecy8g7c!App`。
- 各ツールは `tool:<Id>` というキーバインドアクションになる(`KeyBindingActions.ForTool(tool)`/`ToolIdOf`/`ToolPrefix`)。組み込み `KeyBindingActions.All` にはツールを入れない(`tool.` も `tool:` も持たない=テスト `BuiltInActions_DoNotContainToolActions` で保証)。**過去に tool.skimdown が組み込みに残って孤児バインド・二重行になったので注意**。

## 引数マクロ(だいなファイラー風・`src/Filer.Core/ToolMacros.cs`)
`ToolMacroExpander.Expand(template, ToolMacroContext)` → 展開文字列(`$MO` が空でキャンセルされたら null)。`ExpandToSinglePath` はストアアプリ用に最初の1パス抽出。
- 単一値(そのまま挿入・引用は利用者がテンプレに書く): `$F`名前 / `$W`拡張子除く名前 / `$E`拡張子 / `$P`自窓パス / `$O`他窓 / `$L`左窓 / `$R`右窓(末尾`\`なし)。
- 複数値(各項目を`"`で囲み空白連結): `$MS`マーク名一覧 / `$MF`マークのフルパス一覧(共にマーク無→カーソル1つ) / `$MO`他方マークのフルパス(**無ければコマンド自体キャンセル=null**) / `$mO`同(無ければ空文字)。`$$`=リテラル`$`。未知`$X`はそのまま。
- 2文字マクロ判定は1文字マクロより先。境界 `i+2 < length` でOK(末尾の3文字マクロも拾える。テスト済)。`ToolMacroExpanderTests` 13件。
- `ToolMacroContext` は MainViewModel.BuildMacroContext が構築: `$F`はカーソル名(".."は空)、`$MS/$MF`は `pane.Targets`(MarkedOrCurrent)→空(カーソル".."時)はフォルダー名/現在フォルダーにfallback、`$MO/$mO`は他方ペインの `pane.Marked`(=PaneState.MarkedEntries、マークのみ・fallbackなし)。`$P`等は `TrimDir`(末尾\除去)。

## 起動(App)
- `ExternalToolLauncher.Launch(ExternalTool tool, ToolMacroContext ctx)`(`src/Filer.App/ExternalTools/ExternalToolLauncher.cs`):
  - **Executable**: `Expand`→null(キャンセル)なら何もしない。`ResolveExecutable(Target)`: 絶対パスは**実在必須**(無ければ FileNotFoundException)、名前だけ(例 Code.exe/wt.exe/git-bash.exe)は `KnownLocations` 辞書から解決→無ければ null=シェル経由(`UseShellExecute=true`)。解決できた(=`UseShellExecute=false`)場合は `ChildProcessEnvironment.Scrub(psi.Environment)` で `ELECTRON_RUN_AS_NODE` を除去(VSCode統合ターミナルから起動された際にCode.exeがNode実行へ化けるのを防ぐ。known-issues参照)。`Process.Start(psi)?.Dispose()`(ハンドル即解放)。
  - **StoreApp**: `ExpandToSinglePath`→null なら何もしない→`UwpLauncher.Open(aumid, path)`。
- `MainViewModel.LaunchTool(toolId)`: ツール検索→`EnsureNotInArchive(Active.DirectoryPath)`(書庫内拒否)→`_tools.Launch(tool, BuildMacroContext())`。`OpenSelectedWithAssociation`(Shift+Enter関連付け)は残存。旧 OpenSelectedInVSCode 等は削除。
- `UwpLauncher.cs`: 変更なし。ProgID 経由 ShellExecuteEx か ActivateForFile。SkimDown 等の単一インスタンスビューア対策(GUIプロセスから直接アクティベーション)。
- ディスパッチ: `MainWindow.RebuildKeyBindings` が `KeyBindingMap.Build(overrides, tools.Select(ForTool))` で対応表を作り、`_actions`(=`_builtinActions`コピー+各 `tool:<id>`→`Run(()=>Vm.LaunchTool(id))`)を再構築。設定変更後にも呼ぶ。

## インストール済みアプリ列挙
- `InstalledAppLister.List()`(`src/Filer.App/ExternalTools/InstalledAppLister.cs`): `Shell.Application` COM の `NameSpace("shell:AppsFolder").Items()` を列挙し各 item の `Name`+`Path`(=AUMID)を `InstalledApp(Name, Aumid)` で返す(`Get-StartApps` 相当、実機281件確認)。AUMID重複排除・名前順。COM(shell/folder/items/各item)は `FinalReleaseComObject` で解放。
- 列挙は時間がかかるため `AppPickerDialog` が**STAスレッド**で実行(`Thread.SetApartmentState(STA)`+TaskCompletionSource)。Win32アプリの Path はexeパスになる点に注意(ピッカー用途では問題なし)。

## 設定UI(`SettingsDialog` 外部ツールタブ + `ToolEditDialog` + `AppPickerDialog`)
- **外部ツールタブ**(ListView: ラベル/種類/対象/キー + 追加/編集/複製/削除/↑↓): `_tools`(List<ExternalTool>)が真実、`_map`(KeyBindingMap)は導出。ツール変更で `RebuildMap`(=`BuildMap(_map.ToOverrides())` で組み込み上書きを保ち再構築)+`SyncToolGesturesFromMap`(解決後ジェスチャを_toolsへ書戻し)。
- **ToolEditDialog**: ラベル/種類ラジオ/対象(Executable=パス+参照[OpenFileDialog]、StoreApp=AUMID+「アプリを選択...」[AppPickerDialog])/引数テンプレ(「マクロ一覧 ?」でMessageBox全マクロ)/キー(キャプチャ方式・`ConflictLookup`で重複確認)。`ToolDraft`を返す(Idは呼び出し側=SettingsDialogが`GenerateId`でスラッグ生成)。
- **キー割り当てタブ**: `_map.Actions`(組み込み+ツール)を反映。ツールは「外部ツール」カテゴリに出る。キー変更は `_map.Replace`→`SyncToolGesturesFromMap`。
- **既定キー復活**(レビュー指摘修正): ツール削除/キー変更でツールから外れたキー(`freed`)は `RestoreDefaultOwners`で、現在未割り当てかつ組み込みの既定に持つ操作へ `ResetToDefault`。削除=`ToolDelete_Click`、編集=`EditToolAt`が `existing.Gestures \ draft.Gestures` を freed として渡す。
- OK: `new AppSettings(_map.ToOverrides(), _tools)`→`MainViewModel.UpdateSettings`(保存+読直し)→`RebuildKeyBindings`で即時反映。
- 起動チェーン: Z→設定→外部ツールタブ→編集→ストアアプリ→アプリを選択→AppPicker(281件表示)まで実機E2E確認済み。キー付きカスタムツールの起動+マクロ展開($P/$F)もマーカーファイルで確認。

## 永続化
- `%APPDATA%\Filer\settings.json` の `externalTools`(配列)。`AppSettingsStore`。`kind`は文字列("Executable"/"StoreApp")。`externalTools` キー無し→既定4ツール補完、空配列→「ツール無し」尊重。`keyBindings` は既定と異なる組み込み操作のみ(`tool:`は除外=ツール定義側にジェスチャを持つ)。

## 注意
- T / Shift+T は外部ツールではなく組み込みターミナル(architecture-overview参照)。
- 動的ツールが「明示上書き済み組み込み」とジェスチャ衝突する場合、`Build` の steal は非explicitのみ剥がすため Reindex定義順(組み込みが先勝ち)。UI経路は OwnerOf+Replace で明示的に奪うため通常発生せず、手編集 settings.json でのみ顕在化(低severity・未対応)。
