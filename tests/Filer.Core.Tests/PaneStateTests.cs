using Filer.Core;

namespace Filer.Core.Tests;

/// <summary>
/// メモリ上の木構造を返すフェイク。ディスクに触れずナビゲーションを検証する。
/// </summary>
internal sealed class FakeDirectoryReader : IDirectoryReader
{
    // path -> (子エントリ)。子は親("..")を含まない素のエントリで登録する。
    private readonly Dictionary<string, List<FileEntry>> _tree = new(StringComparer.OrdinalIgnoreCase);
    // 読み取り時に例外を投げるパス(アクセス拒否・存在しないジャンクションを模す)。
    private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);

    public void AddDirectory(string path, params FileEntry[] children)
        => _tree[path] = children.ToList();

    /// <summary>このパスを Read すると例外を投げる(アクセス不能ディレクトリを模す)。</summary>
    public void AddUnreadable(string path) => _unreadable.Add(path);

    public IReadOnlyList<FileEntry> Read(string path)
    {
        if (_unreadable.Contains(path))
            throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");

        var list = new List<FileEntry>();
        var parent = ParentOf(path);
        if (parent is not null)
            list.Add(FileEntry.Parent(parent));
        if (_tree.TryGetValue(path, out var children))
        {
            list.AddRange(children.Where(c => c.IsDirectory).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase));
            list.AddRange(children.Where(c => !c.IsDirectory).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase));
        }
        return list;
    }

    private static string? ParentOf(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var idx = trimmed.LastIndexOf('\\');
        if (idx <= 0) return null;            // ルート相当は親なし
        return trimmed[..idx];
    }
}

public class PaneStateTests
{
    private static FakeDirectoryReader BuildTree()
    {
        var reader = new FakeDirectoryReader();
        // C:\work
        //   sub\        (dir)
        //   a.txt       (file)
        //   b.txt       (file)
        reader.AddDirectory(@"C:\work",
            new FileEntry("sub", @"C:\work\sub", true, 0, default),
            new FileEntry("b.txt", @"C:\work\b.txt", false, 20, default),
            new FileEntry("a.txt", @"C:\work\a.txt", false, 10, default));
        reader.AddDirectory(@"C:\work\sub",
            new FileEntry("inner.txt", @"C:\work\sub\inner.txt", false, 5, default));
        return reader;
    }

    [Fact]
    public void Load_OrdersParentThenDirsThenFiles_AndCursorAtTop()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");

