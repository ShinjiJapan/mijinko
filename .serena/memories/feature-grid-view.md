# サムネイル(グリッド)表示(Ctrl+G / view.toggleGrid)+サイズ切替(Ctrl+Shift+G)

ペインの一覧を**詳細表示 ⇔ サムネイル(グリッド)表示**で切り替える機能。表示モードは**ペインごと**に独立。画像は実サムネイル、フォルダー・その他ファイルは大きいアイコン(エクスプローラー同等)。グリッドの**タイルサイズは 通常 → 拡大(約2倍) → 特大(約4倍) を循環切替**(Ctrl+Shift+G、ペインごと)。`mem:architecture-overview` の補完。

## Core(src/Filer.Core、UI 非依存・TDD)
- `PaneViewMode.cs` — enum `{ Details, Grid }`(既定 Details)。
- `GridTileSize.cs` — enum `GridTileSize{Normal,Large,ExtraLarge}` + `GridTileMetrics`(`TileWidth`/`ImageSize`/`CellWidth`/`CellHeight`/`Next`)。Normal=タイル96/画像80、Large=タイル192/画像160、ExtraLarge=タイル384/画像320(各段きっかり2倍)。`Next` は Normal→Large→ExtraLarge→Normal の循環(switch 式)。**外形(コンテナ1個ぶん)**=`CellWidth=TileWidth+CellChromeWidth(16)` / `CellHeight=ImageSize+CellChromeHeight(6+32+16=54)`。chrome 定数は XAML(GridTileTemplate/GridItemStyle)実装と一致させる**唯一の出所**=仮想化パネル・MainWindow 列数計算・VM が全部これを使う。テスト `GridTileMetricsTests` 11件。
- `GridNavigation.cs` — グリッドのカーソル移動計算。`GridDirection{Left,Right,Up,Down}` + `Move(count, columns, index, dir)`。
  - 左右=端で回り込み(`Left`: index>0?index-1:count-1 / `Right`: index<count-1?index+1:0)。
  - 上下=行単位。範囲外なら**留まる**(`Up`: index-columns>=0?…:index / `Down`: index+columns<count?…:index)。
  - count<=0→0、columns<1 は1扱い、index はクランプ。テスト `GridNavigationTests` 18件。
- 列数は表示幅依存なので Core は持たず、呼び出し側(MainWindow)が算出して渡す。
- `GridVirtualization.cs` — グリッド仮想化のレイアウト計算(UI 非依存・縦スクロール専用・全タイル同寸前提)。`Columns(viewportWidth,itemWidth)`(最低1)/`RowCount(itemCount,columns)`/`ExtentHeight(itemCount,columns,itemHeight)`/`VisibleRange(offsetY,viewportHeight,itemHeight,columns,itemCount,bufferRows=1)`→見える添字範囲[First,Last](両端含む・前後bufferRows行先読み・空なら First>Last)。`VisibleRange` の lastRow は `Ceiling((offsetY+viewportHeight)/itemHeight)-1`(下端ちょうどでは次行を含めない)。App の `VirtualizingWrapPanel` が利用。テスト `GridVirtualizationTests` 9件。

## App(src/Filer.App)
- `ShellThumbnailProvider.cs`(static) — Windows シェルのサムネイル取得。`IShellItemImageFactory::GetImage`(`SHCreateItemFromParsingName` で IShellItem 生成→QI)。フォルダーは `SIIGBF_ICONONLY`(中身覗きの遅延回避)、ファイルは既定(サムネイル→無ければアイコン)。HBITMAP→`Imaging.CreateBitmapSourceFromHBitmap`→Freeze→`DeleteObject`。
  - `TryGetCached(path,size,out img)`(同期即時表示用)/ `LoadAsync(path,isDirectory,size,onLoaded)`(`Task.Run`、`SemaphoreSlim`(CPU/2)で同時生成数を抑制、完了は UI Dispatcher で `onLoaded`)。
  - キャッシュ `ConcurrentDictionary<"path|size", ImageSource>`、上限 `CacheCap=2000` 超過で丸ごと Clear(性能最適化なので素朴で可)。
  - **実ファイル/実フォルダーのみ対象**(`File.Exists`/`Directory.Exists`)。書庫内の仮想パス・実在しないパスは何もしない=呼び出し側がアイコン表示のまま(フォールバックではなく対象外)。COMException は握って null。
