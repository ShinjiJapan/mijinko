using Filer.Core;

namespace Filer.Core.Tests;

public sealed class AppSettingsStoreTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "FilerAppSettingsTests", Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var settings = new AppSettingsStore(TempFile()).Load();
        Assert.Empty(settings.KeyBindingOverrides);
        // 既定では従来どおりの4ツールが入る。
        Assert.Equal(ExternalTools.Defaults().Select(t => t.Id), settings.Tools.Select(t => t.Id));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsToolsAndKeyBindings()
    {
        var path = TempFile();
        var store = new AppSettingsStore(path);
        var tools = new[]
        {
            new ExternalTool("vscode", "VSCode", ExternalToolKind.Executable, @"C:\tools\Code.exe", "$MF", new[] { "V" }),
            new ExternalTool("my-app", "マイアプリ", ExternalToolKind.StoreApp, "Some.App!Id", "$P\\$F", new[] { "Ctrl+1" }),
        };
        var settings = new AppSettings(
            new Dictionary<string, string[]> { ["file.copy"] = new[] { "X" } }, tools);

        store.Save(settings);
        var loaded = new AppSettingsStore(path).Load();

        Assert.Equal(new[] { "X" }, loaded.KeyBindingOverrides["file.copy"]);
        Assert.Equal(2, loaded.Tools.Count);
        var vscode = loaded.Tools[0];
        Assert.Equal("vscode", vscode.Id);
        Assert.Equal(ExternalToolKind.Executable, vscode.Kind);
        Assert.Equal(@"C:\tools\Code.exe", vscode.Target);
        Assert.Equal("$MF", vscode.Arguments);
        Assert.Equal(new[] { "V" }, vscode.Gestures);
        var myApp = loaded.Tools[1];
        Assert.Equal("マイアプリ", myApp.Label);
        Assert.Equal(ExternalToolKind.StoreApp, myApp.Kind);
        Assert.Equal("Some.App!Id", myApp.Target);
    }

    [Fact]
    public void Save_EmptyToolList_LoadsEmpty()
    {
        var path = TempFile();
        new AppSettingsStore(path).Save(new AppSettings(
            new Dictionary<string, string[]>(), Array.Empty<ExternalTool>()));

        // tools キーは存在し空配列 → 既定へ戻さず「ツール無し」を尊重する。
        Assert.Empty(new AppSettingsStore(path).Load().Tools);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ broken json");

        var settings = new AppSettingsStore(path).Load();
        Assert.Empty(settings.KeyBindingOverrides);
        Assert.NotEmpty(settings.Tools);
    }

    [Fact]
    public void Load_NoToolsKey_FillsDefaultTools()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "keyBindings": { "file.copy": ["X"] } }""");

        var settings = new AppSettingsStore(path).Load();
        Assert.Equal(new[] { "X" }, settings.KeyBindingOverrides["file.copy"]);
        Assert.Equal(ExternalTools.Defaults().Count, settings.Tools.Count);
    }

    [Fact]
    public void Load_NoFile_ThemeIsDark()
    {
        Assert.Equal(AppTheme.Dark, new AppSettingsStore(TempFile()).Load().Theme);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTheme()
    {
        var path = TempFile();
        new AppSettingsStore(path).Save(new AppSettings(
            new Dictionary<string, string[]>(), Array.Empty<ExternalTool>(), AppTheme.Beige));

        Assert.Equal(AppTheme.Beige, new AppSettingsStore(path).Load().Theme);
    }

    [Fact]
    public void Load_NoThemeKey_DefaultsToDark()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "keyBindings": { "file.copy": ["X"] } }""");

        Assert.Equal(AppTheme.Dark, new AppSettingsStore(path).Load().Theme);
    }

    [Fact]
    public void Load_UnknownTheme_DefaultsToDark()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "theme": "Bogus" }""");

        Assert.Equal(AppTheme.Dark, new AppSettingsStore(path).Load().Theme);
    }

    [Fact]
    public void Load_NoFile_LightweightListAutomationIsOff()
    {
        Assert.False(new AppSettingsStore(TempFile()).Load().LightweightListAutomation);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsLightweightListAutomation()
    {
        var path = TempFile();
        new AppSettingsStore(path).Save(new AppSettings(
            new Dictionary<string, string[]>(), Array.Empty<ExternalTool>(),
            LightweightListAutomation: true));

        Assert.True(new AppSettingsStore(path).Load().LightweightListAutomation);
    }

    [Fact]
    public void Load_NoLightweightKey_DefaultsToOff()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "keyBindings": { "file.copy": ["X"] } }""");

        Assert.False(new AppSettingsStore(path).Load().LightweightListAutomation);
    }

    [Fact]
    public void Load_NoFile_ConfirmationsDefaultToOn()
    {
        var s = new AppSettingsStore(TempFile()).Load();
        Assert.True(s.ConfirmMove);
        Assert.True(s.ConfirmRecycle);
        Assert.True(s.ConfirmPermanentDelete);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsConfirmations()
    {
        var path = TempFile();
        new AppSettingsStore(path).Save(new AppSettings(
            new Dictionary<string, string[]>(), Array.Empty<ExternalTool>(),
            ConfirmMove: false, ConfirmRecycle: false, ConfirmPermanentDelete: false));

        var loaded = new AppSettingsStore(path).Load();
        Assert.False(loaded.ConfirmMove);
        Assert.False(loaded.ConfirmRecycle);
        Assert.False(loaded.ConfirmPermanentDelete);
    }

    [Fact]
    public void Load_NoConfirmationKeys_DefaultToOn()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "keyBindings": { "file.copy": ["X"] } }""");

        var s = new AppSettingsStore(path).Load();
        Assert.True(s.ConfirmMove);
        Assert.True(s.ConfirmRecycle);
        Assert.True(s.ConfirmPermanentDelete);
    }

    [Fact]
    public void Load_NoFile_EnableElevatedFastSearchDefaultsToOn()
    {
        Assert.True(new AppSettingsStore(TempFile()).Load().EnableElevatedFastSearch);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEnableElevatedFastSearch()
    {
        var path = TempFile();
        new AppSettingsStore(path).Save(new AppSettings(
            new Dictionary<string, string[]>(), Array.Empty<ExternalTool>(),
            EnableElevatedFastSearch: false));

        Assert.False(new AppSettingsStore(path).Load().EnableElevatedFastSearch);
    }

    [Fact]
    public void Load_NoFastSearchKey_DefaultsToOn()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "keyBindings": { "file.copy": ["X"] } }""");

        Assert.True(new AppSettingsStore(path).Load().EnableElevatedFastSearch);
    }

    [Fact]
    public void Load_NoFile_MarkupPreviewModeDefaultsToHighlight()
    {
        Assert.Equal(MarkupPreviewMode.Highlight,
            new AppSettingsStore(TempFile()).Load().MarkupPreviewMode);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsMarkupPreviewMode()
    {
        var path = TempFile();
        new AppSettingsStore(path).Save(new AppSettings(
            new Dictionary<string, string[]>(), Array.Empty<ExternalTool>(),
            MarkupPreviewMode: MarkupPreviewMode.Rendered));

        Assert.Equal(MarkupPreviewMode.Rendered,
            new AppSettingsStore(path).Load().MarkupPreviewMode);
    }

    [Fact]
    public void Load_UnknownMarkupPreviewMode_DefaultsToHighlight()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "markupPreviewMode": "Bogus" }""");

        Assert.Equal(MarkupPreviewMode.Highlight,
            new AppSettingsStore(path).Load().MarkupPreviewMode);
    }

    [Fact]
    public void Save_OmitsKeyOverridesIdenticalToDefaults()
    {
        var path = TempFile();
        var store = new AppSettingsStore(path);
        store.Save(new AppSettings(
            new Dictionary<string, string[]>
            {
                ["file.copy"] = new[] { "C" },   // 既定と同じ → 保存しない
                ["file.move"] = new[] { "X" },
            },
            Array.Empty<ExternalTool>()));

        var loaded = store.Load();
        Assert.False(loaded.KeyBindingOverrides.ContainsKey("file.copy"));
        Assert.Equal(new[] { "X" }, loaded.KeyBindingOverrides["file.move"]);
    }
}
