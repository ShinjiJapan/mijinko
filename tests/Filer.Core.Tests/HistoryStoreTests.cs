using Filer.Core;

namespace Filer.Core.Tests;

/// <summary>フォルダー履歴(MRU)の永続化を一時ファイルで検証する。</summary>
public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _file;

    public HistoryStoreTests()
    {
        _file = Path.Combine(Path.GetTempPath(), "FilerHist_" + Guid.NewGuid().ToString("N"), "history.json");
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_file)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void GetAll_OnMissingFile_ReturnsEmpty()
    {
        Assert.Empty(new HistoryStore(_file).GetAll());
    }

    [Fact]
    public void Add_PutsNewestFirst()
    {
        var store = new HistoryStore(_file);
        store.Add(@"C:\a");
        store.Add(@"C:\b");

        Assert.Equal(new[] { @"C:\b", @"C:\a" }, store.GetAll());
    }

    [Fact]
    public void Add_ExistingPath_MovesToFront_CaseInsensitive()
    {
        var store = new HistoryStore(_file);
        store.Add(@"C:\a");
        store.Add(@"C:\b");
        store.Add(@"c:\A");   // 既存を先頭へ(大文字小文字無視)。重複は作らない

        Assert.Equal(new[] { @"c:\A", @"C:\b" }, store.GetAll());
    }

    [Fact]
    public void Add_BeyondMax_DropsOldest()
    {
        var store = new HistoryStore(_file, maxEntries: 3);
        store.Add(@"C:\1");
        store.Add(@"C:\2");
        store.Add(@"C:\3");
        store.Add(@"C:\4");   // 上限3 → 最古の C:\1 を捨てる

        Assert.Equal(new[] { @"C:\4", @"C:\3", @"C:\2" }, store.GetAll());
    }

    [Fact]
    public void Add_BlankPath_Ignored()
    {
        var store = new HistoryStore(_file);
        store.Add("   ");
        store.Add("");

        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void History_PersistsAcrossInstances()
    {
        new HistoryStore(_file).Add(@"C:\a");

        Assert.Equal(new[] { @"C:\a" }, new HistoryStore(_file).GetAll());
    }
}
