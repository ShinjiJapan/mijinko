using Filer.Core;

namespace Filer.Core.Tests;

public sealed class TerminalPreferenceStoreTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "FilerTerminalPrefTests", Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Load_NoFile_ReturnsNull()
    {
        var store = new TerminalPreferenceStore(TempFile());
        Assert.Null(store.LoadLastProfileName());
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = TempFile();
        new TerminalPreferenceStore(path).SaveLastProfileName("Git Bash");

        Assert.Equal("Git Bash", new TerminalPreferenceStore(path).LoadLastProfileName());
    }

    [Fact]
    public void Save_Overwrites_KeepsLatest()
    {
        var path = TempFile();
        var store = new TerminalPreferenceStore(path);
        store.SaveLastProfileName("PowerShell");
        store.SaveLastProfileName("コマンド プロンプト");

        Assert.Equal("コマンド プロンプト", store.LoadLastProfileName());
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNull()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not valid json");

        Assert.Null(new TerminalPreferenceStore(path).LoadLastProfileName());
    }
}
