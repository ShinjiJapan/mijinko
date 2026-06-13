using Filer.Core;

namespace Filer.Core.Tests;

/// <summary>
/// 実ファイルシステムに対する操作。テストごとに一時ディレクトリを作り、後始末する。
/// </summary>
public sealed class FileOperationsTests : IDisposable
{
    private readonly string _root;
    private readonly FileOperations _ops = new();

    public FileOperationsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string MakeFile(string name, string content = "x")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string MakeDir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Copy_File_DuplicatesIntoDestination()
    {
        var src = MakeFile("a.txt", "hello");
        var dest = MakeDir("dest");

        _ops.Copy(src, dest);

        Assert.True(File.Exists(src));                       // 元は残る
        var copied = Path.Combine(dest, "a.txt");
        Assert.True(File.Exists(copied));
        Assert.Equal("hello", File.ReadAllText(copied));
    }

    [Fact]
    public void Copy_Directory_CopiesRecursively()
    {
        var dir = MakeDir("srcdir");
        File.WriteAllText(Path.Combine(dir, "inner.txt"), "deep");
        var dest = MakeDir("dest");

        _ops.Copy(dir, dest);

        Assert.True(File.Exists(Path.Combine(dest, "srcdir", "inner.txt")));
    }

    [Fact]
    public void Move_File_RemovesOriginal()
    {
        var src = MakeFile("a.txt", "hello");
        var dest = MakeDir("dest");

        _ops.Move(src, dest);

        Assert.False(File.Exists(src));
        Assert.True(File.Exists(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public void Delete_File_Removes()
    {
        var src = MakeFile("a.txt");

        _ops.Delete(src);

        Assert.False(File.Exists(src));
    }

    [Fact]
    public void Delete_Directory_RemovesRecursively()
    {
        var dir = MakeDir("d");
        File.WriteAllText(Path.Combine(dir, "inner.txt"), "x");

        _ops.Delete(dir);

        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void DeleteToRecycleBin_File_RemovesFromOriginalLocation()
    {
        var src = MakeFile("recycle.txt");

        _ops.DeleteToRecycleBin(src);

        // ごみ箱へ送られ、元の場所からは消える(完全削除ではない)。
        Assert.False(File.Exists(src));
    }

    [Fact]
    public void DeleteToRecycleBin_Directory_RemovesFromOriginalLocation()
    {
        var dir = MakeDir("recycledir");
        File.WriteAllText(Path.Combine(dir, "inner.txt"), "x");

        _ops.DeleteToRecycleBin(dir);

        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void DeleteToRecycleBin_Missing_Throws()
    {
        var missing = Path.Combine(_root, "nope.txt");

        Assert.Throws<IOException>(() => _ops.DeleteToRecycleBin(missing));
    }

    [Fact]
    public void Rename_File_ChangesName()
    {
        var src = MakeFile("old.txt", "data");

        _ops.Rename(src, "new.txt");

        Assert.False(File.Exists(src));
        var renamed = Path.Combine(_root, "new.txt");
        Assert.True(File.Exists(renamed));
        Assert.Equal("data", File.ReadAllText(renamed));
    }

    [Fact]
    public void Copy_ToSameDirectory_Throws()
    {
        var src = MakeFile("a.txt");

        // 同一ディレクトリへのコピーは自己上書きとなり危険。明示的に拒否する。
        Assert.Throws<IOException>(() => _ops.Copy(src, _root));
    }

    [Fact]
    public void Rename_ToExistingName_Throws()
    {
        var src = MakeFile("a.txt");
        MakeFile("b.txt");

        Assert.Throws<IOException>(() => _ops.Rename(src, "b.txt"));
    }

    [Fact]
    public void CreateDirectory_CreatesNewFolder()
    {
        _ops.CreateDirectory(_root, "newdir");

        Assert.True(Directory.Exists(Path.Combine(_root, "newdir")));
    }

    [Fact]
    public void CreateDirectory_ExistingFolder_Throws()
    {
        MakeDir("dup");

        Assert.Throws<IOException>(() => _ops.CreateDirectory(_root, "dup"));
    }

    [Fact]
    public void CreateDirectory_ExistingFileName_Throws()
    {
        MakeFile("name.txt");

        Assert.Throws<IOException>(() => _ops.CreateDirectory(_root, "name.txt"));
    }
}
