using Filer.Core;
using Xunit;

namespace Filer.Core.Tests;

public class GitStatusTests
{
    /// <summary>porcelain v2 -z 形式(NUL区切り)の出力を組み立てる。</summary>
    private static string Z(params string[] records) => string.Join('\0', records) + "\0";

    // ---- ブランチヘッダー ----

    [Fact]
    public void Parse_ParsesBranchAndAheadBehind()
    {
        var snapshot = GitStatusParser.Parse(Z(
            "# branch.oid 1234abcd",
            "# branch.head main",
            "# branch.upstream origin/main",
            "# branch.ab +1 -2"));

        Assert.Equal("main", snapshot.Branch);
        Assert.Equal(1, snapshot.Ahead);
        Assert.Equal(2, snapshot.Behind);
    }

    [Fact]
    public void BranchDisplay_BranchOnly()
    {
        var snapshot = GitStatusParser.Parse(Z("# branch.head main"));
        Assert.Equal("main", snapshot.BranchDisplay);
    }

    [Fact]
    public void BranchDisplay_AppendsAheadBehindOnlyWhenNonZero()
    {
        var ahead = GitStatusParser.Parse(Z("# branch.head main", "# branch.ab +3 -0"));
        Assert.Equal("main ↑3", ahead.BranchDisplay);

        var both = GitStatusParser.Parse(Z("# branch.head main", "# branch.ab +1 -2"));
        Assert.Equal("main ↑1 ↓2", both.BranchDisplay);

        var none = GitStatusParser.Parse(Z("# branch.head main", "# branch.ab +0 -0"));
        Assert.Equal("main", none.BranchDisplay);
    }

    [Fact]
    public void BranchDisplay_EmptyWhenNoBranchHeader()
    {
        var snapshot = GitStatusParser.Parse("");
        Assert.Null(snapshot.Branch);
        Assert.Equal("", snapshot.BranchDisplay);
    }

    // ---- ファイル状態 ----

    [Fact]
    public void StateOf_WorktreeModifiedFile_IsModified()
    {
        var snapshot = GitStatusParser.Parse(Z(
            "1 .M N... 100644 100644 100644 aaaa bbbb src/a.txt"));

        Assert.Equal(GitEntryState.Modified, snapshot.StateOf("src/a.txt", isDirectory: false));
    }

    [Fact]
    public void StateOf_StagedNewFile_IsAdded()
    {
        var snapshot = GitStatusParser.Parse(Z(
            "1 A. N... 000000 100644 100644 0000 cccc new.txt"));

        Assert.Equal(GitEntryState.Added, snapshot.StateOf("new.txt", isDirectory: false));
    }

    [Fact]
    public void StateOf_StagedNewThenModified_IsModified()
    {
        var snapshot = GitStatusParser.Parse(Z(
            "1 AM N... 000000 100644 100644 0000 cccc new.txt"));

        Assert.Equal(GitEntryState.Modified, snapshot.StateOf("new.txt", isDirectory: false));
    }

    [Fact]
    public void StateOf_UntrackedFile_IsUntracked()
    {
        var snapshot = GitStatusParser.Parse(Z("? memo.txt"));

        Assert.Equal(GitEntryState.Untracked, snapshot.StateOf("memo.txt", isDirectory: false));
    }

    [Fact]
    public void StateOf_UnmergedFile_IsConflicted()
    {
        var snapshot = GitStatusParser.Parse(Z(
            "u UU N... 100644 100644 100644 100644 a1 a2 a3 conflict.txt"));

        Assert.Equal(GitEntryState.Conflicted, snapshot.StateOf("conflict.txt", isDirectory: false));
    }

    [Fact]
    public void StateOf_IgnoredFile_IsIgnored()
    {
        var snapshot = GitStatusParser.Parse(Z("! bin/out.dll"));

        Assert.Equal(GitEntryState.Ignored, snapshot.StateOf("bin/out.dll", isDirectory: false));
    }

    [Fact]
    public void StateOf_UnknownPath_IsNone()
    {
        var snapshot = GitStatusParser.Parse(Z("? memo.txt"));

        Assert.Equal(GitEntryState.None, snapshot.StateOf("other.txt", isDirectory: false));
    }

