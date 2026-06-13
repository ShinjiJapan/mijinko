using Filer.Core;

namespace Filer.Core.Tests;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public SessionStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "FilerSession_" + Guid.NewGuid().ToString("N"));
        _file = Path.Combine(_dir, "session.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_NoFile_ReturnsNull()
    {
        Assert.Null(new SessionStore(_file).Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SessionStore(_file);
        store.Save(new SessionState(
            new SessionPane(new[] { @"C:\a", @"C:\a\sub" }, 1),
            new SessionPane(new[] { @"D:\b" }, 0),
            IsLeftActive: false));

        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(new[] { @"C:\a", @"C:\a\sub" }, loaded!.Left.TabPaths);
        Assert.Equal(1, loaded.Left.ActiveTabIndex);
        Assert.Equal(new[] { @"D:\b" }, loaded.Right.TabPaths);
        Assert.False(loaded.IsLeftActive);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsWindowBounds()
    {
        var store = new SessionStore(_file);
        store.Save(new SessionState(
            new SessionPane(new[] { @"C:\a" }, 0),
            new SessionPane(new[] { @"D:\b" }, 0),
            IsLeftActive: true,
            new WindowBounds(100, 200, 1280, 720, Maximized: true)));

        var loaded = store.Load();

        Assert.NotNull(loaded!.Window);
        Assert.Equal(100, loaded.Window!.Left);
        Assert.Equal(200, loaded.Window.Top);
        Assert.Equal(1280, loaded.Window.Width);
        Assert.Equal(720, loaded.Window.Height);
        Assert.True(loaded.Window.Maximized);
    }

    [Fact]
    public void Load_WithoutWindowBounds_WindowIsNull()
    {
        var store = new SessionStore(_file);
        store.Save(new SessionState(
            new SessionPane(new[] { @"C:\a" }, 0),
            new SessionPane(new[] { @"D:\b" }, 0),
            IsLeftActive: true));

        Assert.Null(store.Load()!.Window);
    }

    [Fact]
    public void Load_InvalidJson_ReturnsNull()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_file, "not json");

        Assert.Null(new SessionStore(_file).Load());
    }

    [Fact]
    public void Load_OldSchemaWithoutTabs_ReturnsNull()
    {
        Directory.CreateDirectory(_dir);
        // タブ情報を持たない旧形式は復元せず既定値で起動させる。
        File.WriteAllText(_file, """{"LeftPath":"C:\\a","RightPath":"D:\\b","IsLeftActive":true}""");

        Assert.Null(new SessionStore(_file).Load());
    }
}
