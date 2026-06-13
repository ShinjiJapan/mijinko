using Filer.Core;

namespace Filer.Core.Tests;

public sealed class DirectoryListerTests : IDisposable
{
    private readonly string _root;
    private readonly DirectoryLister _lister = new();

    public DirectoryListerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilerLister_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Read_ReturnsParentThenDirsThenFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "zdir"));
        Directory.CreateDirectory(Path.Combine(_root, "adir"));
        File.WriteAllText(Path.Combine(_root, "b.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "a.txt"), "x");

        var entries = _lister.Read(_root);

        Assert.Equal("..", entries[0].Name);
        Assert.Equal(new[] { "..", "adir", "zdir", "a.txt", "b.txt" },
            entries.Select(e => e.Name).ToArray());
        Assert.True(entries.Single(e => e.Name == "adir").IsDirectory);
        Assert.False(entries.Single(e => e.Name == "a.txt").IsDirectory);
    }

    [Fact]
    public void Read_FileEntry_HasSizeAndTimestamp()
    {
        File.WriteAllText(Path.Combine(_root, "data.txt"), "12345");

        var entry = _lister.Read(_root).Single(e => e.Name == "data.txt");

        Assert.Equal(5, entry.Size);
        Assert.NotEqual(default, entry.LastModified);
    }
}