    [Fact]
    public void Rename_RegistersNewPath_AndConsumesOriginalPathToken()
    {
        // 種別 2 はパスの次の NUL 区切りトークンがリネーム元。次レコードの解析がずれないこと。
        var snapshot = GitStatusParser.Parse(Z(
            "2 R. N... 100644 100644 100644 aaaa bbbb R100 dst.txt",
            "old.txt",
            "? memo.txt"));

        Assert.Equal(GitEntryState.Modified, snapshot.StateOf("dst.txt", isDirectory: false));
        Assert.Equal(GitEntryState.None, snapshot.StateOf("old.txt", isDirectory: false));
        Assert.Equal(GitEntryState.Untracked, snapshot.StateOf("memo.txt", isDirectory: false));
    }

    [Fact]
    public void StateOf_PathWithSpaces()
    {
        var snapshot = GitStatusParser.Parse(Z(
            "1 .M N... 100644 100644 100644 aaaa bbbb src/my file.txt"));

        Assert.Equal(GitEntryState.Modified, snapshot.StateOf("src/my file.txt", isDirectory: false));
    }

    // ---- ディレクトリ集約 ----

    [Fact]
    public void StateOf_Directory_AggregatesDescendants()
    {
        var snapshot = GitStatusParser.Parse(Z(
            "1 .M N... 100644 100644 100644 aaaa bbbb src/inner/a.txt"));

        Assert.Equal(GitEntryState.Modified, snapshot.StateOf("src", isDirectory: true));
        Assert.Equal(GitEntryState.Modified, snapshot.StateOf("src/inner", isDirectory: true));
    }

    [Fact]
    public void StateOf_Directory_HighestPriorityWins()
    {
        // 優先度: Conflicted > Modified > Added > Untracked > Ignored
        var snapshot = GitStatusParser.Parse(Z(
            "? src/new.txt",
            "u UU N... 100644 100644 100644 100644 a1 a2 a3 src/conflict.txt"));

        Assert.Equal(GitEntryState.Conflicted, snapshot.StateOf("src", isDirectory: true));
    }

    [Fact]
    public void StateOf_UntrackedDirectoryEntry()
    {
        // 未追跡ディレクトリは「dir/」のように末尾スラッシュで1件にまとめて報告される
        var snapshot = GitStatusParser.Parse(Z("? sub/newdir/"));

        Assert.Equal(GitEntryState.Untracked, snapshot.StateOf("sub/newdir", isDirectory: true));
        Assert.Equal(GitEntryState.Untracked, snapshot.StateOf("sub", isDirectory: true));
    }

    [Fact]
    public void StateOf_IgnoredDirectoryEntry()
    {
        var snapshot = GitStatusParser.Parse(Z("! sub/cache/"));

        Assert.Equal(GitEntryState.Ignored, snapshot.StateOf("sub/cache", isDirectory: true));
        Assert.Equal(GitEntryState.Ignored, snapshot.StateOf("sub", isDirectory: true));
    }

    // ---- パス正規化 ----

    [Fact]
    public void StateOf_IsCaseInsensitive()
    {
        var snapshot = GitStatusParser.Parse(Z(
            "1 .M N... 100644 100644 100644 aaaa bbbb src/a.txt"));

        Assert.Equal(GitEntryState.Modified, snapshot.StateOf("SRC/A.TXT", isDirectory: false));
    }

    [Fact]
    public void StateOf_AcceptsBackslashSeparators()
    {
        var snapshot = GitStatusParser.Parse(Z(
            "1 .M N... 100644 100644 100644 aaaa bbbb src/inner/a.txt"));

        Assert.Equal(GitEntryState.Modified, snapshot.StateOf(@"src\inner\a.txt", isDirectory: false));
        Assert.Equal(GitEntryState.Modified, snapshot.StateOf(@"src\inner", isDirectory: true));
    }
}

public class GitRepositoryLocatorTests
{
    [Fact]
    public void FindRoot_MarkerInStartDirectory()
    {
        var root = GitRepositoryLocator.FindRoot(@"C:\repo", p => p == @"C:\repo\.git");
        Assert.Equal(@"C:\repo", root);
    }

    [Fact]
    public void FindRoot_WalksUpToAncestor()
    {
        var root = GitRepositoryLocator.FindRoot(@"C:\repo\src\inner", p => p == @"C:\repo\.git");
        Assert.Equal(@"C:\repo", root);
    }

    [Fact]
    public void FindRoot_ReturnsNullWhenNoMarker()
    {
        var root = GitRepositoryLocator.FindRoot(@"C:\plain\folder", _ => false);
        Assert.Null(root);
    }
}
