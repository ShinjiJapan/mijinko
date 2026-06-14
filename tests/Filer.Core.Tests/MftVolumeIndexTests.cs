using System.IO;
using Filer.Core;

namespace Filer.Core.Tests;

/// <summary>
/// MFT 索引のコアロジック(I/O なし)。合成レコードで登録・検索・スコープ判定・パス構築を検証する。
/// </summary>
public sealed class MftVolumeIndexTests
{
    // FRN = 上位16bit シーケンス番号 + 下位48bit レコード番号
    private static ulong Frn(ulong recno, ushort seq = 1) => ((ulong)seq << 48) | recno;

    private static readonly ulong Root = Frn(5, 5);

    /// <summary>
    /// C:\
    ///   docs\            (frn 10)
    ///     guide.md       (frn 11)
    ///     sub\           (frn 12)
    ///       deep.md      (frn 13)
    ///   readme.md        (frn 20)
    ///   pack.zip         (frn 21)
    ///   日本語フォルダ\   (frn 30)
    ///     メモ.txt        (frn 31)
    /// </summary>
    private static MftVolumeIndex BuildSample()
    {
        var index = new MftVolumeIndex(@"C:\", Root);
        index.Set(Frn(10), Root, "docs", isDirectory: true);
        index.Set(Frn(11), Frn(10), "guide.md", isDirectory: false);
        index.Set(Frn(12), Frn(10), "sub", isDirectory: true);
        index.Set(Frn(13), Frn(12), "deep.md", isDirectory: false);
        index.Set(Frn(20), Root, "readme.md", isDirectory: false);
        index.Set(Frn(21), Root, "pack.zip", isDirectory: false);
        index.Set(Frn(30), Root, "日本語フォルダ", isDirectory: true);
        index.Set(Frn(31), Frn(30), "メモ.txt", isDirectory: false);
        return index;
    }

    private static FileNameMatcher Matcher(string pattern)
    {
        Assert.True(FileSearcher.TryCreateMatcher(pattern, useRegex: false, out var matcher, out _));
        return matcher;
    }

    private static List<(string Path, bool IsDir, bool Matched, bool IsZip)> Scan(
        MftVolumeIndex index, string pattern, ulong baseFrn,
        bool includeFiles = true, bool includeDirs = false, bool needZips = false)
    {
        var hits = new List<(string, bool, bool, bool)>();
        index.Scan(Matcher(pattern), needZips, baseFrn, includeFiles, includeDirs,
            (path, isDir, matched, isZip) => hits.Add((path, isDir, matched, isZip)));
        return hits;
    }

    [Fact]
    public void Scan_BuildsFullPath_ThroughParents()
    {
        var hits = Scan(BuildSample(), "deep", Root);

        var hit = Assert.Single(hits);
        Assert.Equal(@"C:\docs\sub\deep.md", hit.Path);
        Assert.False(hit.IsDir);
        Assert.True(hit.Matched);
    }

    [Fact]
    public void Scan_RootScope_FindsAllMatches()
    {
        var hits = Scan(BuildSample(), ".md", Root);

        Assert.Equal(3, hits.Count);
        Assert.Contains(hits, h => h.Path == @"C:\readme.md");
    }

    [Fact]
    public void Scan_SubtreeScope_LimitsToDescendants()
    {
        var hits = Scan(BuildSample(), ".md", Frn(10));

        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.StartsWith(@"C:\docs\", h.Path));
    }

    [Fact]
    public void Scan_ScopeExcludesBaseItself()
    {
        var hits = Scan(BuildSample(), "docs", Frn(10), includeDirs: true);

        Assert.Empty(hits);   // docs 自身は基準ディレクトリなので対象外
    }

    [Fact]
    public void Scan_Directories_OnlyWhenIncluded()
    {
        var index = BuildSample();

        Assert.Empty(Scan(index, "sub", Root));
        var hits = Scan(index, "sub", Root, includeDirs: true);
        var hit = Assert.Single(hits);
        Assert.Equal(@"C:\docs\sub", hit.Path);
        Assert.True(hit.IsDir);
    }

    [Fact]
    public void Scan_FilesOff_ReturnsDirectoriesOnly()
    {
        var hits = Scan(BuildSample(), "", Root, includeFiles: false, includeDirs: true);

        Assert.Equal(3, hits.Count);
        Assert.All(hits, h => Assert.True(h.IsDir));
    }

    [Fact]
    public void Scan_NeedZips_EmitsZipEvenWhenNameNotMatched()
    {
        var hits = Scan(BuildSample(), ".md", Root, needZips: true);

        var zip = Assert.Single(hits, h => h.IsZip);
        Assert.Equal(@"C:\pack.zip", zip.Path);
        Assert.False(zip.Matched);   // ".md" には不一致だが zip 内検索用に通知される
    }

    [Fact]
    public void Scan_UnicodeNames()
    {
        var hits = Scan(BuildSample(), "メモ", Root);

        Assert.Equal(@"C:\日本語フォルダ\メモ.txt", Assert.Single(hits).Path);
    }

    [Fact]
    public void Remove_DeletesEntry()
    {
        var index = BuildSample();
        index.Remove(Frn(20));

        Assert.DoesNotContain(Scan(index, ".md", Root), h => h.Path == @"C:\readme.md");
    }

    [Fact]
    public void Set_SameFrn_UpdatesNameAndParent()
    {
        var index = BuildSample();
        index.Set(Frn(20), Frn(12), "renamed-longer-name.md", isDirectory: false);   // 改名+移動

        var hits = Scan(index, "renamed", Root);
        Assert.Equal(@"C:\docs\sub\renamed-longer-name.md", Assert.Single(hits).Path);
        Assert.Empty(Scan(index, "readme", Root));
    }

    [Fact]
    public void Scan_OrphanEntry_Skipped()
    {
        var index = BuildSample();
        index.Set(Frn(99), Frn(1000), "orphan.md", isDirectory: false);   // 親が索引にない

        Assert.DoesNotContain(Scan(index, ".md", Root), h => h.Path.Contains("orphan"));
    }

    [Fact]
    public void Scan_StaleParentSequence_Skipped()
    {
        var index = BuildSample();
        // 親 FRN のシーケンス番号が現在の索引と食い違う(レコード再利用後の古い参照)
        index.Set(Frn(40), Frn(10, seq: 9), "stale.md", isDirectory: false);

        Assert.DoesNotContain(Scan(index, ".md", Root), h => h.Path.Contains("stale"));
    }

    [Fact]
    public void Scan_ManyEntries_NameHeapGrows()
    {
        var index = new MftVolumeIndex(@"C:\", Root);
        for (ulong i = 0; i < 50_000; i++)
            index.Set(Frn(100 + i), Root, $"file-{i:D8}-with-a-reasonably-long-name.txt", isDirectory: false);

        var hits = Scan(index, "file-00049999", Root);
        Assert.Equal(@"C:\file-00049999-with-a-reasonably-long-name.txt", Assert.Single(hits).Path);
    }

    // ---- ハードリンク(1 FRN に複数名)対応 ----

    [Fact]
    public void AddName_HardLink_FoundAtBothPaths()
    {
        var index = BuildSample();
        // readme.md(frn 20)に docs\linked.md というハードリンク名を追加
        index.AddName(Frn(20), Frn(10), "linked.md", isDirectory: false);

        var hits = Scan(index, ".md", Root);
        Assert.Contains(hits, h => h.Path == @"C:\readme.md");
        Assert.Contains(hits, h => h.Path == @"C:\docs\linked.md");
    }

    [Fact]
    public void AddName_FirstNameForFrn_BehavesLikeSet()
    {
        var index = new MftVolumeIndex(@"C:\", Root);
        index.AddName(Frn(10), Root, "docs", isDirectory: true);
        index.AddName(Frn(11), Frn(10), "a.md", isDirectory: false);

        Assert.Equal(@"C:\docs\a.md", Assert.Single(Scan(index, ".md", Root)).Path);
    }

    [Fact]
    public void AddName_DuplicateSameNameAndParent_NotDoubled()
    {
        var index = BuildSample();
        index.AddName(Frn(20), Root, "readme.md", isDirectory: false);   // 同名・同親の重複登録

        Assert.Single(Scan(index, "readme", Root));
    }

    [Fact]
    public void Set_ClearsExtraNames()
    {
        var index = BuildSample();
        index.AddName(Frn(20), Frn(10), "linked.md", isDirectory: false);
        index.Set(Frn(20), Root, "readme.md", isDirectory: false);   // 改名イベント等で置き換え

        var hits = Scan(index, ".md", Root);
        Assert.DoesNotContain(hits, h => h.Path == @"C:\docs\linked.md");
    }

    [Fact]
    public void Remove_ClearsExtraNames()
    {
        var index = BuildSample();
        index.AddName(Frn(20), Frn(10), "linked.md", isDirectory: false);
        index.Remove(Frn(20));

        var hits = Scan(index, ".md", Root);
        Assert.DoesNotContain(hits, h => h.Path.EndsWith("readme.md") || h.Path.EndsWith("linked.md"));
    }

    [Fact]
    public void ExtraName_ScopeFilteredIndependently()
    {
        var index = BuildSample();
        index.AddName(Frn(20), Frn(12), "linked.md", isDirectory: false);   // sub\linked.md

        var hits = Scan(index, ".md", Frn(10));   // docs 配下のみ
        Assert.Contains(hits, h => h.Path == @"C:\docs\sub\linked.md");
        Assert.DoesNotContain(hits, h => h.Path == @"C:\readme.md");
    }

    [Fact]
    public void PrimaryPathOf_ReturnsCurrentPath()
    {
        var index = BuildSample();

        Assert.Equal(@"C:\docs\sub\deep.md", index.PrimaryPathOf(Frn(13)));
        Assert.Null(index.PrimaryPathOf(Frn(999)));
    }

    [Fact]
    public void Scan_Cancellation_StopsEarly()
    {
        var index = BuildSample();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var hits = new List<string>();
        index.Scan(Matcher(""), false, Root, true, true,
            (path, _, _, _) => hits.Add(path), cts.Token);

        Assert.Empty(hits);
    }

    // ---- ディスク永続化(WriteTo / TryReadFrom)----

    private static MftVolumeIndex RoundTrip(MftVolumeIndex index)
    {
        using var ms = new MemoryStream();
        index.WriteTo(ms);
        ms.Position = 0;
        var loaded = MftVolumeIndex.TryReadFrom(ms, @"C:\", Root);
        Assert.NotNull(loaded);
        return loaded!;
    }

    [Fact]
    public void RoundTrip_PreservesScanResults()
    {
        var loaded = RoundTrip(BuildSample());

        Assert.Equal(@"C:\docs\sub\deep.md", Assert.Single(Scan(loaded, "deep", Root)).Path);
        Assert.Equal(3, Scan(loaded, ".md", Root).Count);
        Assert.Equal(@"C:\日本語フォルダ\メモ.txt", Assert.Single(Scan(loaded, "メモ", Root)).Path);
    }

    [Fact]
    public void RoundTrip_PreservesJournalPosition()
    {
        var index = BuildSample();
        index.JournalId = 0x1122334455667788UL;
        index.NextUsn = 9_876_543_210L;

        var loaded = RoundTrip(index);

        Assert.Equal(0x1122334455667788UL, loaded.JournalId);
        Assert.Equal(9_876_543_210L, loaded.NextUsn);
    }

    [Fact]
    public void RoundTrip_PreservesHardLinks()
    {
        var index = BuildSample();
        index.AddName(Frn(20), Frn(10), "linked.md", isDirectory: false);

        var hits = Scan(RoundTrip(index), ".md", Root);
        Assert.Contains(hits, h => h.Path == @"C:\readme.md");
        Assert.Contains(hits, h => h.Path == @"C:\docs\linked.md");
    }

    [Fact]
    public void RoundTrip_ManyEntries_MultiChunkNameHeap()
    {
        var index = new MftVolumeIndex(@"C:\", Root);
        for (ulong i = 0; i < 50_000; i++)
            index.Set(Frn(100 + i), Root, $"file-{i:D8}-with-a-reasonably-long-name.txt", isDirectory: false);

        var loaded = RoundTrip(index);

        Assert.Equal(@"C:\file-00049999-with-a-reasonably-long-name.txt",
            Assert.Single(Scan(loaded, "file-00049999", Root)).Path);
        Assert.Equal(@"C:\file-00000000-with-a-reasonably-long-name.txt",
            Assert.Single(Scan(loaded, "file-00000000", Root)).Path);
    }

    [Fact]
    public void RoundTrip_ThenDeltaUpdate_Works()
    {
        var loaded = RoundTrip(BuildSample());
        loaded.Set(Frn(40), Root, "added.md", isDirectory: false);   // 読み込み後の差分適用
        loaded.Remove(Frn(20));

        var hits = Scan(loaded, ".md", Root);
        Assert.Contains(hits, h => h.Path == @"C:\added.md");
        Assert.DoesNotContain(hits, h => h.Path == @"C:\readme.md");
    }

    [Fact]
    public void TryReadFrom_RootMismatch_ReturnsNull()
    {
        using var ms = new MemoryStream();
        BuildSample().WriteTo(ms);

        ms.Position = 0;
        Assert.Null(MftVolumeIndex.TryReadFrom(ms, @"D:\", Root));   // ルート違い
        ms.Position = 0;
        Assert.Null(MftVolumeIndex.TryReadFrom(ms, @"C:\", Frn(99, 99)));   // ルート FRN 違い
    }

    [Fact]
    public void TryReadFrom_GarbageOrBadMagic_ReturnsNull()
    {
        Assert.Null(MftVolumeIndex.TryReadFrom(new MemoryStream(new byte[] { 1, 2, 3, 4 }), @"C:\", Root));
        Assert.Null(MftVolumeIndex.TryReadFrom(new MemoryStream(), @"C:\", Root));
    }
}
