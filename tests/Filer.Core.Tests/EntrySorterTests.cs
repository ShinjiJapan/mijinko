using Filer.Core;
using Xunit;

namespace Filer.Core.Tests;

public class EntrySorterTests
{
    private static FileEntry Dir(string name) => new(name, $@"C:\w\{name}", true, 0, default);
    private static FileEntry File(string name, long size, DateTime date) =>
        new(name, $@"C:\w\{name}", false, size, date);

    private static IReadOnlyList<FileEntry> Sample() => new[]
    {
        FileEntry.Parent(@"C:\"),
        Dir("zebra"),
        Dir("alpha"),
        File("b.txt", 30, new DateTime(2020, 1, 2)),
        File("a.log", 10, new DateTime(2020, 1, 3)),
        File("c.dat", 20, new DateTime(2020, 1, 1)),
    };

    [Fact]
    public void Sort_ByName_Ascending_ParentFirst_DirsBeforeFiles()
    {
        var r = EntrySorter.Sort(Sample(), SortKey.Name, descending: false);

        Assert.Equal(new[] { "..", "alpha", "zebra", "a.log", "b.txt", "c.dat" },
            r.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Sort_ByName_Descending_ParentStillFirst_DirsBeforeFiles()
    {
        var r = EntrySorter.Sort(Sample(), SortKey.Name, descending: true);

        Assert.Equal("..", r[0].Name);
        Assert.Equal(new[] { "zebra", "alpha" },
            r.Where(e => e.IsDirectory && !e.IsParent).Select(e => e.Name).ToArray());
        Assert.Equal(new[] { "c.dat", "b.txt", "a.log" },
            r.Where(e => !e.IsDirectory).Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Sort_ByExtension_Ascending()
    {
        var r = EntrySorter.Sort(Sample(), SortKey.Extension, descending: false);

        // dat < log < txt
        Assert.Equal(new[] { "c.dat", "a.log", "b.txt" },
            r.Where(e => !e.IsDirectory).Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Sort_BySize_Ascending()
    {
        var r = EntrySorter.Sort(Sample(), SortKey.Size, descending: false);

        // 10 < 20 < 30
        Assert.Equal(new[] { "a.log", "c.dat", "b.txt" },
            r.Where(e => !e.IsDirectory).Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Sort_ByDate_Ascending()
    {
        var r = EntrySorter.Sort(Sample(), SortKey.Date, descending: false);

        // 1/1 < 1/2 < 1/3
        Assert.Equal(new[] { "c.dat", "b.txt", "a.log" },
            r.Where(e => !e.IsDirectory).Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Sort_BySize_Descending_KeepsDirsBeforeFiles()
    {
        var r = EntrySorter.Sort(Sample(), SortKey.Size, descending: true);

        Assert.Equal("..", r[0].Name);
        Assert.Equal(new[] { "b.txt", "c.dat", "a.log" },
            r.Where(e => !e.IsDirectory).Select(e => e.Name).ToArray());
    }
}
