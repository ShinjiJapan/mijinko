using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using Filer.Core;
using static Filer.Core.SearchIpcProtocol;

namespace Filer.App;

/// <summary>UAC で管理者権限が承認されなかった(ユーザーが拒否した)。</summary>
public sealed class ElevationDeclinedException : Exception
{
    public ElevationDeclinedException()
        : base("管理者権限が承認されなかったため高速検索は使えません") { }
}

/// <summary>
/// 本体(標準権限)側。初回の高速検索でのみ昇格ヘルパーを <c>runas</c> で起動し、
/// 名前付きパイプ(本体=サーバー)で繋いだまま常駐させる(MFT 索引を温存)。
/// 2回目以降は既存パイプを再利用するため UAC は出ない。
/// パイプの向きは「本体=サーバー(中IL)/ ヘルパー=クライアント(高IL)」=高IL→中IL 接続で整合性問題を回避。
/// </summary>
public sealed class ElevatedSearchProxy : IDisposable
{
    private readonly string _exePath;
    private readonly object _gate = new();

    private NamedPipeServerStream? _pipe;
    private Process? _helper;
    private bool _disposed;

    /// <summary>本体 exe のパス。既定は現在のプロセス(Filer.App.exe)。</summary>
    public ElevatedSearchProxy(string? exePath = null)
    {
        _exePath = exePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("実行ファイルのパスを取得できません。");
    }

    /// <summary>
    /// 昇格ヘルパー経由で検索する。<paramref name="onBatch"/> は発見バッチごとに呼ばれ、
    /// 完了時にソート済みの全結果(と使用エンジン・補足)を返す。
    /// 失敗時は理由付きで例外を投げる(暗黙の通常走査フォールバックはしない)。
    /// </summary>
    public async Task<FileSearchResult> SearchAsync(FileSearchOptions options,
        Action<IReadOnlyList<FileEntry>> onBatch, CancellationToken token)
    {
        var pipe = await EnsureConnectedAsync(token).ConfigureAwait(false);

        await WriteMessageAsync(pipe, MessageType.Request,
            ToJsonUtf8(SearchRequestDto.FromOptions(options)), CancellationToken.None).ConfigureAwait(false);

        // 結果ストリーミング中にキャンセルされたら Cancel を送る(読みとは別タスク=トークン登録)。
        using var cancelRegistration = token.Register(() =>
        {
            try { lock (_gate) WriteMessage(pipe, MessageType.Cancel, ReadOnlySpan<byte>.Empty); }
            catch { /* 既に切断/完了 */ }
        });

        var entries = new List<FileEntry>();
        while (true)
        {
            var message = await ReadMessageAsync(pipe, CancellationToken.None).ConfigureAwait(false);
            if (message is null)
            {
                Reset();
                throw new IOException("高速検索ヘルパーとの接続が切断されました。");
            }

            switch (message.Type)
            {
                case MessageType.Batch:
                    var batch = Array.ConvertAll(
                        FromJsonUtf8<FileEntryDto[]>(message.Payload), d => d.ToEntry());
                    entries.AddRange(batch);
                    onBatch(batch);
                    break;

                case MessageType.Done:
                    var done = FromJsonUtf8<DoneDto>(message.Payload);
                    entries.Sort(static (a, b) =>
                        string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    var engine = done.Engine == nameof(FileSearchEngine.MftIndex)
                        ? FileSearchEngine.MftIndex : FileSearchEngine.DirectoryScan;
                    return new FileSearchResult(entries, engine, done.Note);

                case MessageType.Error:
                    throw new InvalidOperationException(FromJsonUtf8<ErrorDto>(message.Payload).Message);
            }
        }
    }

    /// <summary>
    /// 接続を確保する。未接続(初回 or ヘルパー異常終了後)なら runas でヘルパーを起動し接続を待つ。
    /// </summary>
    private async Task<NamedPipeServerStream> EnsureConnectedAsync(CancellationToken token)
    {
        if (_pipe is { IsConnected: true } existing && _helper is { HasExited: false })
            return existing;

        Reset();

        var pipeName = "filer-mftsearch-" + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        try
        {
            _helper = Process.Start(new ProcessStartInfo
            {
                FileName = _exePath,
                Arguments = $"{Program.MftSearchServerArg} {pipeName} {Environment.ProcessId}",
                UseShellExecute = true,
                Verb = "runas",   // UAC 昇格(別プロセス)
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)   // ERROR_CANCELLED
        {
            pipe.Dispose();
            throw new ElevationDeclinedException();
        }
        catch
        {
            pipe.Dispose();
            throw;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await pipe.WaitForConnectionAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            pipe.Dispose();
            throw new TimeoutException("高速検索ヘルパーが接続しませんでした。");
        }
        catch
        {
            pipe.Dispose();
            throw;
        }

        _pipe = pipe;
        return pipe;
    }

    /// <summary>パイプを閉じる(ヘルパーは読み取り失敗で自己終了する)。ヘルパープロセスは常駐のまま。</summary>
    private void Reset()
    {
        lock (_gate)
        {
            _pipe?.Dispose();
            _pipe = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
        try { _helper?.Dispose(); } catch { /* ハンドル解放のみ */ }
        _helper = null;
    }
}
