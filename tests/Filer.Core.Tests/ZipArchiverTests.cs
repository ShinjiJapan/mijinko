using System.IO.Compression;
using Filer.Core;

namespace Filer.Core.Tests;

public sealed class ZipArchiverTests : IDisposable
{
    private readonly string _root;

    public ZipArchiverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilerZipCreate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Src(string rel, string content)
    {
        var path = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Create_SingleFile_StoresAtRoot()
    {
        var file = Src("a.txt", "hello");
        var zip = Path.Combine(_root, "out.zip");

        ZipArchiver.Create(new[] { file }, zip);

        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal("hello", ReadEntry(archive, "a.txt"));
    }

    [Fact]
    public void Create_MultipleFiles_StoresEach()
    {
        var a = Src("a.txt", "AA");
        var b = Src("b.txt", "BB");
        var zip = Path.Combine(_root, "out.zip");

        ZipArchiver.Create(new[] { a, b }, zip);

        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal("AA", ReadEntry(archive, "a.txt"));
        Assert.Equal("BB", ReadEntry(archive, "b.txt"));
    }

    [Fact]
    public void Create_Folder_StoresRecursivelyUnderFolderName()
    {
        Src("dir/c.txt", "333");
        Src("dir/inner/d.txt", "4444");
        var dir = Path.Combine(_root, "dir");
        var zip = Path.Combine(_root, "out.zip");

        ZipArchiver.Create(new[] { dir }, zip);

        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal("333", ReadEntry(archive, "dir/c.txt"));
        Assert.Equal("4444", ReadEntry(archive, "dir/inner/d.txt"));
    }

    [Fact]
    public void Create_EmptyFolder_KeptAsDirectoryEntry()
    {
        var dir = Path.Combine(_root, "empty");
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(_root, "out.zip");

        ZipArchiver.Create(new[] { dir }, zip);

        using var archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName == "empty/");
    }

    [Fact]
    public void Create_ExistingDestination_Throws()
    {
        var file = Src("a.txt", "hello");
        var zip = Path.Combine(_root, "out.zip");
        File.WriteAllText(zip, "occupied");

        Assert.Throws<IOException>(() => ZipArchiver.Create(new[] { file }, zip));
    }

    [Fact]
    public void Create_NoSources_Throws()
    {
        var zip = Path.Combine(_root, "out.zip");

        Assert.Throws<InvalidOperationException>(() => ZipArchiver.Create(Array.Empty<string>(), zip));
    }

    [Theory]
    [InlineData("report.docx", "report.zip")]
    [InlineData("myfolder", "myfolder.zip")]
    [InlineData("photo.jpeg", "photo.zip")]
    public void DefaultZipName_IgnoresOriginalExtension(string source, string expected)
    {
        Assert.Equal(expected, ZipArchiver.DefaultZipName(source));
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new Xunit.Sdk.XunitException($"エントリなし: {name}");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
