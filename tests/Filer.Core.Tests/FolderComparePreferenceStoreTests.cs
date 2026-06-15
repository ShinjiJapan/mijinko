using System.IO;
using Filer.Core;

namespace Filer.Core.Tests;

public class FolderComparePreferenceStoreTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "FolderCmpPref_" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Load_WhenMissing_ReturnsDefaults()
    {
        var opts = new FolderComparePreferenceStore(TempFile()).Load();

        Assert.Equal(new FolderCompareOptions(), opts);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = TempFile();
        var saved = new FolderCompareOptions(CompareSize: false, CompareDate: true, CompareContent: true,
            Recursive: false, ShowSame: false);

        new FolderComparePreferenceStore(path).Save(saved);
        var loaded = new FolderComparePreferenceStore(path).Load();

        Assert.Equal(saved, loaded);
    }

    [Fact]
    public void Load_WhenCorrupt_ReturnsDefaults()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ not json");

        var opts = new FolderComparePreferenceStore(path).Load();

        Assert.Equal(new FolderCompareOptions(), opts);
    }
}