- `EntryViewModel.Thumbnail`(ImageSource?) — グリッド用画像。`ThumbnailSize=320`(特大タイル320pxでも鮮明にするため大きめに取得し、通常80px/拡大160pxは縮小表示=全サイズで同じキャッシュを共用)。生成済みなら即返し、未生成は**アイコン(`IconImage`)を仮表示**して `LoadAsync`、完了時 `OnPropertyChanged(Thumbnail)` で差し替え。`_thumbnailRequested` で二重要求防止。EntryViewModel は Refresh ごとに作り直されるためキャッシュは provider 側(static)に置く。
- `PaneViewModel`:
  - `[ObservableProperty] PaneViewMode ViewMode`(既定 Details)。`OnViewModeChanged` で `DetailsVisibility`/`GridVisibility` 通知。
  - `DetailsVisibility`/`GridVisibility`(`Visibility`、コンバーター不要)。`ToggleViewMode()`。
  - `[ObservableProperty] GridTileSize GridSize`(既定 Normal)+ `GridTileWidth`/`GridImageSize`/`GridCellWidth`/`GridCellHeight`(`GridTileMetrics` 由来。XAML バインド用)+ `ToggleGridSize()`(=`GridTileMetrics.Next` で循環)。
  - **ViewMode/GridSize は Refresh で変更されない**=フォルダー移動・タブ切替後もモード/サイズ維持。**セッション永続化はしない**(再起動で Details/Normal に戻る)。

## UI(MainWindow.xaml / .cs)
- 各ペインの一覧領域を `<Grid>` で包み、**詳細リスト**(既存 `LeftList`/`RightList`、`Visibility={Binding DetailsVisibility}`)と**グリッド**(新 `LeftGrid`/`RightGrid`)を重ね、可視性で切替。
- グリッドは `local:PaneListView`(=ListView。View 指定なし=ListBox 相当)に `PaneGridStyle` 適用:
  - ItemsPanel=`local:VirtualizingWrapPanel`(**UI 仮想化**)、`ScrollViewer.CanContentScroll=True`(項目単位スクロール=パネルの IScrollInfo に委譲)、`VirtualizingPanel.IsVirtualizing=True`、`VirtualizationMode=Recycling`、横スクロール無効。パネルの `CellWidth`/`CellHeight` を **VM の `GridCellWidth`/`GridCellHeight` へ `AncestorType=ListView` でバインド**。
  - `GridTileTemplate`(StackPanel + `Image{Binding Thumbnail}` + `Name` 中央寄せ)。名前欄は**固定高さ 32**(全タイル同寸)。タイル幅・画像サイズは**ペイン VM の `GridTileWidth`/`GridImageSize` へ `RelativeSource={AncestorType=ListView}` でバインド**(DataTemplate の DataContext は EntryViewModel なので ListView.DataContext 経由でペイン VM を参照)。サイズ切替で即反映。
- `Controls/VirtualizingWrapPanel.cs`(`VirtualizingPanel`+`IScrollInfo`、namespace `Filer.App`) — 同寸タイルを横並び折り返しで**見えている行だけ実体化**する仮想化パネル(縦スクロール専用)。レイアウト計算は `GridVirtualization`(Core)へ委譲。
  - **タイル寸法 `_itemSize` はバインドされた `CellWidth`/`CellHeight`(=Core の `GridTileMetrics.Cell*`)から決める。コンテナ実測は絶対にしない**。理由:`Image{Stretch=Uniform}` を無制約 Measure すると、サイズバインドが一瞬外れた隙にサムネイル本来サイズへ膨張して `_itemSize` が乱高下→列数/extent/可視範囲が毎パス変化→**measure 無限ループ(1スクロールで数百回 measure・CPU 全コア張り付き・点滅・操作不能)**になった。固定値採用で安定。
  - `BringIndexIntoView`/`MakeVisible` でキー移動の ScrollIntoView に対応。`MakeVisible` は**移動後オフセット基準の可視矩形を返す**(入力矩形のまま返すと再呼び出しループ)。`SetVerticalOffset` はデッドゾーンを設けない(要求値=報告値、ドラッグ点滅防止)。`_isMeasuring` 中は bring-into-view を無視(実体化が誘発する RequestBringIntoView の再スクロール抑止)。
  - **実体化の前に `MeasureOverride` 内で最新 extent/viewport にオフセットをクランプ**(最下部ジャンプ直後の寸法未確定 SetVerticalOffset が下端を行き過ぎ→最終行がビューポート上に外れて空白になる不具合を防ぐ)。
  - 効果:数万件フォルダーでも**見えている範囲だけがサムネイル要求を出す**=後方ファイルもその行までスクロールすれば即要求・表示(従来は全件一斉要求で FIFO 末尾が長時間待ち)。
  - `GridItemStyle`(ListViewItem): マーク=オレンジ背景+太字、ディレクトリ=`Entry.Directory` 色、**アクティブペインの選択タイルは `Accent` 枠**(詳細表示の下線に相当。MultiDataTrigger: IsSelected + DataContext.IsActive)。
