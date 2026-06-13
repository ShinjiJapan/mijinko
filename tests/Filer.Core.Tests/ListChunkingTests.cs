using Filer.Core;

namespace Filer.Core.Tests;

public class ListChunkingTests
{
    [Fact]
    public void FirstChunkCount_SmallList_ReturnsAll()
    {
        // チャンクサイズ以下は分割の意味がないので全件。
        Assert.Equal(100, ListChunking.FirstChunkCount(totalCount: 100, cursorIndex: 0, chunkSize: 128));
    }

    [Fact]
    public void FirstChunkCount_SlightlyOverChunk_ReturnsAll()
    {
        // 2倍以下の分割は描画2回のオーバーヘッドの方が大きいので全件。
        Assert.Equal(200, ListChunking.FirstChunkCount(totalCount: 200, cursorIndex: 0, chunkSize: 128));
    }

    [Fact]
    public void FirstChunkCount_LargeList_ReturnsChunk()
    {
        Assert.Equal(128, ListChunking.FirstChunkCount(totalCount: 7000, cursorIndex: 0, chunkSize: 128));
    }

    [Fact]
    public void FirstChunkCount_CursorBeyondChunk_ReturnsAll()
    {
        // カーソル復元位置がチャンク外なら分割しない(部分表示中に選択不能になるため)。
        Assert.Equal(7000, ListChunking.FirstChunkCount(totalCount: 7000, cursorIndex: 500, chunkSize: 128));
    }

    [Fact]
    public void FirstChunkCount_CursorJustInsideChunk_ReturnsChunk()
    {
        Assert.Equal(128, ListChunking.FirstChunkCount(totalCount: 7000, cursorIndex: 127, chunkSize: 128));
    }

    [Fact]
    public void FirstChunkCount_ZeroItems_ReturnsZero()
    {
        Assert.Equal(0, ListChunking.FirstChunkCount(totalCount: 0, cursorIndex: 0, chunkSize: 128));
    }
}
