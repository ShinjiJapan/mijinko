using Filer.Core;

namespace Filer.Core.Tests;

/// <summary>全画面メモ(反対ペイン)のテキスト永続化を一時ファイルで検証する。</summary>
public sealed class MemoStoreTests : IDisposable
{
    private readonly string _file;

    public MemoStoreTests()
    {
        _file = Path.Combine(Path.GetTempPath(), "FilerMemo_" + Guid.NewGuid().ToString("N"), "memo.txt");
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_file)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_OnMissingFile_ReturnsEmpty()
    {
        Assert.Equal("", new MemoStore(_file).Load());
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var store = new MemoStore(_file);
        store.Save("買い物リスト\n- 卵\n- 牛乳");

        Assert.Equal("買い物リスト\n- 卵\n- 牛乳", store.Load());
    }

    [Fact]
    public void Save_OverwritesPrevious()
    {
        var store = new MemoStore(_file);
        store.Save("古い内容");
        store.Save("新しい内容");

        Assert.Equal("新しい内容", store.Load());
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var store = new MemoStore(_file);
        store.Save("テスト");

        Assert.True(File.Exists(_file));
    }

    [Fact]
    public void Memo_PersistsAcrossInstances()
    {
        new MemoStore(_file).Save("永続テキスト");

        Assert.Equal("永続テキスト", new MemoStore(_file).Load());
    }

    [Fact]
    public void Save_EmptyString_ClearsContent()
    {
        var store = new MemoStore(_file);
        store.Save("何か");
        store.Save("");

        Assert.Equal("", store.Load());
    }
}
