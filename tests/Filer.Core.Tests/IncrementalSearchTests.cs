using Filer.Core;

namespace Filer.Core.Tests;

public class IncrementalSearchTests
{
    private static FileEntry Dir(string name) => new(name, @"C:\test\" + name, true, 0, default);
    private static FileEntry File(string name) => new(name, @"C:\test\" + name, false, 1, default);

    private static IReadOnlyList<FileEntry> Entries() => new[]
    {
        FileEntry.Parent(@"C:\"),       // 0: ".."
        Dir("docs"),                    // 1
        Dir("Maker"),                   // 2
        File("make_rag_pdf.py"),        // 3
        File("readme.md"),              // 4
        File("マニュアル.md"),           // 5
        File("Map.txt"),                // 6
    };

    // ---- FindFrom: 開始位置から下方向(開始位置含む・回り込み)で最初の一致 ----

    [Fact]
    public void FindFrom_前方一致で最初の一致を返す()
    {
        Assert.Equal(2, IncrementalSearch.FindFrom(Entries(), "ma", prefixOnly: true, startIndex: 0));
    }

    [Fact]
    public void FindFrom_開始位置自身も一致対象になる()
    {
        Assert.Equal(3, IncrementalSearch.FindFrom(Entries(), "make", prefixOnly: true, startIndex: 3));
    }

    [Fact]
    public void FindFrom_開始位置より後ろを優先し末尾を超えたら先頭へ回り込む()
    {
        // index 4 から "ma" を探すと 6 (Map.txt)、先頭へ回り込んで 2 ではない。
        Assert.Equal(6, IncrementalSearch.FindFrom(Entries(), "ma", prefixOnly: true, startIndex: 4));
        // index 7 相当(末尾超え)は存在しないため startIndex は末尾までで使う。
        Assert.Equal(2, IncrementalSearch.FindFrom(Entries(), "maker", prefixOnly: true, startIndex: 4));
    }

    [Fact]
    public void FindFrom_大文字小文字を無視する()
    {
        Assert.Equal(2, IncrementalSearch.FindFrom(Entries(), "MAKER", prefixOnly: true, startIndex: 0));
    }

    [Fact]
    public void FindFrom_部分一致モードでは名前の途中も一致する()
    {
        Assert.Equal(3, IncrementalSearch.FindFrom(Entries(), "rag", prefixOnly: false, startIndex: 0));
    }

    [Fact]
    public void FindFrom_前方一致モードでは名前の途中は一致しない()
    {
        Assert.Equal(-1, IncrementalSearch.FindFrom(Entries(), "rag", prefixOnly: true, startIndex: 0));
    }

    [Fact]
    public void FindFrom_日本語名も検索できる()
    {
        Assert.Equal(5, IncrementalSearch.FindFrom(Entries(), "マニュ", prefixOnly: true, startIndex: 0));
    }

    [Fact]
    public void FindFrom_親エントリは一致対象にしない()
    {
        Assert.Equal(-1, IncrementalSearch.FindFrom(Entries(), "..", prefixOnly: true, startIndex: 0));
    }

    [Fact]
    public void FindFrom_空クエリは一致なし()
    {
        Assert.Equal(-1, IncrementalSearch.FindFrom(Entries(), "", prefixOnly: true, startIndex: 0));
        Assert.Equal(-1, IncrementalSearch.FindFrom(Entries(), "  ", prefixOnly: true, startIndex: 0));
    }

    [Fact]
    public void FindFrom_空一覧は一致なし()
    {
        Assert.Equal(-1, IncrementalSearch.FindFrom(Array.Empty<FileEntry>(), "a", prefixOnly: true, startIndex: 0));
    }

    // ---- FindNext: 現在位置を除いた次/前の一致(回り込み) ----

    [Fact]
    public void FindNext_下方向は現在位置の次から探す()
    {
        Assert.Equal(3, IncrementalSearch.FindNext(Entries(), "ma", prefixOnly: true, currentIndex: 2, backward: false));
        Assert.Equal(6, IncrementalSearch.FindNext(Entries(), "ma", prefixOnly: true, currentIndex: 3, backward: false));
    }

    [Fact]
    public void FindNext_下方向は末尾を超えたら先頭へ回り込む()
    {
        Assert.Equal(2, IncrementalSearch.FindNext(Entries(), "ma", prefixOnly: true, currentIndex: 6, backward: false));
    }

    [Fact]
    public void FindNext_上方向は現在位置の前から探す()
    {
        Assert.Equal(3, IncrementalSearch.FindNext(Entries(), "ma", prefixOnly: true, currentIndex: 6, backward: true));
    }

    [Fact]
    public void FindNext_上方向は先頭を超えたら末尾へ回り込む()
    {
        Assert.Equal(6, IncrementalSearch.FindNext(Entries(), "ma", prefixOnly: true, currentIndex: 2, backward: true));
    }

    [Fact]
    public void FindNext_一致が1件だけなら同じ位置へ戻る()
    {
        Assert.Equal(4, IncrementalSearch.FindNext(Entries(), "readme", prefixOnly: true, currentIndex: 4, backward: false));
    }

    [Fact]
    public void FindNext_一致なしはマイナス1()
    {
        Assert.Equal(-1, IncrementalSearch.FindNext(Entries(), "zzz", prefixOnly: true, currentIndex: 0, backward: false));
        Assert.Equal(-1, IncrementalSearch.FindNext(Entries(), "", prefixOnly: true, currentIndex: 0, backward: false));
    }
}
