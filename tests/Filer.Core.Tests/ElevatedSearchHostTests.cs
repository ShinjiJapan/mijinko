using Filer.Core;
using static Filer.Core.SearchIpcProtocol;

namespace Filer.Core.Tests;

public sealed class ElevatedSearchHostTests
{
    private static FileEntry Entry(string name) =>
        new(name, @"C:\base\" + name, false, 1, new DateTime(2024, 1, 1));

    private static byte[] RequestPayload() =>
        ToJsonUtf8(new SearchRequestDto("x", @"C:\base", false, true, false, false));

    [Fact]
    public async Task Request_StreamsBatches_ThenDone()
    {
        var (hostSide, clientSide) = DuplexStream.CreatePair();

        ElevatedSearchHost.SearchEngine engine = (_, _, onBatch) =>
        {
            onBatch!(new[] { Entry("a.txt") });
            onBatch(new[] { Entry("b.txt") });
            return new FileSearchResult(
                new[] { Entry("a.txt"), Entry("b.txt") }, FileSearchEngine.MftIndex, "MFT索引(差分更新)");
        };

        var host = new ElevatedSearchHost(hostSide, engine);
        var runTask = host.RunAsync();

        await WriteMessageAsync(clientSide, MessageType.Request, RequestPayload());

        var m1 = await ReadMessageAsync(clientSide);
        var m2 = await ReadMessageAsync(clientSide);
        var m3 = await ReadMessageAsync(clientSide);

        Assert.Equal(MessageType.Batch, m1!.Type);
        Assert.Equal("a.txt", FromJsonUtf8<FileEntryDto[]>(m1.Payload).Single().Name);
        Assert.Equal(MessageType.Batch, m2!.Type);
        Assert.Equal("b.txt", FromJsonUtf8<FileEntryDto[]>(m2.Payload).Single().Name);

        Assert.Equal(MessageType.Done, m3!.Type);
        var done = FromJsonUtf8<DoneDto>(m3.Payload);
        Assert.Equal(nameof(FileSearchEngine.MftIndex), done.Engine);
        Assert.Equal("MFT索引(差分更新)", done.Note);
        Assert.Equal(2, done.Count);

        clientSide.Dispose();
        await runTask;
    }

    [Fact]
    public async Task Engine_Throws_SendsError()
    {
        var (hostSide, clientSide) = DuplexStream.CreatePair();

        ElevatedSearchHost.SearchEngine engine = (_, _, _) => throw new InvalidOperationException("boom");
        var host = new ElevatedSearchHost(hostSide, engine);
        var runTask = host.RunAsync();

        await WriteMessageAsync(clientSide, MessageType.Request, RequestPayload());

        var msg = await ReadMessageAsync(clientSide);
        Assert.Equal(MessageType.Error, msg!.Type);
        Assert.Equal("boom", FromJsonUtf8<ErrorDto>(msg.Payload).Message);

        clientSide.Dispose();
        await runTask;
    }

    [Fact]
    public async Task Cancel_DuringSearch_StopsSearch_AndCompletes()
    {
        var (hostSide, clientSide) = DuplexStream.CreatePair();

        var started = new ManualResetEventSlim();
        ElevatedSearchHost.SearchEngine engine = (_, token, onBatch) =>
        {
            started.Set();
            // キャンセルされるまで待つ(本来の検索が中断点で打ち切られるのを模す)。
            while (!token.IsCancellationRequested)
                Thread.Sleep(5);
            onBatch!(new[] { Entry("partial.txt") });
            return new FileSearchResult(
                new[] { Entry("partial.txt") }, FileSearchEngine.DirectoryScan, "中断");
        };

        var host = new ElevatedSearchHost(hostSide, engine);
        var runTask = host.RunAsync();

        await WriteMessageAsync(clientSide, MessageType.Request, RequestPayload());
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        // 検索ストリーミング中に Cancel を送る(別タスクが読みつつ受け取る)。
        await WriteMessageAsync(clientSide, MessageType.Cancel, ReadOnlyMemory<byte>.Empty);

        // 中断後の partial バッチ → Done が来る。
        var m1 = await ReadMessageAsync(clientSide);
        var m2 = await ReadMessageAsync(clientSide);
        Assert.Equal(MessageType.Batch, m1!.Type);
        Assert.Equal("partial.txt", FromJsonUtf8<FileEntryDto[]>(m1.Payload).Single().Name);
        Assert.Equal(MessageType.Done, m2!.Type);

        clientSide.Dispose();
        await runTask;
    }

    [Fact]
    public async Task PipeClosed_EndsRunLoop()
    {
        var (hostSide, clientSide) = DuplexStream.CreatePair();
        var host = new ElevatedSearchHost(hostSide, (_, _, _) =>
            new FileSearchResult(Array.Empty<FileEntry>(), FileSearchEngine.DirectoryScan, null));
        var runTask = host.RunAsync();

        clientSide.Dispose();   // 本体終了(パイプ切断)を模す

        // 例外なく終了する。
        await runTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
