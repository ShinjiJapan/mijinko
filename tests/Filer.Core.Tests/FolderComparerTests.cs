using Filer.Core;

namespace Filer.Core.Tests;

public class FolderComparerTests
{
    /// <summary>インメモリのディレクトリツリーを表す比較ソース。パスは "root/sub/name" 形式。</summary>
    private sealed class FakeSource : IFolderCompareSource
    {
        // dirPath -> 直下の項目
        private readonly Dictionary<string, List<CompareDirEntry>> _dirs = new(StringComparer.OrdinalIgnoreCase);
        // filePath -> 内容
        private readonly Dictionary<string, string> _contents = new(StringComparer.OrdinalIgnoreCase);

        public FakeSource Dir(string path)
        {
            _dirs.TryAdd(path, new List<CompareDirEntry>());
            return this;
        }

        public FakeSource AddDir(string parent, string name)
        {
            Dir(parent);
            var child = parent + "\\" + name;
            Dir(child);
            _dirs[parent].Add(new CompareDirEntry(name, true, 0, default));
            return this;
        }

        public FakeSource AddFile(string parent, string name, long size, string content = "", DateTime date = default)
        {
            Dir(parent);
            _dirs[parent].Add(new CompareDirEntry(name, false, size, date));
            _contents[parent + "\\" + name] = content;
            return this;
        }

        public IReadOnlyList<CompareDirEntry> List(string directoryPath) =>
            _dirs.TryGetValue(directoryPath, out var items) ? items : new List<CompareDirEntry>();

        public bool ContentEquals(string a, string b) =>
            _contents.GetValueOrDefault(a, "") == _contents.GetValueOrDefault(b, "");
    }

    private static FolderCompareNode Find(IReadOnlyList<FolderCompareNode> nodes, string name) =>
        nodes.First(n => n.Name == name);

    [Fact]
    public void SameSizeFile_IsSame()
    {
        var src = new FakeSource()
            .AddFile(@"L", "a.txt", 10)
            .AddFile(@"R", "a.txt", 10);

        var nodes = FolderComparer.Compare("L", "R", new FolderCompareOptions(), src);

        Assert.Equal(FolderCompareKind.Same, Find(nodes, "a.txt").Kind);
    }

    [Fact]
    public void DifferentSize_IsModified()
    {
        var src = new FakeSource()
            .AddFile(@"L", "a.txt", 10)
            .AddFile(@"R", "a.txt", 20);

        var nodes = FolderComparer.Compare("L", "R", new FolderCompareOptions(), src);

        var node = Find(nodes, "a.txt");
        Assert.Equal(FolderCompareKind.Modified, node.Kind);
        Assert.Equal(10, node.LeftSize);
        Assert.Equal(20, node.RightSize);
    }

    [Fact]
    public void LeftOnlyAndRightOnly_AreClassified()
    {
        var src = new FakeSource()
            .AddFile(@"L", "left.txt", 1)
            .AddFile(@"R", "right.txt", 1);

        var nodes = FolderComparer.Compare("L", "R", new FolderCompareOptions(), src);

        Assert.Equal(FolderCompareKind.LeftOnly, Find(nodes, "left.txt").Kind);
        Assert.Equal(FolderCompareKind.RightOnly, Find(nodes, "right.txt").Kind);
        Assert.Null(Find(nodes, "left.txt").RightPath);
        Assert.Null(Find(nodes, "right.txt").LeftPath);
    }

    [Fact]
    public void DateCompare_SameSizeDifferentDate_IsModified()
    {
        var src = new FakeSource()
            .AddFile(@"L", "a.txt", 10, date: new DateTime(2020, 1, 1))
            .AddFile(@"R", "a.txt", 10, date: new DateTime(2021, 1, 1));

        var size = FolderComparer.Compare("L", "R", new FolderCompareOptions(CompareDate: false), src);
        Assert.Equal(FolderCompareKind.Same, Find(size, "a.txt").Kind);

        var date = FolderComparer.Compare("L", "R", new FolderCompareOptions(CompareDate: true), src);
        Assert.Equal(FolderCompareKind.Modified, Find(date, "a.txt").Kind);
    }

    [Fact]
    public void ContentCompare_SameSizeDifferentContent_IsModified()
    {
        var src = new FakeSource()
            .AddFile(@"L", "a.txt", 3, content: "abc")
            .AddFile(@"R", "a.txt", 3, content: "xyz");

        var sizeOnly = FolderComparer.Compare("L", "R", new FolderCompareOptions(CompareContent: false), src);
        Assert.Equal(FolderCompareKind.Same, Find(sizeOnly, "a.txt").Kind);

        var content = FolderComparer.Compare("L", "R", new FolderCompareOptions(CompareContent: true), src);
        Assert.Equal(FolderCompareKind.Modified, Find(content, "a.txt").Kind);
    }

    [Fact]
    public void NoCriteria_FallsBackToSize()
    {
        var src = new FakeSource()
            .AddFile(@"L", "a.txt", 10)
            .AddFile(@"R", "a.txt", 20);

        var nodes = FolderComparer.Compare("L", "R",
            new FolderCompareOptions(CompareSize: false, CompareDate: false, CompareContent: false), src);

        Assert.Equal(FolderCompareKind.Modified, Find(nodes, "a.txt").Kind);
    }

