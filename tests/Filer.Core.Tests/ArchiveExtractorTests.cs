using System.IO.Compression;
using Filer.Core;

namespace Filer.Core.Tests;

public sealed class ArchiveExtractorTests : IDisposable
{
    private readonly string _root;
    private readonly string _zip;
    private readonly string _dest;

    public ArchiveExtractorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilerZipEx_" + Guid.NewGuid().ToString("N"));
        _dest = Path.Combine(_root, "out");
        Directory.CreateDirectory(_dest);
        _zip = Path.Combine(_root, "test.zip");

        using var fs = File.Create(_zip);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        WriteEntry(archive, "a.txt", "hello");
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
    public void ReadEntryBytes_ReturnsFileContent()
    {
        var bytes = ArchiveExtractor.ReadEntryBytes(Path.Combine(_zip, "a.txt"));

        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void ExtractTo_File_WritesIntoDestination()
    {
        ArchiveExtractor.ExtractTo(Path.Combine(_zip, "a.txt"), _dest);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(_dest, "a.txt")));
    }

    [Fact]
    public void ExtractTo_NestedFile_UsesFileNameOnly()
    {
        ArchiveExtractor.ExtractTo(Path.Combine(_zip, "sub", "c.txt"), _dest);

        Assert.Equal("333", File.ReadAllText(Path.Combine(_dest, "c.txt")));
    }

    [Fact]
    public void ExtractTo_Directory_ExtractsRecursively()
    {
        ArchiveExtractor.ExtractTo(Path.Combine(_zip, "sub"), _dest);

        Assert.Equal("333", File.ReadAllText(Path.Combine(_dest, "sub", "c.txt")));
        Assert.Equal("4444", File.ReadAllText(Path.Combine(_dest, "sub", "inner", "d.txt")));
    }

    [Fact]
    public void ExtractTo_ExistingName_Throws()
    {
        ArchiveExtractor.ExtractTo(Path.Combine(_zip, "a.txt"), _dest);

        Assert.Throws<IOException>(() =>
            ArchiveExtractor.ExtractTo(Path.Combine(_zip, "a.txt"), _dest));
    }
}
