# 高速検索(管理者権限)— 常駐昇格ヘルパー方式

非管理者で起動した Filer から、**本体を再起動も昇格もせず** MFT 直読み(Everything 方式)の高速検索を使う仕組み。設計は `memo/filer-管理者権限検索-設計.md`。

## 方式
最初の高速検索でだけ昇格ヘルパー(別 exe を増やさず `Filer.App.exe --mft-search-server <pipe> <親PID>`)を `runas`(UAC 1回)で起動し、以降は常駐させ MFT 索引(USN 差分)をプロセス内に温存。本体は標準権限・再起動なし。2回目以降は UAC なしで既存パイプ再利用。**暗黙の通常走査フォールバックはしない**(失敗理由を UI 表示)。

## パイプの向き(重要)
**本体=パイプサーバー(中IL)/ ヘルパー=パイプクライアント(高IL)**。高IL クライアント→中IL サーバー接続は常に許可される(逆は SACL ラベル降格が必要で煩雑)。検索の実体は昇格側だがサーバーは本体側に置き整合性問題を回避。

## ファイル
- `src/Filer.Core/Mft/SearchIpcProtocol.cs`(Core) — フレーム `[int32 len(LE)][byte type][UTF-8 JSON payload]`(len は payload のみ)。`MessageType`{Request=0x01, Cancel=0x02, Batch=0x10, Done=0x11, Error=0x12}。`Message`(Type/Payload)。`WriteMessage`(同期・onBatch 直列化用)/`WriteMessageAsync`/`ReadMessageAsync`(終端=切断で **null** を返す。例外を投げない)。`ToJsonUtf8`/`FromJsonUtf8`(System.Text.Json, `JsonSerializerDefaults.General`=PascalCase・case-sensitive)。DTO=`SearchRequestDto`(6項目。`FromOptions`/`ToOptions`=**PreferMft は転送せずヘルパー側で常に true**)/`FileEntryDto`(`FromEntry`/`ToEntry`、IsArchive 込み)/`DoneDto`(Engine/Note/Count)/`ErrorDto`(Message)。
- `src/Filer.Core/Mft/ElevatedSearchHost.cs`(Core) — ヘルパー側ループ。`SearchEngine` delegate(`(options, token, onBatch)=>FileSearchResult`。本番は `FileSearcher.SearchWithInfo` 注入、テストは偽関数)。`RunAsync`=Request 受信で別タスク検索開始(直前の検索は cancel+await で逐次化)・**検索中も読み続け Cancel で CTS.Cancel**・パイプ切断(ReadMessageAsync→null)で自己終了。発見は onBatch→Batch 送出(`_writeLock` で直列化)、完了で Done、例外で Error。Done は件数だけ(エントリ再送なし=proxy がバッチを集約)。
- `src/Filer.App/Mft/Program.cs`(App) — カスタムエントリ `[STAThread] Main`。引数 `--mft-search-server <pipe> <ppid>` ならヘッドレスで `NamedPipeClientStream` 接続(15秒)→`ElevatedSearchHost.RunAsync`。それ以外は `new App(); app.InitializeComponent(); app.Run()`。親 PID を `Process.Exited` 監視し本体終了で `Environment.Exit`(孤児防止)。
- `src/Filer.App/Mft/ElevatedSearchProxy.cs`(App) — 本体側。`SearchAsync(options, onBatch, token)`=接続確保→Request→Batch を onBatch+集約→Done でソート済み `FileSearchResult` 返却。token キャンセルで Cancel 送出。`EnsureConnectedAsync`=未接続/ヘルパー死亡時に GUID パイプ名で `NamedPipeServerStream` を立て runas でヘルパー起動・接続待ち(30秒)。`ElevationDeclinedException`(UAC 拒否=Win32 1223)。切断は IOException、ホスト Error は InvalidOperationException(理由付き)。`Dispose`=パイプ閉鎖→ヘルパー自己終了。
- `src/Filer.App/Filer.App.csproj` — `<StartupObject>Filer.App.Program</StartupObject>` + App.xaml を `ApplicationDefinition Remove`→`Page Include`(自動生成 Main を無効化し InitializeComponent だけ生成)。
- `src/Filer.App/FileSearchDialog.xaml(.cs)` — **専用の高速検索ボタンは無い**。唯一の「検索開始」(`Search_Click`)が proxy 非 null(=非管理者+設定 ON)なら `StartSearchAsync(elevated:true)`、null なら `false`。内容検索(grep)は proxy の有無によらず常にインプロセス(`StartContentSearchAsync` へ分岐)。`StartSearchAsync(elevated)`→`RunSearchAsync(options, elevated)` で分岐。`ElevationDeclinedException` は理由表示のみ。コンストラクタ `FileSearchDialog(baseDir, ElevatedSearchProxy? proxy=null)`。
- `src/Filer.App/MainWindow.xaml.cs` — `GetOrCreateSearchProxy()`(管理者判定 `WindowsPrincipal.IsInRole(Administrator)`、管理者なら null=ボタン出さない。**さらに設定 `AppSettings.EnableElevatedFastSearch`(既定 true)が false なら非管理者でも null を返し、既存の常駐ヘルパーは `Dispose` で終了させる**。有効時は単一 `ElevatedSearchProxy` を使い回しヘルパー常駐)。`ShowFileSearchDialog` で都度呼び dialog へ渡す(設定変更が即反映)。`OnClosed` で `_searchProxy?.Dispose()`。
- 設定:`AppSettings.EnableElevatedFastSearch`(`src/Filer.Core/AppSettingsStore.cs`、JSON `enableElevatedFastSearch`、欠落時 true)。設定ダイアログ `SettingsDialog.xaml` の「検索」タブ `EnableFastSearchCheck` で ON/OFF。OK で即時反映(再起動不要)。管理者起動時はこの設定によらず常に高速検索。
- app.manifest は**変更なし**(asInvoker 維持=本体は標準権限)。

## テスト
- `tests/Filer.Core.Tests/SearchIpcProtocolTests.cs`(フレーム往復・EOF=null・DTO/ドメイン変換、6件)
- `tests/Filer.Core.Tests/ElevatedSearchHostTests.cs`(Request→Batch→Done・Error・**Cancel 中断**・切断終了、4件)
- `tests/Filer.Core.Tests/DuplexStream.cs`(テスト用メモリ全二重ストリーム対=`ByteChannel` 2本。読み書き別スレッド同時可・Dispose で相手 Read が 0)
- 実昇格・UAC・MFT は手動確認。非昇格 IPC 往復は `agent/tmp/ipc-smoke.ps1` で検証済み(Request→Batch×N→Done、非管理者なら Note=「MFT: 管理者権限がないため通常走査」で DirectoryScan にフォールバック、切断でヘルパー code=0 終了)。

詳細な MFT 索引本体は `mem:architecture-overview` の「MFT検索(Everything方式)」を参照。