    [Fact]
    public void Recursive_DirWithDifferingChild_IsModified()
    {
        var src = new FakeSource()
            .AddDir(@"L", "sub").AddDir(@"R", "sub")
            .AddFile(@"L\sub", "x.txt", 1)
            .AddFile(@"R\sub", "x.txt", 2);

        var nodes = FolderComparer.Compare("L", "R", new FolderCompareOptions(Recursive: true), src);

        var sub = Find(nodes, "sub");
        Assert.True(sub.IsDirectory);
        Assert.Equal(FolderCompareKind.Modified, sub.Kind);
        Assert.Equal(FolderCompareKind.Modified, Find(sub.Children, "x.txt").Kind);
    }

    [Fact]
    public void Recursive_DirWithSameChildren_IsSame()
    {
        var src = new FakeSource()
            .AddDir(@"L", "sub").AddDir(@"R", "sub")
            .AddFile(@"L\sub", "x.txt", 1)
            .AddFile(@"R\sub", "x.txt", 1);

        var nodes = FolderComparer.Compare("L", "R", new FolderCompareOptions(Recursive: true), src);

        Assert.Equal(FolderCompareKind.Same, Find(nodes, "sub").Kind);
    }

    [Fact]
    public void NonRecursive_DoesNotDescend()
    {
        var src = new FakeSource()
            .AddDir(@"L", "sub").AddDir(@"R", "sub")
            .AddFile(@"L\sub", "x.txt", 1)
            .AddFile(@"R\sub", "x.txt", 2);

        var nodes = FolderComparer.Compare("L", "R", new FolderCompareOptions(Recursive: false), src);

        var sub = Find(nodes, "sub");
        Assert.Empty(sub.Children);
        Assert.Equal(FolderCompareKind.Same, sub.Kind);
    }

    [Fact]
    public void LeftOnlyDirectory_ExpandsChildrenAsLeftOnly()
    {
        var src = new FakeSource()
            .AddDir(@"L", "only")
            .AddFile(@"L\only", "x.txt", 1)
            .Dir(@"R");

        var nodes = FolderComparer.Compare("L", "R", new FolderCompareOptions(Recursive: true), src);

        var only = Find(nodes, "only");
        Assert.Equal(FolderCompareKind.LeftOnly, only.Kind);
        Assert.Equal(FolderCompareKind.LeftOnly, Find(only.Children, "x.txt").Kind);
    }

    [Fact]
    public void TypeMismatch_SameName_IsModified()
    {
        var src = new FakeSource()
            .AddDir(@"L", "node")
            .AddFile(@"R", "node", 5);

        var nodes = FolderComparer.Compare("L", "R", new FolderCompareOptions(), src);

        Assert.Equal(FolderCompareKind.Modified, Find(nodes, "node").Kind);
    }

    [Fact]
    public void DirectoriesSortBeforeFiles()
    {
        var src = new FakeSource()
            .AddFile(@"L", "a.txt", 1).AddFile(@"R", "a.txt", 1)
            .AddDir(@"L", "zdir").AddDir(@"R", "zdir");

        var nodes = FolderComparer.Compare("L", "R", new FolderCompareOptions(), src);

        Assert.True(nodes[0].IsDirectory);
        Assert.Equal("zdir", nodes[0].Name);
        Assert.Equal("a.txt", nodes[1].Name);
    }

    [Fact]
    public void Summarize_CountsFilesByKind()
    {
        var src = new FakeSource()
            .AddFile(@"L", "same.txt", 1).AddFile(@"R", "same.txt", 1)
            .AddFile(@"L", "mod.txt", 1).AddFile(@"R", "mod.txt", 2)
            .AddFile(@"L", "left.txt", 1)
            .AddFile(@"R", "right.txt", 1);

        var nodes = FolderComparer.Compare("L", "R", new FolderCompareOptions(), src);
        var s = FolderComparer.Summarize(nodes);

        Assert.Equal(1, s.Same);
        Assert.Equal(1, s.Modified);
        Assert.Equal(1, s.LeftOnly);
        Assert.Equal(1, s.RightOnly);
    }

    [Fact]
    public void FilterDifferencesOnly_RemovesSameNodes()
    {
        var src = new FakeSource()
            .AddFile(@"L", "same.txt", 1).AddFile(@"R", "same.txt", 1)
            .AddFile(@"L", "mod.txt", 1).AddFile(@"R", "mod.txt", 2)
            .AddDir(@"L", "samedir").AddDir(@"R", "samedir")
            .AddFile(@"L\samedir", "y.txt", 1).AddFile(@"R\samedir", "y.txt", 1);

        var nodes = FolderComparer.Compare("L", "R", new FolderCompareOptions(Recursive: true), src);
        var filtered = FolderComparer.FilterDifferencesOnly(nodes);

        Assert.Single(filtered);
        Assert.Equal("mod.txt", filtered[0].Name);
    }
}
