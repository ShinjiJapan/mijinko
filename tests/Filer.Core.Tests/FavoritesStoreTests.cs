using Filer.Core;

namespace Filer.Core.Tests;

/// <summary>お気に入り(項目+グループの階層)の永続化(JSON)を一時ファイルで検証する。</summary>
public sealed class FavoritesStoreTests : IDisposable
{
    private readonly string _file;

    public FavoritesStoreTests()
    {
        _file = Path.Combine(Path.GetTempPath(), "FilerFav_" + Guid.NewGuid().ToString("N"), "favorites.json");
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_file)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void GetTree_OnMissingFile_ReturnsEmpty()
    {
        var store = new FavoritesStore(_file);
        Assert.Empty(store.GetTree());
    }

    [Fact]
    public void Add_ThenGetTree_ReturnsItem_WithEmptyLabel()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\work");

        var item = Assert.Single(store.GetTree());
        Assert.Equal(@"C:\work", item.Path);
        Assert.Equal("", item.Label);
        Assert.False(item.IsGroup);
    }

    [Fact]
    public void Add_WithLabel_StoresLabel()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\work", "作業");

        var item = Assert.Single(store.GetTree());
        Assert.Equal(@"C:\work", item.Path);
        Assert.Equal("作業", item.Label);
    }

    [Fact]
    public void Add_Duplicate_IgnoresCaseInsensitively()
    {
        var store = new FavoritesStore(_file);
        Assert.True(store.Add(@"C:\work"));
        Assert.False(store.Add(@"c:\WORK"));

        Assert.Single(store.GetTree());
    }

    [Fact]
    public void Add_Persists_AcrossInstances()
    {
        new FavoritesStore(_file).Add(@"C:\work", "作業");
        var reopened = new FavoritesStore(_file);

        var item = Assert.Single(reopened.GetTree());
        Assert.Equal(@"C:\work", item.Path);
        Assert.Equal("作業", item.Label);
    }

    [Fact]
    public void Add_KeepsInsertionOrder()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\b");
        store.Add(@"C:\a");

        Assert.Equal(new[] { @"C:\b", @"C:\a" }, store.GetTree().Select(f => f.Path));
    }

    [Fact]
    public void Add_WithGroup_CreatesGroupAndAddsItem()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\biz", "", "仕事");

        var group = Assert.Single(store.GetTree());
        Assert.True(group.IsGroup);
        Assert.Equal("仕事", group.Label);
        var item = Assert.Single(group.Children!);
        Assert.Equal(@"C:\biz", item.Path);
    }

    [Fact]
    public void Add_WithNestedGroup_CreatesHierarchy()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\biz\cli", "", "仕事/CLI");

        var outer = Assert.Single(store.GetTree());
        Assert.Equal("仕事", outer.Label);
        var inner = Assert.Single(outer.Children!);
        Assert.True(inner.IsGroup);
        Assert.Equal("CLI", inner.Label);
        var item = Assert.Single(inner.Children!);
        Assert.Equal(@"C:\biz\cli", item.Path);
    }

    [Fact]
    public void Add_ToExistingGroup_Appends_WithoutDuplicatingGroup()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\biz", "", "仕事");
        store.Add(@"C:\cli", "", "仕事");

        var group = Assert.Single(store.GetTree());
        Assert.Equal(new[] { @"C:\biz", @"C:\cli" }, group.Children!.Select(c => c.Path));
    }

    [Fact]
    public void Add_DuplicatePath_InDifferentGroup_Ignored()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\work");
        Assert.False(store.Add(@"C:\work", "", "仕事"));

        var item = Assert.Single(store.GetTree());
        Assert.False(item.IsGroup);
    }

    [Fact]
    public void GetGroupPaths_ReturnsDepthFirstOrder()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\a", "", "仕事/CLI");
        store.Add(@"C:\b", "", "趣味");

        Assert.Equal(new[] { "仕事", "仕事/CLI", "趣味" }, store.GetGroupPaths());
    }

    [Fact]
    public void Update_ChangesPathAndLabel_KeepingPosition()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\a");
        store.Add(@"C:\b");

        store.Update(@"C:\a", @"D:\new", "新ラベル", "");

        var all = store.GetTree();
        Assert.Equal(@"D:\new", all[0].Path);
        Assert.Equal("新ラベル", all[0].Label);
        Assert.Equal(@"C:\b", all[1].Path);
    }

    [Fact]
    public void Update_UnknownPath_DoesNothing()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\a");

        store.Update(@"C:\missing", @"D:\x", "x", "");

        var item = Assert.Single(store.GetTree());
        Assert.Equal(@"C:\a", item.Path);
    }

    [Fact]
    public void Update_MovesItem_ToAnotherGroup()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\a");
        store.Add(@"C:\b");

        store.Update(@"C:\a", @"C:\a", "ラベル", "仕事");

        var tree = store.GetTree();
        Assert.Equal(2, tree.Count);
        Assert.Equal(@"C:\b", tree[0].Path);
        var group = tree[1];
        Assert.True(group.IsGroup);
        Assert.Equal("仕事", group.Label);
        var moved = Assert.Single(group.Children!);
        Assert.Equal(@"C:\a", moved.Path);
        Assert.Equal("ラベル", moved.Label);
    }

    [Fact]
    public void Update_WithinSameGroup_KeepsPosition()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\a", "", "仕事");
        store.Add(@"C:\b", "", "仕事");

        store.Update(@"C:\a", @"D:\a2", "x", "仕事");

        var group = Assert.Single(store.GetTree());
        Assert.Equal(new[] { @"D:\a2", @"C:\b" }, group.Children!.Select(c => c.Path));
    }

    [Fact]
    public void Update_ToPathOfAnotherItem_DoesNothing()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\a");
        store.Add(@"C:\b", "", "仕事");

        store.Update(@"C:\b", @"c:\A", "x", "仕事");

        var tree = store.GetTree();
        Assert.Equal(@"C:\a", tree[0].Path);
        var item = Assert.Single(tree[1].Children!);
        Assert.Equal(@"C:\b", item.Path);
    }

    [Fact]
    public void Update_SamePathDifferentCase_IsAllowed()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\a");

        store.Update(@"C:\a", @"c:\A", "ラベル", "");

        var item = Assert.Single(store.GetTree());
        Assert.Equal(@"c:\A", item.Path);
        Assert.Equal("ラベル", item.Label);
    }

    [Fact]
    public void Remove_DeletesEntry_CaseInsensitively()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\work");
        store.Remove(@"c:\WORK");

        Assert.Empty(store.GetTree());
    }

    [Fact]
    public void Remove_ItemInGroup_KeepsEmptyGroup()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\biz", "", "仕事");
        store.Remove(@"C:\biz");

        var group = Assert.Single(store.GetTree());
        Assert.True(group.IsGroup);
        Assert.Empty(group.Children!);
    }

    [Fact]
    public void RenameGroup_ChangesName_KeepingChildren()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\biz", "", "仕事");

        Assert.True(store.RenameGroup("仕事", "Work"));

        var group = Assert.Single(store.GetTree());
        Assert.Equal("Work", group.Label);
        Assert.Single(group.Children!);
    }

    [Fact]
    public void RenameGroup_ToExistingSiblingName_ReturnsFalse()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\a", "", "仕事");
        store.Add(@"C:\b", "", "趣味");

        Assert.False(store.RenameGroup("仕事", "趣味"));
        Assert.Equal(new[] { "仕事", "趣味" }, store.GetGroupPaths());
    }

    [Fact]
    public void RemoveGroup_RemovesGroupAndDescendants()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\a", "", "仕事/CLI");
        store.Add(@"C:\b");

        store.RemoveGroup("仕事");

        var item = Assert.Single(store.GetTree());
        Assert.Equal(@"C:\b", item.Path);
        Assert.Empty(store.GetGroupPaths());
    }

    [Fact]
    public void NestedTree_Persists_AcrossInstances()
    {
        var store = new FavoritesStore(_file);
        store.Add(@"C:\a", "エー", "仕事/CLI");
        store.Add(@"C:\b");

        var reopened = new FavoritesStore(_file);
        var tree = reopened.GetTree();

        Assert.Equal(2, tree.Count);
        var item = Assert.Single(Assert.Single(tree[0].Children!).Children!);
        Assert.Equal(@"C:\a", item.Path);
        Assert.Equal("エー", item.Label);
        Assert.Equal(@"C:\b", tree[1].Path);
    }

    [Fact]
    public void GetTree_MigratesLegacyStringArrayFormat()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, "[\"C:\\\\a\", \"C:\\\\b\"]");

        var store = new FavoritesStore(_file);
        var all = store.GetTree();

        Assert.Equal(new[] { @"C:\a", @"C:\b" }, all.Select(f => f.Path));
        Assert.All(all, f => Assert.Equal("", f.Label));
    }

    [Fact]
    public void GetTree_MigratesLegacyFlatObjectFormat()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, "[{\"Path\":\"C:\\\\a\",\"Label\":\"エー\"}]");

        var store = new FavoritesStore(_file);
        var item = Assert.Single(store.GetTree());

        Assert.Equal(@"C:\a", item.Path);
        Assert.Equal("エー", item.Label);
        Assert.False(item.IsGroup);
    }

    [Fact]
    public void Add_AfterLegacyLoad_RewritesAsNewFormat()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, "[\"C:\\\\a\"]");

        var store = new FavoritesStore(_file);
        store.Add(@"C:\b", "ビー");

        var json = File.ReadAllText(_file);
        Assert.Contains("\"Path\"", json);
        Assert.Contains("\"Label\"", json);
        Assert.Contains("ビー", json);
    }
}
