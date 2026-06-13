using Filer.Core;

namespace Filer.Core.Tests;

public sealed class SearchResultsReaderTests : IDisposable
{
    private readonly string _root;
    private readonly SearchResultsReader _reader = new(new DirectoryLister());

    public SearchResultsReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilerSearchReader_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "a.md"), "1");
        File.WriteAllText(Path.Combine(_root, "b.md"), "2");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private FileEntry Entry(string name) =>
        new(name, Path.Combine(_root, name), false, 1, DateTime.Now);

    [Fact]
    public void Register_ReturnsVirtualPath_WithLabel()
    {
        var path = _reader.Register("検索結果: .md", _root, new[] { Entry("a.md") });

        Assert.True(SearchResultsReader.IsVirtual(path));
        Assert.True(SearchResultsReader.TryGetLabel(path, out var label));
        Assert.Equal("検索結果: .md", label);
    }

    [Fact]
    public void IsVirtual_FalseForNormalPath()
    {
        Assert.False(SearchResultsReader.IsVirtual(_root));
        Assert.False(SearchResultsReader.TryGetLabel(_root, out _));
    }

    [Fact]
    public void Read_VirtualPath_ReturnsParentThenEntries()
    {
        var path = _reader.Register("検索結果", _root, new[] { Entry("a.md"), Entry("b.md") });

        var entries = _reader.Read(path);

        Assert.Equal(3, entries.Count);
        Assert.True(entries[0].IsParent);
        Assert.Equal(_root, entries[0].FullPath);   // ".." は基準ディレクトリへ戻る
        Assert.Equal(new[] { "a.md", "b.md" }, entries.Skip(1).Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Read_VirtualPath_FiltersDeletedEntries()
    {
        var path = _reader.Register("検索結果", _root, new[] { Entry("a.md"), Entry("b.md") });
        File.Delete(Path.Combine(_root, "b.md"));

        var entries = _reader.Read(path);

        Assert.Equal(new[] { "a.md" }, entries.Skip(1).Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Read_NormalPath_DelegatesToInner()
    {
        var entries = _reader.Read(_root);

        Assert.Contains(entries, e => e.Name == "a.md");
    }

    [Fact]
    public void Read_UnknownVirtualPath_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => _reader.Read("search://999/なし"));
    }

    [Fact]
    public void Register_DistinctPathsForEachCall()
    {
        var p1 = _reader.Register("A", _root, Array.Empty<FileEntry>());
        var p2 = _reader.Register("A", _root, Array.Empty<FileEntry>());

        Assert.NotEqual(p1, p2);
    }
}