- 両ビューとも `ItemsSource={Binding Entries}`・`SelectedIndex={Binding SelectedIndex, Mode=TwoWay}`(同じ VM 値を共有)・`GotKeyboardFocus=Pane_GotKeyboardFocus`。ドラッグ/ダブルクリックは ctor のループに `LeftGrid`/`RightGrid` を追加(`(ListView)sender` キャストは PaneListView が ListView 派生なので両対応)。
- `MainWindow.xaml.cs`:
  - `ListFor(isLeft)`=モードに応じて詳細 or グリッドのコントロールを返す。`ActiveList`/`FocusActiveList`/`ScrollActiveIntoView` がこれを使い、グリッド時はグリッド側へフォーカス/スクロール。`ActiveIsGrid`。
  - 列数/行数: タイル外形は `GridTileOuterWidth=Vm.Active.GridCellWidth` / `GridTileOuterHeight=Vm.Active.GridCellHeight`(=Core の `GridTileMetrics.Cell*`。仮想化パネルと同一値)。`GridColumns(grid)`=ActualWidth/外形幅、`GridRows(grid)`=ActualHeight/外形高さ(拡大・特大時は列数が減り、移動の行幅も追従)。
  - キー分岐: `cursor.up/down`→`CursorVertical`(グリッドは `GridMoveCursor(Up/Down)` 行単位、詳細は `MoveCursorWrap`)。`pane.left/right`→グリッドなら `GridMoveCursor(Left/Right)`(端で回り込み)、詳細は従来(親移動/ペイン切替)。`cursor.pageUp/Down`→グリッドは `GridMovePage`(列×行)。**グリッド中は←→がタイル移動になる**(親移動は BS、ペイン切替は Tab で代替可能)。
  - `ToggleGridView`(`view.toggleGrid`)=`Active.ToggleViewMode()`→FocusActiveList→Loaded で ScrollActiveIntoView。`ToggleGridSize`(`view.gridSize`)=`Active.ToggleGridSize()`→Loaded で ScrollActiveIntoView。

## キー
`KeyBindings.cs` に `view.toggleGrid`(既定 Ctrl+G、フッター「サムネ表示」)と `view.gridSize`(既定 Ctrl+Shift+G、フッター「サムネ拡大」)。いずれも衝突なし・設定(Z)で変更可。フッターは Ctrl 押下時に表示。

## 制約 / 仕様 / 今後
- **グリッドは UI 仮想化済み**(`VirtualizingWrapPanel`)。見えている行のみコンテナ生成=数万件フォルダーでも切替・スクロールが軽く、サムネイル要求も表示中の範囲だけ。全タイル同寸(名前欄固定高32)が前提。
- サムネイル生成は非同期+throttle+キャッシュで、詳細表示中は `Thumbnail` getter が呼ばれない(=オーバーヘッドなし。グリッドにした時だけ生成)。
- 書庫(.zip)内ファイルはサムネイル非対応(アイコン表示)。BOM 無し等の扱いは関係なし(画像はシェル任せ)。

## 検証(2026-06-14)
- 配布単一exe で `agent/tmp`(PNG 多数)を左ペイン Ctrl+G→グリッドで各 PNG の実サムネイル+フォルダーアイコン表示を確認。右ペインは詳細のまま=ペインごと独立。↓↓→ で [1/150]→[10/150](列数4: 2行+1)=行/列移動正常、選択タイルにアクセント枠を確認。
- Ctrl+Shift+G で拡大(4列→2列・タイル約2倍・画像も拡大して鮮明)→ ↓×5 で [11/150](2列: 5行)=行移動が列数に追従、選択維持。再度 Ctrl+Shift+G で通常へ戻ることも確認。`dotnet test` 608件 全成功。
- 2026-06-15: 特大(ExtraLarge=タイル384/画像320)を追加し循環を 通常→拡大→特大→通常 に変更。`ThumbnailSize` を 160→320 に引き上げ(特大でも鮮明)。`GridTileMetricsTests` 11件 全成功。
