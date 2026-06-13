using Filer.Core;

namespace Filer.Core.Tests;

/// <summary>
/// 1ペイン内の複数タブ(各タブ = 1つの <see cref="PaneState"/>)の
/// 追加・削除・切り替えを検証する。フェイクReaderでディスクに触れない。
/// </summary>
public class PaneTabsTests
{
    private static FakeDirectoryReader BuildTree()
    {
        var reader = new FakeDirectoryReader();
        reader.AddDirectory(@"C:\work",
            new FileEntry("sub", @"C:\work\sub", true, 0, default),
            new FileEntry("a.txt", @"C:\work\a.txt", false, 10, default));
        reader.AddDirectory(@"C:\work\sub",
            new FileEntry("c.txt", @"C:\work\sub\c.txt", false, 5, default));
        reader.AddDirectory(@"C:\other");
        return reader;
    }

    [Fact]
    public void Constructor_StartsWithSingleActiveTab()
    {
        var tabs = new PaneTabs(BuildTree(), @"C:\work");

        Assert.Single(tabs.Tabs);
        Assert.Equal(0, tabs.ActiveIndex);
        Assert.Equal(@"C:\work", tabs.Active.CurrentPath);
    }

    [Fact]
    public void AddTab_DuplicatesActivePath_AndBecomesActive()
    {
        var tabs = new PaneTabs(BuildTree(), @"C:\work");

        var added = tabs.AddTab();

        Assert.Equal(2, tabs.Tabs.Count);
        Assert.Equal(1, tabs.ActiveIndex);
        Assert.Same(added, tabs.Active);
        Assert.Equal(@"C:\work", added.CurrentPath);   // 現在フォルダを複製
    }

    [Fact]
    public void AddTab_InsertsRightAfterActive()
    {
        var tabs = new PaneTabs(BuildTree(), @"C:\work");
        var second = tabs.AddTab();     // index1 (active)
        tabs.Activate(0);               // 先頭へ戻す
        var inserted = tabs.AddTab();   // index1 に挿入され active

        Assert.Equal(3, tabs.Tabs.Count);
        Assert.Equal(1, tabs.ActiveIndex);
        Assert.Same(inserted, tabs.Tabs[1]);
        Assert.Same(second, tabs.Tabs[2]);
    }

    [Fact]
    public void Tabs_NavigateIndependently()
    {
        var tabs = new PaneTabs(BuildTree(), @"C:\work");
        tabs.AddTab();
        tabs.Active.NavigateTo(@"C:\work\sub");

        Assert.Equal(@"C:\work\sub", tabs.Active.CurrentPath);
        Assert.Equal(@"C:\work", tabs.Tabs[0].CurrentPath);   // 別タブは影響を受けない
    }

    [Fact]
    public void CloseActive_RemovesTab_AndActivatesPrevious()
    {
        var tabs = new PaneTabs(BuildTree(), @"C:\work");
        tabs.AddTab();   // index1 active

        tabs.CloseActive();

        Assert.Single(tabs.Tabs);
        Assert.Equal(0, tabs.ActiveIndex);
    }

    [Fact]
    public void CloseTab_BeforeActive_KeepsActiveTabSelected()
    {
        var tabs = new PaneTabs(BuildTree(), @"C:\work");
        var t1 = tabs.AddTab();   // index1
        var t2 = tabs.AddTab();   // index2 active
        Assert.Same(t2, tabs.Active);

        tabs.CloseTab(0);         // 先頭(非アクティブ)を閉じる

        Assert.Equal(2, tabs.Tabs.Count);
        Assert.Same(t2, tabs.Active);   // アクティブは t2 のまま
        Assert.Same(t1, tabs.Tabs[0]);
    }

    [Fact]
    public void CloseTab_LastRemaining_IsNoOp()
    {
        var tabs = new PaneTabs(BuildTree(), @"C:\work");

        tabs.CloseActive();   // 1枚しかないので閉じない

        Assert.Single(tabs.Tabs);
    }

    [Fact]
    public void Activate_OutOfRange_IsIgnored()
    {
        var tabs = new PaneTabs(BuildTree(), @"C:\work");
        tabs.AddTab();

        tabs.Activate(-1);
        Assert.Equal(1, tabs.ActiveIndex);
        tabs.Activate(99);
        Assert.Equal(1, tabs.ActiveIndex);
    }

    [Fact]
    public void ActivateNext_WrapsAround()
    {
        var tabs = new PaneTabs(BuildTree(), @"C:\work");
        tabs.AddTab();   // index1 active (2 tabs)

        tabs.ActivateNext();
        Assert.Equal(0, tabs.ActiveIndex);   // 末尾の次は先頭
        tabs.ActivateNext();
        Assert.Equal(1, tabs.ActiveIndex);
    }

    [Fact]
    public void RestoreConstructor_OpensAllTabs_AndClampsActiveIndex()
    {
        var tabs = new PaneTabs(BuildTree(), new[] { @"C:\work", @"C:\work\sub", @"C:\other" }, 5);

        Assert.Equal(3, tabs.Tabs.Count);
        Assert.Equal(2, tabs.ActiveIndex);   // 範囲外はクランプされ末尾
        Assert.Equal(new[] { @"C:\work", @"C:\work\sub", @"C:\other" }, tabs.TabPaths);
    }

    [Fact]
    public void RestoreConstructor_EmptyPaths_Throws()
    {
        Assert.Throws<ArgumentException>(() => new PaneTabs(BuildTree(), Array.Empty<string>(), 0));
    }

    [Fact]
    public void TabPaths_ReflectsNavigation()
    {
        var tabs = new PaneTabs(BuildTree(), @"C:\work");
        tabs.AddTab();
        tabs.Active.NavigateTo(@"C:\work\sub");

        Assert.Equal(new[] { @"C:\work", @"C:\work\sub" }, tabs.TabPaths);
    }

    [Fact]
    public void ActivatePrev_WrapsAround()
    {
        var tabs = new PaneTabs(BuildTree(), @"C:\work");
        tabs.AddTab();   // index1 active
        tabs.Activate(0);

        tabs.ActivatePrev();
        Assert.Equal(1, tabs.ActiveIndex);   // 先頭の前は末尾
    }
}
