# サムネイル(グリッド)表示(Ctrl+G / view.toggleGrid)+サイズ切替(Ctrl+Shift+G)

ペインの一覧を**詳細表示 ⇔ サムネイル(グリッド)表示**で切り替える機能。表示モードは**ペインごと**に独立。画像は実サムネイル、フォルダー・その他ファイルは大きいアイコン(エクスプローラー同等)。グリッドの**タイルサイズも 通常 ⇔ 拡大(約2倍)で切替**(Ctrl+Shift+G、ペインごと)。`mem:architecture-overview` の補完。

## Core(src/Filer.Core、UI 非依存・TDD)
- `PaneViewMode.cs` — enum `{ Details, Grid }`(既定 Details)。
- `GridTileSize.cs` — enum `GridTileSize{Normal,Large}` + `GridTileMetrics`(`TileWidth`/`ImageSize`/`Next`)。Normal=タイル96/画像80、Large=タイル192/画像160(きっかり2倍)。XAML バインドと MainWindow の列数計算が同じ値を使うため一元化。テスト `GridTileMetricsTests` 4件。
- `GridNavigation.cs` — グリッドのカーソル移動計算。`GridDirection{Left,Right,Up,Down}` + `Move(count, columns, index, dir)`。
  - 左右=端で回り込み(`Left`: index>0?index-1:count-1 / `Right`: index<count-1?index+1:0)。
  - 上下=行単位。範囲外なら**留まる**(`Up`: index-columns>=0?…:index / `Down`: index+columns<count?…:index)。
  - count<=0→0、columns<1 は1扱い、index はクランプ。テスト `GridNavigationTests` 18件。
- 列数は表示幅依存なので Core は持たず、呼び出し側(MainWindow)が算出して渡す。

## App(src/Filer.App)
- `ShellThumbnailProvider.cs`(static) — Windows シェルのサムネイル取得。`IShellItemImageFactory::GetImage`(`SHCreateItemFromParsingName` で IShellItem 生成→QI)。フォルダーは `SIIGBF_ICONONLY`(中身覗きの遅延回避)、ファイルは既定(サムネイル→無ければアイコン)。HBITMAP→`Imaging.CreateBitmapSourceFromHBitmap`→Freeze→`DeleteObject`。
  - `TryGetCached(path,size,out img)`(同期即時表示用)/ `LoadAsync(path,isDirectory,size,onLoaded)`(`Task.Run`、`SemaphoreSlim`(CPU/2)で同時生成数を抑制、完了は UI Dispatcher で `onLoaded`)。
  - キャッシュ `ConcurrentDictionary<"path|size", ImageSource>`、上限 `CacheCap=2000` 超過で丸ごと Clear(性能最適化なので素朴で可)。
  - **実ファイル/実フォルダーのみ対象**(`File.Exists`/`Directory.Exists`)。書庫内の仮想パス・実在しないパスは何もしない=呼び出し側がアイコン表示のまま(フォールバックではなく対象外)。COMException は握って null。
- `EntryViewModel.Thumbnail`(ImageSource?) — グリッド用画像。`ThumbnailSize=160`(拡大タイル160pxでも鮮明にするため大きめに取得し、通常80pxは縮小表示=両サイズで同じキャッシュを共用)。生成済みなら即返し、未生成は**アイコン(`IconImage`)を仮表示**して `LoadAsync`、完了時 `OnPropertyChanged(Thumbnail)` で差し替え。`_thumbnailRequested` で二重要求防止。EntryViewModel は Refresh ごとに作り直されるためキャッシュは provider 側(static)に置く。
- `PaneViewModel`:
  - `[ObservableProperty] PaneViewMode ViewMode`(既定 Details)。`OnViewModeChanged` で `DetailsVisibility`/`GridVisibility` 通知。
  - `DetailsVisibility`/`GridVisibility`(`Visibility`、コンバーター不要)。`ToggleViewMode()`。
  - `[ObservableProperty] GridTileSize GridSize`(既定 Normal)+ `GridTileWidth`/`GridImageSize`(`GridTileMetrics` 由来。XAML バインド用)+ `ToggleGridSize()`。
  - **ViewMode/GridSize は Refresh で変更されない**=フォルダー移動・タブ切替後もモード/サイズ維持。**セッション永続化はしない**(再起動で Details/Normal に戻る)。

