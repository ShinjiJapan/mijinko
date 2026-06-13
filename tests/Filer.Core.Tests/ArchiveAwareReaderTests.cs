using System.IO.Compression;
using Filer.Core;

namespace Filer.Core.Tests;

public sealed class ArchiveAwareReaderTests : IDisposable
{
    private readonly string _root;
    private readonly string _zip;
    private readonly ArchiveAwareReader _reader = new(new DirectoryLister());

    public ArchiveAwareReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilerZip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _zip = Path.Combine(_root, "test.zip");

        using var fs = File.Create(_zip);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        WriteEntry(archive, "a.txt", "1");
        WriteEntry(archive, "b.txt", "22");
        WriteEntry(archive, "sub/c.txt", "333");
        WriteEntry(archive, "sub/inner/d.txt", "4444");
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        using var w = new StreamWriter(archive.CreateEntry(name).Open());
        w.Write(content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Read_FileSystem_MarksZipAsArchive()
    {
        var entry = _reader.Read(_root).Single(e => e.Name == "test.zip");

        Assert.True(entry.IsArchive);
        Assert.False(entry.IsDirectory);
    }

    [Fact]
    public void Read_ArchiveRoot_ListsTopLevelDirsThenFiles()
    {
        var entries = _reader.Read(_zip);

        Assert.Equal(new[] { "..", "sub", "a.txt", "b.txt" }, entries.Select(e => e.Name).ToArray());
        Assert.True(entries.Single(e => e.Name == "sub").IsDirectory);
        Assert.Equal(_root, entries[0].FullPath);   // ".." は書庫を含む FS フォルダー
    }

    [Fact]
    public void Read_ArchiveSubDir_ListsChildrenAndParent()
    {
        var subPath = Path.Combine(_zip, "sub");

        var entries = _reader.Read(subPath);

        Assert.Equal(new[] { "..", "inner", "c.txt" }, entries.Select(e => e.Name).ToArray());
        Assert.Equal(_zip, entries[0].FullPath);     // ".." は書庫ルート
        Assert.Equal(3, entries.Single(e => e.Name == "c.txt").Size);   // "333"
    }

    [Fact]
    public void Read_ArchiveNestedDir_ParentIsSubDir()
    {
        var innerPath = Path.Combine(_zip, "sub", "inner");

        var entries = _reader.Read(innerPath);

        Assert.Equal(new[] { "..", "d.txt" }, entries.Select(e => e.Name).ToArray());
        Assert.Equal(Path.Combine(_zip, "sub"), entries[0].FullPath);
    }

    [Fact]
    public void TrySplit_PathInsideArchive_SplitsAtZipBoundary()
    {
        var ok = ArchivePath.TrySplit(Path.Combine(_zip, "sub", "inner"), out var archive, out var entry);

        Assert.True(ok);
        Assert.Equal(_zip, archive);
        Assert.Equal(@"sub\inner", entry);
    }

    [Fact]
    public void TrySplit_PlainDirectory_ReturnsFalse()
    {
        Assert.False(ArchivePath.TrySplit(_root, out _, out _));
    }

    [Fact]
    public void IsInsideArchive_PathInsideArchive_ReturnsTrue()
    {
        Assert.True(ArchivePath.IsInsideArchive(Path.Combine(_zip, "sub", "inner")));
    }

    [Fact]
    public void IsInsideArchive_ZipFileItself_ReturnsFalse()
    {
        // 書庫ファイル自体は実ファイルであり「書庫の内部」ではない(削除・移動などが可能)。
        Assert.False(ArchivePath.IsInsideArchive(_zip));
    }

    [Fact]
    public void IsInsideArchive_PlainDirectory_ReturnsFalse()
    {
        Assert.False(ArchivePath.IsInsideArchive(_root));
    }
}
