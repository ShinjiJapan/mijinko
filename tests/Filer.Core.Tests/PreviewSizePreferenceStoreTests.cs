using Filer.Core;

namespace Filer.Core.Tests;

public sealed class PreviewSizePreferenceStoreTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "FilerPreviewSizePrefTests", Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void NoFile_DefaultsToFullScreen()
    {
        var store = new PreviewSizePreferenceStore(TempFile());
        Assert.True(store.IsFullScreen(PreviewKind.Image));
        Assert.True(store.IsFullScreen(PreviewKind.Markdown));
    }

    [Fact]
    public void SetThenReload_RoundTripsPerKind()
    {
        var path = TempFile();
        var store = new PreviewSizePreferenceStore(path);
        store.Set(PreviewKind.Image, false);     // 画像は1ペインで記憶
        store.Set(PreviewKind.Markdown, true);   // Markdown は全画面で記憶

        var reloaded = new PreviewSizePreferenceStore(path);
        Assert.False(reloaded.IsFullScreen(PreviewKind.Image));
        Assert.True(reloaded.IsFullScreen(PreviewKind.Markdown));
        // 未保存の種別は既定の全画面。
        Assert.True(reloaded.IsFullScreen(PreviewKind.Pdf));
    }

    [Fact]
    public void Set_Overwrites_KeepsLatest()
    {
        var path = TempFile();
        var store = new PreviewSizePreferenceStore(path);
        store.Set(PreviewKind.Text, false);
        store.Set(PreviewKind.Text, true);

        Assert.True(new PreviewSizePreferenceStore(path).IsFullScreen(PreviewKind.Text));
    }

    [Fact]
    public void CorruptFile_DefaultsToFullScreen()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not valid json");

        Assert.True(new PreviewSizePreferenceStore(path).IsFullScreen(PreviewKind.Html));
    }
}