## UI(MainWindow.xaml / .cs)
- 各ペインの一覧領域を `<Grid>` で包み、**詳細リスト**(既存 `LeftList`/`RightList`、`Visibility={Binding DetailsVisibility}`)と**グリッド**(新 `LeftGrid`/`RightGrid`)を重ね、可視性で切替。
- グリッドは `local:PaneListView`(=ListView。View 指定なし=ListBox 相当)に `PaneGridStyle` 適用:
  - ItemsPanel=`WrapPanel`、`ScrollViewer.CanContentScroll=False`(WrapPanel に折り返し用ビューポート幅を与える)、横スクロール無効。
  - `GridTileTemplate`(StackPanel + `Image{Binding Thumbnail}` + `Name` 中央寄せ最大2行)。タイル幅・画像サイズは**ペイン VM の `GridTileWidth`/`GridImageSize` へ `RelativeSource={AncestorType=ListView}` でバインド**(DataTemplate の DataContext は EntryViewModel なので Git バッジ同様 ListView.DataContext 経由でペイン VM を参照)。サイズ切替で即反映。
  - `GridItemStyle`(ListViewItem): マーク=オレンジ背景+太字、ディレクトリ=`Entry.Directory` 色、**アクティブペインの選択タイルは `Accent` 枠**(詳細表示の下線に相当。MultiDataTrigger: IsSelected + DataContext.IsActive)。
- 両ビューとも `ItemsSource={Binding Entries}`・`SelectedIndex={Binding SelectedIndex, Mode=TwoWay}`(同じ VM 値を共有)・`GotKeyboardFocus=Pane_GotKeyboardFocus`。ドラッグ/ダブルクリックは ctor のループに `LeftGrid`/`RightGrid` を追加(`(ListView)sender` キャストは PaneListView が ListView 派生なので両対応)。
- `MainWindow.xaml.cs`:
  - `ListFor(isLeft)`=モードに応じて詳細 or グリッドのコントロールを返す。`ActiveList`/`FocusActiveList`/`ScrollActiveIntoView` がこれを使い、グリッド時はグリッド側へフォーカス/スクロール。`ActiveIsGrid`。
  - 列数/行数: タイル外形は**現在のサイズ依存**=`GridTileOuterWidth=Vm.Active.GridTileWidth+16` / `GridTileOuterHeight=Vm.Active.GridImageSize+54`(chrome: 余白/枠/パディング+画像上下余白+名前)。`GridColumns(grid)`=ActualWidth/タイル幅、`GridRows(grid)`=ActualHeight/タイル高さ(拡大時は列数が減り、移動の行幅も追従)。
  - キー分岐: `cursor.up/down`→`CursorVertical`(グリッドは `GridMoveCursor(Up/Down)` 行単位、詳細は `MoveCursorWrap`)。`pane.left/right`→グリッドなら `GridMoveCursor(Left/Right)`(端で回り込み)、詳細は従来(親移動/ペイン切替)。`cursor.pageUp/Down`→グリッドは `GridMovePage`(列×行)。**グリッド中は←→がタイル移動になる**(親移動は BS、ペイン切替は Tab で代替可能)。
  - `ToggleGridView`(`view.toggleGrid`)=`Active.ToggleViewMode()`→FocusActiveList→Loaded で ScrollActiveIntoView。`ToggleGridSize`(`view.gridSize`)=`Active.ToggleGridSize()`→Loaded で ScrollActiveIntoView。

## キー
`KeyBindings.cs` に `view.toggleGrid`(既定 Ctrl+G、フッター「サムネ表示」)と `view.gridSize`(既定 Ctrl+Shift+G、フッター「サムネ拡大」)。いずれも衝突なし・設定(Z)で変更可。フッターは Ctrl 押下時に表示。

## 制約 / 仕様 / 今後
- **グリッドは仮想化なし**(WrapPanel)。表示切替・移動時に全タイルのコンテナを生成するため、数万件のフォルダーでは切替が一瞬重い。通常(画像フォルダー数百〜数千)は問題なし。**VirtualizingWrapPanel 化が将来課題**。
- サムネイル生成は非同期+throttle+キャッシュで、詳細表示中は `Thumbnail` getter が呼ばれない(=オーバーヘッドなし。グリッドにした時だけ生成)。
- 書庫(.zip)内ファイルはサムネイル非対応(アイコン表示)。BOM 無し等の扱いは関係なし(画像はシェル任せ)。

## 検証(2026-06-14)
- 配布単一exe で `agent/tmp`(PNG 多数)を左ペイン Ctrl+G→グリッドで各 PNG の実サムネイル+フォルダーアイコン表示を確認。右ペインは詳細のまま=ペインごと独立。↓↓→ で [1/150]→[10/150](列数4: 2行+1)=行/列移動正常、選択タイルにアクセント枠を確認。
- Ctrl+Shift+G で拡大(4列→2列・タイル約2倍・画像も拡大して鮮明)→ ↓×5 で [11/150](2列: 5行)=行移動が列数に追従、選択維持。再度 Ctrl+Shift+G で通常へ戻ることも確認。`dotnet test` 608件 全成功。
