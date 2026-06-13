using System.Linq;

namespace Filer.Core;

using static SearchIpcProtocol;

/// <summary>
/// 昇格ヘルパー側の検索ループ。名前付きパイプ(duplex)で本体と繋がり、
/// <see cref="MessageType.Request"/> を受けて検索を実行、発見を <see cref="MessageType.Batch"/> で
/// 逐次返し、完了で <see cref="MessageType.Done"/>(失敗で <see cref="MessageType.Error"/>)を送る。
/// 検索中も読み取りを続け、<see cref="MessageType.Cancel"/> で実行中検索を中断する。
/// 索引(<see cref="MftSearchService"/> の static キャッシュ)はプロセス常駐で温存される。
/// </summary>
public sealed class ElevatedSearchHost
{
    /// <summary>
    /// 検索処理。本番は <see cref="FileSearcher.SearchWithInfo"/> を渡す。
    /// テストでは偽の検索関数を注入する(<see cref="FileSearcher"/> を直呼びしない)。
    /// </summary>
    public delegate FileSearchResult SearchEngine(
        FileSearchOptions options, CancellationToken token,
        Action<IReadOnlyList<FileEntry>>? onBatch);

    private readonly Stream _stream;
    private readonly SearchEngine _engine;
    private readonly object _writeLock = new();

    private CancellationTokenSource? _searchCts;
    private Task _searchTask = Task.CompletedTask;

    public ElevatedSearchHost(Stream stream, SearchEngine engine)
    {
        _stream = stream;
        _engine = engine;
    }

    /// <summary>
    /// メッセージループ。パイプが切断(本体終了)されるまで Request/Cancel を処理し続ける。
    /// </summary>
    public async Task RunAsync(CancellationToken hostToken = default)
    {
        try
        {
            while (true)
            {
                var message = await ReadMessageAsync(_stream, hostToken).ConfigureAwait(false);
                if (message is null) break;   // パイプ切断 → 自己終了

                switch (message.Type)
                {
                    case MessageType.Request:
                        await BeginSearchAsync(message.Payload, hostToken).ConfigureAwait(false);
                        break;
                    case MessageType.Cancel:
                        _searchCts?.Cancel();
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // hostToken のキャンセルによる正常終了。
        }
        finally
        {
            _searchCts?.Cancel();
            try { await _searchTask.ConfigureAwait(false); } catch { /* 中断中の例外は無視 */ }
        }
    }

    /// <summary>直前の検索を中断・待機してから、新しい検索を別タスクで開始する(逐次実行)。</summary>
    private async Task BeginSearchAsync(byte[] payload, CancellationToken hostToken)
    {
        _searchCts?.Cancel();
        try { await _searchTask.ConfigureAwait(false); } catch { /* 中断分は無視 */ }

        var cts = _searchCts = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        var options = FromJsonUtf8<SearchRequestDto>(payload).ToOptions();
        _searchTask = Task.Run(() => ExecuteSearch(options, cts.Token));
    }

    /// <summary>検索を実行し、発見バッチ→完了(または失敗)を送る。読み取りループとは別スレッドで動く。</summary>
    private void ExecuteSearch(FileSearchOptions options, CancellationToken token)
    {
        try
        {
            var result = _engine(options, token, batch => SendBatch(batch));
            Send(MessageType.Done,
                ToJsonUtf8(new DoneDto(result.Engine.ToString(), result.EngineNote, result.Entries.Count)));
        }
        catch (Exception ex)
        {
            Send(MessageType.Error, ToJsonUtf8(new ErrorDto(ex.Message)));
        }
    }

    private void SendBatch(IReadOnlyList<FileEntry> batch)
    {
        if (batch.Count == 0) return;
        var dtos = batch.Select(FileEntryDto.FromEntry).ToArray();
        Send(MessageType.Batch, ToJsonUtf8(dtos));
    }

    /// <summary>フレーム送出を直列化する(onBatch は複数ワーカーから並行に呼ばれるため)。</summary>
    private void Send(MessageType type, byte[] payload)
    {
        lock (_writeLock)
            WriteMessage(_stream, type, payload);
    }
}
