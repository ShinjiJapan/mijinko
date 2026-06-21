using System.IO.Compression;
using Filer.Core;

namespace Filer.Core.Tests;

public sealed class DragFileBuilderTests : IDisposable
{
    private readonly string _root;
    private readonly string _zip;

    public DragFileBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilerDrag_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _zip = Path.Combine(_root, "test.zip");

        using var fs = File.Create(_zip);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        using var w = new StreamWriter(archive.CreateEntry("pic.png").Open());
        w.Write("PNGDATA");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static FileEntry Entry(string fullPath, string name) =>
        new(name, fullPath, false, 0, default);

    [Fact]
    public void Build_RealFile_ReturnsPathUnchanged()
    {
        var path = Path.Combine(_root, "a.txt");
        System.IO.File.WriteAllText(path, "x");

        var files = DragFileBuilder.Build(new[] { Entry(path, "a.txt") });

        Assert.Equal(new[] { path }, files);
    }

    [Fact]
    public void Build_ArchiveEntry_ExtractsToTempAndReturnsExistingPath()
    {
        var virtualPath = Path.Combine(_zip, "pic.png");

        var files = DragFileBuilder.Build(new[] { Entry(virtualPath, "pic.png") });

        var single = Assert.Single(files);
        Assert.True(System.IO.File.Exists(single));
        Assert.Equal("pic.png", Path.GetFileName(single));
        Assert.NotEqual(virtualPath, single);   // 書庫内パスではなく抽出先の実パス
    }

    [Fact]
    public void Build_Mixed_KeepsOrder()
    {
        var real = Path.Combine(_root, "b.txt");
        System.IO.File.WriteAllText(real, "y");
        var virtualPath = Path.Combine(_zip, "pic.png");

        var files = DragFileBuilder.Build(new[] { Entry(real, "b.txt"), Entry(virtualPath, "pic.png") });

        Assert.Equal(2, files.Length);
        Assert.Equal(real, files[0]);
        Assert.Equal("pic.png", Path.GetFileName(files[1]));
    }
}
