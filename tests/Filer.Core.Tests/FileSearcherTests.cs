using System.IO.Compression;
using Filer.Core;

namespace Filer.Core.Tests;

public sealed class FileSearcherTests : IDisposable
{
    private readonly string _root;

    public FileSearcherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilerSearch_" + Guid.NewGuid().ToString("N"));
        // root/
        //   readme.md  notes.txt  MD-list.txt
        //   docs/  (dir)
        //     guide.md  image.png
        //     sub/  (dir)
        //       deep.MD
        //   mdfolder/  (dir, 名前に "md" を含む)
        Directory.CreateDirectory(Path.Combine(_root, "docs", "sub"));
        Directory.CreateDirectory(Path.Combine(_root, "mdfolder"));
        File.WriteAllText(Path.Combine(_root, "readme.md"), "a");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "b");
        File.WriteAllText(Path.Combine(_root, "MD-list.txt"), "c");
        File.WriteAllText(Path.Combine(_root, "docs", "guide.md"), "d");
        File.WriteAllText(Path.Combine(_root, "docs", "image.png"), "e");
        File.WriteAllText(Path.Combine(_root, "docs", "sub", "deep.MD"), "f");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // 走査エンジンの仕様を検証するため MFT は使わない(管理者権限で実行しても結果が変わらないように)。
    private static FileSearchOptions Options(string root, string pattern) =>
        new(pattern, root) { PreferMft = false };

    // ---- 一致判定 ----

    [Fact]
    public void Matcher_Substring_IgnoresCase()
    {
        Assert.True(FileSearcher.TryCreateMatcher(".md", useRegex: false, out var matcher, out _));
        Assert.True(matcher("README.MD"));
        Assert.True(matcher("a.md.bak"));
        Assert.False(matcher("readme.txt"));
    }

    // ---- 相対パスの切り出し ----

    [Theory]
    // 通常のディレクトリ(区切りで終わらない): 基準長 + 区切り1文字をスキップ。
    [InlineData(@"C:\work", @"C:\work\docs\a.md", @"docs\a.md")]
    // ドライブ直下(区切りで終わる): 余分に1文字スキップしない(先頭欠けバグの再現防止)。
    [InlineData(@"H:\", @"H:\Game\x.md", @"Game\x.md")]
    [InlineData(@"H:\", @"H:\Downloads\a3.md", @"Downloads\a3.md")]
    // 末尾に区切りが付いた通常ディレクトリでも正しく切り出す。
    [InlineData(@"C:\work\", @"C:\work\docs\a.md", @"docs\a.md")]
    public void MakeRelative_TrimsBaseDirectoryWithoutLosingFirstChar(
        string baseDir, string fullPath, string expected)
    {
        Assert.Equal(expected, FileSearcher.MakeRelative(baseDir, fullPath));
    }

    [Fact]
    public void Matcher_Empty_MatchesAll()
    {
        Assert.True(FileSearcher.TryCreateMatcher("", useRegex: false, out var matcher, out _));
        Assert.True(matcher("anything"));
    }

    [Fact]
    public void Matcher_Wildcard_AnchorsWholeName()
    {
        Assert.True(FileSearcher.TryCreateMatcher("*.md", useRegex: false, out var matcher, out _));
        Assert.True(matcher("readme.md"));
        Assert.False(matcher("a.md.bak"));   // 末尾が .md でないものは不一致

        Assert.True(FileSearcher.TryCreateMatcher("a?c.txt", useRegex: false, out var m2, out _));
        Assert.True(m2("abc.txt"));
        Assert.False(m2("abbc.txt"));
    }

    [Fact]
    public void Matcher_Regex_UsedWhenEnabled()
    {
        Assert.True(FileSearcher.TryCreateMatcher(@"^(readme|guide)\.md$", useRegex: true, out var matcher, out _));
        Assert.True(matcher("README.md"));
        Assert.False(matcher("deep.md"));
    }

    [Fact]
    public void Matcher_InvalidRegex_ReturnsFalseWithError()
    {
        Assert.False(FileSearcher.TryCreateMatcher("[", useRegex: true, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    // ---- 検索本体 ----

    [Fact]
    public void Search_FindsFilesRecursively_NameIsRelativePath()
    {
        var results = FileSearcher.Search(Options(_root, ".md"));

        var names = results.Select(e => e.Name).ToArray();
        Assert.Equal(new[] { @"docs\guide.md", @"docs\sub\deep.MD", "readme.md" }, names);
        var deep = results.Single(e => e.Name == @"docs\sub\deep.MD");
        Assert.Equal(Path.Combine(_root, "docs", "sub", "deep.MD"), deep.FullPath);
        Assert.False(deep.IsDirectory);
    }

    [Fact]
    public void Search_DirectoriesNotIncludedByDefault()
    {
        var results = FileSearcher.Search(Options(_root, "md"));

        Assert.DoesNotContain(results, e => e.IsDirectory);
        Assert.Contains(results, e => e.Name == "MD-list.txt");   // 部分一致・大小無視
    }

    [Fact]
    public void Search_IncludeDirectories_FindsDirs()
    {
        var options = Options(_root, "md") with { IncludeDirectories = true };

        var results = FileSearcher.Search(options);

        var dir = results.Single(e => e.IsDirectory);
        Assert.Equal("mdfolder", dir.Name);
    }

    [Fact]
    public void Search_DirectoriesOnly_WhenFilesOff()
    {
        var options = Options(_root, "") with { IncludeFiles = false, IncludeDirectories = true };

        var results = FileSearcher.Search(options);

        Assert.All(results, e => Assert.True(e.IsDirectory));
        Assert.Equal(new[] { "docs", @"docs\sub", "mdfolder" }, results.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Search_EmptyPattern_FindsAllFiles()
    {
        var results = FileSearcher.Search(Options(_root, ""));

        Assert.Equal(6, results.Count);
    }

    [Fact]
    public void Search_ReportsBatches_SameEntriesAsResult()
    {
        var batched = new List<FileEntry>();
        var results = FileSearcher.Search(Options(_root, ".md"), default,
            batch => { lock (batched) batched.AddRange(batch); });

        Assert.Equal(results.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(e => e.FullPath),
            batched.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(e => e.FullPath));
    }

    [Fact]
    public void Search_AlreadyCancelled_ReturnsEmpty()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var results = FileSearcher.Search(Options(_root, ""), cts.Token);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_ZipFileItselfMatches_AndMarkedAsArchive()
    {
        CreateZip("inner.md", "inner.txt");

        var results = FileSearcher.Search(Options(_root, "pack"));

        var zip = results.Single();
        Assert.Equal("pack.zip", zip.Name);
        Assert.True(zip.IsArchive);
    }

    [Fact]
    public void Search_InsideArchives_WhenEnabled()
    {
        CreateZip("inner.md", "sub/also.md", "skip.txt");
        var options = Options(_root, ".md") with { SearchArchives = true };

        var results = FileSearcher.Search(options);

        var zipPath = Path.Combine(_root, "pack.zip");
        Assert.Contains(results, e => e.FullPath == Path.Combine(zipPath, "inner.md"));
        Assert.Contains(results, e => e.FullPath == Path.Combine(zipPath, "sub", "also.md"));
        Assert.DoesNotContain(results, e => e.FullPath.EndsWith("skip.txt"));
        // 書庫内の相対名は「書庫ファイルからの仮想パス」を基準ディレクトリ相対で表す
        Assert.Contains(results, e => e.Name == @"pack.zip\inner.md");
    }

    [Fact]
    public void Search_InsideArchives_OffByDefault()
    {
        CreateZip("inner.md");

        var results = FileSearcher.Search(Options(_root, "inner"));

        Assert.Empty(results);
    }

    [Fact]
    public void SearchWithInfo_PreferMftOff_UsesDirectoryScan()
    {
        var result = FileSearcher.SearchWithInfo(Options(_root, ".md"));

        Assert.Equal(FileSearchEngine.DirectoryScan, result.Engine);
        Assert.Equal(3, result.Entries.Count);
    }

    /// <summary>
    /// MFT エンジンの統合テスト。管理者権限 + NTFS でのみ実体検証できるため、
    /// 権限がなく通常走査へ切り替わった場合は理由(EngineNote)の通知だけを確認する。
    /// </summary>
    [Fact]
    public void SearchWithInfo_PreferMft_MatchesScanResults_OrReportsReason()
    {
        var options = Options(_root, ".md") with { PreferMft = true };

        var result = FileSearcher.SearchWithInfo(options);

        if (result.Engine == FileSearchEngine.MftIndex)
        {
            // 管理者で実行された場合: 走査エンジンと同じ結果になること
            Assert.Equal(new[] { @"docs\guide.md", @"docs\sub\deep.MD", "readme.md" },
                result.Entries.Select(e => e.Name).ToArray());
        }
        else
        {
            Assert.False(string.IsNullOrEmpty(result.EngineNote));   // 暗黙フォールバック禁止: 理由必須
        }
    }

    private void CreateZip(params string[] entryNames)
    {
        using var fs = File.Create(Path.Combine(_root, "pack.zip"));
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var name in entryNames)
        {
            using var w = new StreamWriter(archive.CreateEntry(name).Open());
            w.Write("x");
        }
    }
}
