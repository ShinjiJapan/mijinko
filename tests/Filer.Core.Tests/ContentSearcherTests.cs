using System.Text;
using Filer.Core;

namespace Filer.Core.Tests;

public sealed class ContentSearcherTests : IDisposable
{
    private readonly string _root;

    public ContentSearcherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilerGrep_" + Guid.NewGuid().ToString("N"));
        // root/
        //   a.txt   : alpha / beta / Gamma
        //   b.log   : Beta at start / nothing
        //   sub/c.cs: class C {} /   // beta
        //   notes.md: no match here
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "alpha\nbeta\nGamma\n");
        File.WriteAllText(Path.Combine(_root, "b.log"), "Beta at start\nnothing\n");
        File.WriteAllText(Path.Combine(_root, "sub", "c.cs"), "class C {}\n  // beta\n");
        File.WriteAllText(Path.Combine(_root, "notes.md"), "no match here\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ContentSearchOptions Options(string query) => new(query, _root);

    // ---- 行一致判定 ----

    [Fact]
    public void LineMatcher_Substring_IgnoresCaseByDefault()
    {
        Assert.True(ContentSearcher.TryCreateLineMatcher("beta", useRegex: false, caseSensitive: false,
            out var matcher, out _));
        Assert.True(matcher("Beta at start"));
        Assert.True(matcher("a BETA line"));
        Assert.False(matcher("gamma"));
    }

    [Fact]
    public void LineMatcher_CaseSensitive_WhenSet()
    {
        Assert.True(ContentSearcher.TryCreateLineMatcher("Beta", useRegex: false, caseSensitive: true,
            out var matcher, out _));
        Assert.True(matcher("Beta at start"));
        Assert.False(matcher("beta lower"));
    }

    [Fact]
    public void LineMatcher_Empty_ReturnsError()
    {
        Assert.False(ContentSearcher.TryCreateLineMatcher("", useRegex: false, caseSensitive: false,
            out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void LineMatcher_Regex_UsedWhenEnabled()
    {
        Assert.True(ContentSearcher.TryCreateLineMatcher(@"^beta", useRegex: true, caseSensitive: false,
            out var matcher, out _));
        Assert.True(matcher("Beta at start"));
        Assert.False(matcher("  // beta"));   // 行頭でないので不一致
    }

    [Fact]
    public void LineMatcher_InvalidRegex_ReturnsFalseWithError()
    {
        Assert.False(ContentSearcher.TryCreateLineMatcher("[", useRegex: true, caseSensitive: false,
            out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    // ---- 検索本体 ----

    [Fact]
    public void Search_FindsFilesContaining_WithRelativeNameAndLineNumbers()
    {
        var matches = ContentSearcher.Search(Options("beta"));

        var names = matches.Select(m => m.Entry.Name).ToArray();
        Assert.Equal(new[] { "a.txt", "b.log", @"sub\c.cs" }, names);

        var a = matches.Single(m => m.Entry.Name == "a.txt");
        var line = Assert.Single(a.Lines);
        Assert.Equal(2, line.LineNumber);
        Assert.Equal("beta", line.Text);

        var c = matches.Single(m => m.Entry.Name == @"sub\c.cs");
        Assert.Equal(2, c.Lines.Single().LineNumber);
        Assert.Contains("beta", c.Lines.Single().Text);
    }

    [Fact]
    public void Search_CollectsAllMatchingLinesInAFile()
    {
        File.WriteAllText(Path.Combine(_root, "many.txt"), "beta\nx\nbeta\nbeta\n");

        var match = ContentSearcher.Search(Options("beta")).Single(m => m.Entry.Name == "many.txt");

        Assert.Equal(new[] { 1, 3, 4 }, match.Lines.Select(l => l.LineNumber).ToArray());
    }

    [Fact]
    public void Search_NamePattern_FiltersTargetFiles()
    {
        var options = Options("beta") with { NamePattern = "*.cs" };

        var matches = ContentSearcher.Search(options);

        var match = Assert.Single(matches);
        Assert.Equal(@"sub\c.cs", match.Entry.Name);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(ContentSearcher.Search(Options("不在の文字列ZZZ")));
    }

    [Fact]
    public void Search_CaseSensitive_ExcludesOtherCasing()
    {
        var options = Options("Beta") with { CaseSensitive = true };

        var matches = ContentSearcher.Search(options);

        var match = Assert.Single(matches);
        Assert.Equal("b.log", match.Entry.Name);   // 「Beta」を含むのは b.log のみ
    }

    [Fact]
    public void Search_Regex_MatchesLineStart()
    {
        var options = Options("^beta") with { UseRegex = true };

        var names = ContentSearcher.Search(options).Select(m => m.Entry.Name).ToArray();

        Assert.Equal(new[] { "a.txt", "b.log" }, names);   // 行頭beta(大小無視)。"  // beta" は除外
    }

    [Fact]
    public void Search_SkipsBinaryFiles()
    {
        // NUL を含む=バイナリ扱いで対象外(中身に "beta" があっても拾わない)
        File.WriteAllBytes(Path.Combine(_root, "bin.dat"),
            new byte[] { 0x62, 0x65, 0x74, 0x61, 0x00, 0x62, 0x65, 0x74, 0x61 });

        var matches = ContentSearcher.Search(Options("beta"));

        Assert.DoesNotContain(matches, m => m.Entry.Name == "bin.dat");
    }

    [Fact]
    public void Search_SkipsFilesOverMaxSize()
    {
        File.WriteAllText(Path.Combine(_root, "big.txt"), new string('x', 2000) + "\nbeta\n");
        var options = Options("beta") with { MaxFileSize = 500 };

        var matches = ContentSearcher.Search(options);

        Assert.DoesNotContain(matches, m => m.Entry.Name == "big.txt");
        Assert.Contains(matches, m => m.Entry.Name == "a.txt");   // 小さいファイルは検索される
    }

    [Fact]
    public void Search_MaxMatchesPerFile_CapsCollectedLines()
    {
        File.WriteAllText(Path.Combine(_root, "many.txt"), "beta\nbeta\nbeta\nbeta\n");
        var options = Options("beta") with { MaxMatchesPerFile = 2 };

        var match = ContentSearcher.Search(options).Single(m => m.Entry.Name == "many.txt");

        Assert.Equal(2, match.Lines.Count);
        Assert.Equal(new[] { 1, 2 }, match.Lines.Select(l => l.LineNumber).ToArray());
    }

    [Fact]
    public void Search_TruncatesLongLines()
    {
        File.WriteAllText(Path.Combine(_root, "long.txt"), "beta" + new string('z', 100));
        var options = Options("beta") with { MaxLineLength = 10 };

        var match = ContentSearcher.Search(options).Single(m => m.Entry.Name == "long.txt");

        Assert.True(match.Lines.Single().Text.Length <= 11);   // 切り詰め + 省略記号
    }

    [Fact]
    public void Search_ReportsMatchesViaCallback()
    {
        var collected = new List<ContentMatch>();
        var matches = ContentSearcher.Search(Options("beta"), default,
            m => { lock (collected) collected.Add(m); });

        Assert.Equal(
            matches.Select(m => m.Entry.FullPath).OrderBy(p => p),
            collected.Select(m => m.Entry.FullPath).OrderBy(p => p));
    }

    [Fact]
    public void Search_EmptyQuery_Throws()
    {
        Assert.Throws<ArgumentException>(() => ContentSearcher.Search(Options("")));
    }

    [Fact]
    public void Search_AlreadyCancelled_ReturnsEmpty()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Empty(ContentSearcher.Search(Options("beta"), cts.Token));
    }

    [Fact]
    public void SearchWithInfo_ReportsEnumerationEngine()
    {
        var result = ContentSearcher.SearchWithInfo(Options("beta"));

        Assert.Equal(FileSearchEngine.DirectoryScan, result.Engine);   // 候補列挙は通常走査
        Assert.NotEmpty(result.Matches);
    }

    // ---- エンコーディング判定 ----

    private static Encoding ShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    [Fact]
    public void Search_FindsJapanese_InShiftJisFile()
    {
        File.WriteAllBytes(Path.Combine(_root, "sjis.txt"),
            ShiftJis().GetBytes("一行目\r\nこれは検索テストです\r\n"));

        var match = ContentSearcher.Search(Options("検索")).Single(m => m.Entry.Name == "sjis.txt");

        Assert.Equal(2, match.Lines.Single().LineNumber);
        Assert.Contains("検索", match.Lines.Single().Text);
    }

    [Fact]
    public void Search_FindsJapanese_InUtf8NoBomFile()
    {
        File.WriteAllText(Path.Combine(_root, "u8.txt"), "あいう\n検索ワード\n", new UTF8Encoding(false));

        var match = ContentSearcher.Search(Options("検索")).Single(m => m.Entry.Name == "u8.txt");

        Assert.Equal(2, match.Lines.Single().LineNumber);
    }

    [Fact]
    public void Search_FindsJapanese_InUtf16LeBomFile()
    {
        File.WriteAllText(Path.Combine(_root, "u16.txt"), "first\n検索行\n", new UnicodeEncoding(false, true));

        var match = ContentSearcher.Search(Options("検索")).Single(m => m.Entry.Name == "u16.txt");

        Assert.Equal(2, match.Lines.Single().LineNumber);
    }

    [Fact]
    public void Search_ShiftJisNotMisreadAsUtf8()
    {
        File.WriteAllBytes(Path.Combine(_root, "sjis2.txt"), ShiftJis().GetBytes("日本語の文章\r\n"));

        var matches = ContentSearcher.Search(Options("日本語"));

        Assert.Contains(matches, m => m.Entry.Name == "sjis2.txt");
    }
}