        Assert.Equal(new[] { "..", "sub", "a.txt", "b.txt" },
            pane.Entries.Select(e => e.Name).ToArray());
        Assert.Equal(0, pane.CursorIndex);
        Assert.Equal("..", pane.Current.Name);
    }

    [Fact]
    public void MoveCursor_ClampsWithinBounds()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");

        pane.MoveCursor(-5);                 // 上限クランプ
        Assert.Equal(0, pane.CursorIndex);

        pane.MoveCursor(100);                // 下限クランプ
        Assert.Equal(pane.Entries.Count - 1, pane.CursorIndex);
    }

    [Fact]
    public void MoveCursorWrap_FromTopUp_GoesToBottom()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(0);                 // 先頭

        pane.MoveCursorWrap(-1);              // 先頭で↑ → 末尾へ
        Assert.Equal(pane.Entries.Count - 1, pane.CursorIndex);
    }

    [Fact]
    public void MoveCursorWrap_FromBottomDown_GoesToTop()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(int.MaxValue);      // 末尾

        pane.MoveCursorWrap(1);               // 末尾で↓ → 先頭へ
        Assert.Equal(0, pane.CursorIndex);
    }

    [Fact]
    public void MoveCursorWrap_WithinBounds_MovesNormally()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(1);

        pane.MoveCursorWrap(1);
        Assert.Equal(2, pane.CursorIndex);
    }

    [Fact]
    public void SetSort_ReordersAndKeepsCursorOnSameItem()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(2);                 // "a.txt"
        var before = pane.Current.FullPath;

        pane.SetSort(SortKey.Name, descending: true);

        Assert.Equal(SortKey.Name, pane.SortKey);
        Assert.True(pane.SortDescending);
        // ファイルは降順 b.txt, a.txt。カーソルは同じ a.txt に追従。
        Assert.Equal(before, pane.Current.FullPath);
        Assert.Equal(new[] { "..", "sub", "b.txt", "a.txt" },
            pane.Entries.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void Open_OnDirectory_NavigatesInto()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(1);                // "sub"
        pane.Open();

        Assert.Equal(@"C:\work\sub", pane.CurrentPath);
        Assert.Contains(pane.Entries, e => e.Name == "inner.txt");
    }

    [Fact]
    public void Open_OnFile_DoesNothing()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(2);                // "a.txt"
        pane.Open();

        Assert.Equal(@"C:\work", pane.CurrentPath);
    }

    [Fact]
    public void GoToParent_PlacesCursorOnChildWeCameFrom()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(1);                // "sub"
        pane.Open();                         // C:\work\sub へ
        pane.GoToParent();                   // C:\work へ戻る

        Assert.Equal(@"C:\work", pane.CurrentPath);
        Assert.Equal("sub", pane.Current.Name); // 来た元のディレクトリにカーソル
    }

    [Fact]
    public void Open_OnParentEntry_GoesUp()
    {
        var pane = new PaneState(BuildTree(), @"C:\work\sub");
        pane.MoveCursorTo(0);                // ".."
        pane.Open();

        Assert.Equal(@"C:\work", pane.CurrentPath);
    }

    [Fact]
    public void ToggleMark_TracksSelection_IgnoresParentEntry()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");

        pane.MoveCursorTo(0);                // ".."
        pane.ToggleMark();                   // 親は対象外
        Assert.Empty(pane.MarkedEntries);

        pane.MoveCursorTo(2);                // "a.txt"
        pane.ToggleMark();
        Assert.Single(pane.MarkedEntries);
        Assert.True(pane.IsMarked(pane.Current));

        pane.ToggleMark();                   // 解除
        Assert.Empty(pane.MarkedEntries);
    }

    [Fact]
    public void MarkRange_MarksInclusiveRange_IgnoresParentEntry()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");

        // 一覧は "..", "sub", "a.txt", "b.txt"。0(..)〜3 を範囲指定しても ".." は除外。
        pane.MarkRange(0, 3);
        Assert.Equal(new[] { "a.txt", "b.txt", "sub" },
            pane.MarkedEntries.Select(e => e.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public void MarkRange_AcceptsReversedBounds_AndClamps()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");

        // 端を逆順・範囲外で渡しても 2〜3 をマークする。
        pane.MarkRange(99, 2);
        Assert.Equal(new[] { "a.txt", "b.txt" },
            pane.MarkedEntries.Select(e => e.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public void ItemCount_ExcludesParentEntry()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");

        // 一覧は "..", "sub", "a.txt", "b.txt"。".." は母数から除外。
        Assert.Equal(3, pane.ItemCount);
    }

    [Fact]
    public void MarkedSize_SumsMarkedFilesOnly_IgnoringFolders()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");

        pane.MoveCursorTo(1);                // "sub"(フォルダー)
        pane.ToggleMark();
        pane.MoveCursorTo(2);                // "a.txt"(10 バイト)
        pane.ToggleMark();
        pane.MoveCursorTo(3);                // "b.txt"(20 バイト)
        pane.ToggleMark();

        Assert.Equal(3, pane.MarkedEntries.Count);   // フォルダーもマーク対象
        Assert.Equal(30, pane.MarkedSize);           // ただしサイズはファイルのみ合算
    }

    [Fact]
    public void ToggleMarkAll_MarksAllFiles_ThenClears()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");

        pane.ToggleMarkAll();                // 全選択。フォルダー(sub)と ".." は対象外 → ファイルのみ
        Assert.Equal(new[] { "a.txt", "b.txt" },
            pane.MarkedEntries.Select(e => e.Name).OrderBy(n => n).ToArray());

        pane.ToggleMarkAll();                // 全選択解除
        Assert.Empty(pane.MarkedEntries);
    }

    [Fact]
    public void ToggleMarkAll_ExcludesFolders()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");

        pane.ToggleMarkAll();
        Assert.DoesNotContain(pane.MarkedEntries, e => e.Name == "sub");
    }

    [Fact]
    public void ToggleMarkAll_WhenPartiallyMarked_MarksAllFiles()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(2);                // "a.txt"
        pane.ToggleMark();                   // 一部だけマーク

        pane.ToggleMarkAll();                // 全ファイルがマークされていない → 全ファイル選択
        Assert.Equal(2, pane.MarkedEntries.Count);
    }

    [Fact]
    public void MarkedOrCurrent_FallsBackToCursorWhenNoMarks()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(2);                // "a.txt"

        var targets = pane.MarkedOrCurrent;
        Assert.Single(targets);
        Assert.Equal("a.txt", targets[0].Name);
    }

    [Fact]
    public void MarkedOrCurrent_ReturnsMarks_WhenPresent()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(2);                // "a.txt"
        pane.ToggleMark();
        pane.MoveCursorTo(3);                // "b.txt"
        pane.ToggleMark();
        pane.MoveCursorTo(0);                // カーソルは ".." に移動

        var targets = pane.MarkedOrCurrent.Select(e => e.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "a.txt", "b.txt" }, targets);
    }

    [Fact]
    public void SelectedItemPath_OnFile_ReturnsFilePath()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(2);                // "a.txt"

        Assert.Equal(@"C:\work\a.txt", pane.SelectedItemPath);
    }

    [Fact]
    public void SelectedItemPath_OnSubDirectory_ReturnsFolderPath()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(1);                // "sub"

        Assert.Equal(@"C:\work\sub", pane.SelectedItemPath);
    }

    [Fact]
    public void SelectedItemPath_OnParentEntry_ReturnsCurrentDirectory()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(0);                // ".."

        Assert.Equal(@"C:\work", pane.SelectedItemPath);
    }

    [Fact]
    public void TargetFolderPath_OnSubDirectory_ReturnsThatFolder()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(1);                // "sub"(サブフォルダー)

        Assert.Equal(@"C:\work\sub", pane.TargetFolderPath);
    }

    [Fact]
    public void TargetFolderPath_OnFile_ReturnsCurrentDirectory()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(2);                // "a.txt"(ファイル)

        Assert.Equal(@"C:\work", pane.TargetFolderPath);
    }

    [Fact]
    public void TargetFolderPath_OnParentEntry_ReturnsCurrentDirectory()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(0);                // ".."

        Assert.Equal(@"C:\work", pane.TargetFolderPath);
    }

    [Fact]
    public void Open_WhenTargetUnreadable_PreservesStateAndThrows()
    {
        var reader = BuildTree();
        reader.AddUnreadable(@"C:\work\sub");   // "sub" を開けない状態に
        var pane = new PaneState(reader, @"C:\work");
        pane.MoveCursorTo(1);                    // "sub"

        // 開く操作は例外を投げるが、ペインの状態は元のまま壊れないこと。
        Assert.Throws<UnauthorizedAccessException>(() => pane.Open());
        Assert.Equal(@"C:\work", pane.CurrentPath);
        Assert.Equal(1, pane.CursorIndex);
        Assert.Equal("sub", pane.Current.Name);
        Assert.Equal(4, pane.Entries.Count);
    }

    [Fact]
    public void NavigateAwayAndBack_RestoresCursorToRememberedItem()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(3);                 // "b.txt"

        pane.NavigateTo(@"C:\work\sub");      // 別フォルダーへ移動
        Assert.Equal(0, pane.CursorIndex);    // 初訪問は先頭

        pane.NavigateTo(@"C:\work");          // 戻る
        Assert.Equal("b.txt", pane.Current.Name);  // カーソル位置が再現される
    }

    [Fact]
    public void NavigateBack_WhenRememberedItemGone_CursorAtTop()
    {
        var reader = BuildTree();
        var pane = new PaneState(reader, @"C:\work");
        pane.MoveCursorTo(3);                 // "b.txt"
        pane.NavigateTo(@"C:\work\sub");

        // 戻る前に b.txt を削除(一覧から消す)
        reader.AddDirectory(@"C:\work",
            new FileEntry("sub", @"C:\work\sub", true, 0, default),
            new FileEntry("a.txt", @"C:\work\a.txt", false, 10, default));

        pane.NavigateTo(@"C:\work");
        Assert.Equal(0, pane.CursorIndex);    // 記憶した項目が無ければ先頭
    }

    [Fact]
    public void Reload_KeepsCursorOnSameItem()
    {
        var reader = BuildTree();
        var pane = new PaneState(reader, @"C:\work");
        pane.MoveCursorTo(3);                // "b.txt"

        pane.Reload();

        Assert.Equal(@"C:\work", pane.CurrentPath);
        Assert.Equal("b.txt", pane.Current.Name);
    }

    [Fact]
    public void Reload_WhenItemAddedBefore_CursorFollowsSameItem()
    {
        var reader = BuildTree();
        var pane = new PaneState(reader, @"C:\work");
        pane.MoveCursorTo(2);                // "a.txt"(index 2)

        // a.txt より前に並ぶファイルを追加(並びが繰り上がる)
        reader.AddDirectory(@"C:\work",
            new FileEntry("sub", @"C:\work\sub", true, 0, default),
            new FileEntry("A0.txt", @"C:\work\A0.txt", false, 1, default),
            new FileEntry("a.txt", @"C:\work\a.txt", false, 10, default),
            new FileEntry("b.txt", @"C:\work\b.txt", false, 20, default));

        pane.Reload();

        Assert.Equal("a.txt", pane.Current.Name);   // インデックスがずれてもカーソルは同じ項目
    }

    [Fact]
    public void Reload_WhenCursorItemRemoved_KeepsClampedIndex()
    {
        var reader = BuildTree();
        var pane = new PaneState(reader, @"C:\work");
        pane.MoveCursorTo(3);                // "b.txt"(末尾)

        // b.txt を削除
        reader.AddDirectory(@"C:\work",
            new FileEntry("sub", @"C:\work\sub", true, 0, default),
            new FileEntry("a.txt", @"C:\work\a.txt", false, 10, default));

        pane.Reload();

        // 消えた項目はクランプして近い位置へ(末尾 a.txt)
        Assert.Equal(2, pane.CursorIndex);
        Assert.Equal("a.txt", pane.Current.Name);
    }

    // ---- フィルター表示 ----

    [Fact]
    public void SetFilter_ShowsOnlyMatching_AndAlwaysKeepsParent()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");

        pane.SetFilter("a.txt");

        Assert.True(pane.HasFilter);
        Assert.Equal("a.txt", pane.Filter);
        // ".." は常に残し、一致した a.txt のみ表示(sub・b.txt は隠す)
        Assert.Equal(new[] { "..", "a.txt" },
            pane.Entries.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SetFilter_Wildcard_MatchesWholeName()
    {
        var reader = new FakeDirectoryReader();
        reader.AddDirectory(@"C:\pics",
            new FileEntry("a.jpg", @"C:\pics\a.jpg", false, 1, default),
            new FileEntry("b.jpg", @"C:\pics\b.jpg", false, 1, default),
            new FileEntry("c.png", @"C:\pics\c.png", false, 1, default));
        var pane = new PaneState(reader, @"C:\pics");

        pane.SetFilter("*.jpg");

        Assert.Equal(new[] { "..", "a.jpg", "b.jpg" },
            pane.Entries.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SetFilter_Empty_ClearsFilter()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.SetFilter("a.txt");

        pane.SetFilter("");

        Assert.False(pane.HasFilter);
        Assert.Equal(new[] { "..", "sub", "a.txt", "b.txt" },
            pane.Entries.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SetFilter_KeepsCursorOnCurrentItem_WhenStillVisible()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(2);                // "a.txt"

        pane.SetFilter("txt");               // a.txt も b.txt も残る

        Assert.Equal("a.txt", pane.Current.Name);   // 同じ項目にカーソルが残る
    }

    [Fact]
    public void SetFilter_WhenCursorItemHidden_CursorGoesToTop()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.MoveCursorTo(3);                // "b.txt"

        pane.SetFilter("a.txt");             // b.txt は隠れる

        Assert.Equal(0, pane.CursorIndex);
        Assert.Equal("..", pane.Current.Name);
    }

    [Fact]
    public void Filter_ClearedOnNavigation()
    {
        var pane = new PaneState(BuildTree(), @"C:\work");
        pane.SetFilter("a.txt");

        pane.MoveCursorTo(1);                // "sub"... ただしフィルター中は "a.txt" のみなので
        // フィルター解除して移動を確認するため一旦解除
        pane.SetFilter("");
        pane.MoveCursorTo(1);                // "sub"
        pane.SetFilter("nomatch-keep");      // フィルターを掛ける
        pane.NavigateTo(@"C:\work\sub");     // 別フォルダーへ移動

        Assert.False(pane.HasFilter);        // 移動でフィルター解除
        Assert.Contains(pane.Entries, e => e.Name == "inner.txt");
    }

    [Fact]
    public void Reload_PreservesFilter()
    {
        var reader = BuildTree();
        var pane = new PaneState(reader, @"C:\work");
        pane.SetFilter("a.txt");

        pane.Reload();

        Assert.True(pane.HasFilter);
        Assert.Equal(new[] { "..", "a.txt" },
            pane.Entries.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SetSort_PreservesFilter()
    {
        var reader = new FakeDirectoryReader();
        reader.AddDirectory(@"C:\pics",
            new FileEntry("a.jpg", @"C:\pics\a.jpg", false, 1, default),
            new FileEntry("b.jpg", @"C:\pics\b.jpg", false, 1, default),
            new FileEntry("c.png", @"C:\pics\c.png", false, 1, default));
        var pane = new PaneState(reader, @"C:\pics");
        pane.SetFilter("*.jpg");

        pane.SetSort(SortKey.Name, descending: true);

        // フィルターは維持しつつ降順に並べ替え(.. は先頭)
        Assert.True(pane.HasFilter);
        Assert.Equal(new[] { "..", "b.jpg", "a.jpg" },
            pane.Entries.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SetFilter_NoMatchWithoutParent_LeavesPaneEmptyAndOperationsAreSafe()
    {
        // 親("..")の無いルート相当("C:")でフィルターが全件を隠す状況を再現する。
        var reader = new FakeDirectoryReader();
        reader.AddDirectory(@"C:",
            new FileEntry("a.jpg", @"C:\a.jpg", false, 1, default),
            new FileEntry("b.png", @"C:\b.png", false, 1, default));
        var pane = new PaneState(reader, @"C:");

        pane.SetFilter("*.nomatch");         // 一致なし・".." も無い → 表示は空

        Assert.False(pane.HasItems);
        Assert.Empty(pane.Entries);
        Assert.Empty(pane.MarkedOrCurrent);          // 対象なし
        Assert.Equal(@"C:", pane.SelectedItemPath);  // 表示項目なしは現在ディレクトリ
        Assert.Equal(@"C:", pane.TargetFolderPath);

        // 表示項目が無い状態での操作は例外を投げず何もしない。
        pane.Open();
        pane.ToggleMark();
        Assert.Empty(pane.MarkedEntries);
        Assert.Equal(@"C:", pane.CurrentPath);

        pane.SetFilter("");                  // 解除すれば元に戻る
        Assert.True(pane.HasItems);
        Assert.Equal(new[] { "a.jpg", "b.png" },
            pane.Entries.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void SetFilter_MarkAndOperateOnFilteredViewOnly()
    {
        var reader = new FakeDirectoryReader();
        reader.AddDirectory(@"C:\pics",
            new FileEntry("a.jpg", @"C:\pics\a.jpg", false, 1, default),
            new FileEntry("b.jpg", @"C:\pics\b.jpg", false, 1, default),
            new FileEntry("c.png", @"C:\pics\c.png", false, 1, default));
        var pane = new PaneState(reader, @"C:\pics");
        pane.SetFilter("*.jpg");

        pane.ToggleMarkAll();                // 表示中(*.jpg)のファイルだけマーク

        Assert.Equal(new[] { "a.jpg", "b.jpg" },
            pane.MarkedEntries.Select(e => e.Name).OrderBy(n => n).ToArray());
    }
}
