using System.Runtime.InteropServices;
using Filer.Core;

namespace Filer.Core.Tests;

/// <summary>
/// 実ファイルシステムでの統合テスト。ユーザープロファイル直下にある旧Windows互換用
/// ジャンクション("My Documents" 等。Hidden+System+ReparsePoint で実体は開けない)へ
/// ナビゲートしてもクラッシュ要因(状態破壊)が起きないことを保証する。
/// 該当ジャンクションが無い環境ではスキップ相当(早期 return)。
/// </summary>
public sealed class JunctionNavigationIntegrationTests
{
    [Fact]
    public void Open_OnInaccessibleCompatJunction_ThrowsButKeepsStateConsistent()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var junction = FindInaccessibleReparsePoint(profile);
        if (junction is null) return;   // 環境に互換ジャンクションが無ければ検証不要

        var pane = new PaneState(new DirectoryLister(), profile);
        var index = IndexOf(pane, junction);
        Assert.True(index >= 0, $"一覧に {junction} が現れること");
        pane.MoveCursorTo(index);

        var before = pane.Entries.Count;

        // 開けないジャンクションを Open → 例外は出るが PaneState は壊れない
        Assert.ThrowsAny<Exception>(() => pane.Open());
        Assert.Equal(profile, pane.CurrentPath);
        Assert.Equal(junction, pane.Current.Name);
        Assert.Equal(before, pane.Entries.Count);
    }

    private static int IndexOf(PaneState pane, string name)
    {
        for (var i = 0; i < pane.Entries.Count; i++)
            if (string.Equals(pane.Entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>
    /// ReparsePoint かつ列挙不可(DirectoryNotFound/UnauthorizedAccess)な子ディレクトリ名を返す。
    /// "My Music"/"My Documents" 等の互換ジャンクションが該当する。
    /// </summary>
    private static string? FindInaccessibleReparsePoint(string root)
    {
        foreach (var di in new DirectoryInfo(root).EnumerateDirectories())
        {
            if (!di.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            try
            {
                _ = di.EnumerateDirectories().Any();   // 開けるなら対象外
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException)
            {
                return di.Name;
            }
        }
        return null;
    }
}
