using Filer.Core;

namespace Filer.Core.Tests;

public class BulkRenamerTests
{
    private static string[] Names(IReadOnlyList<BulkRenameResult> r) => r.Select(x => x.NewName).ToArray();

    [Fact]
    public void Replace_replaces_plain_text_in_base_name_only()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Replace, Find = "IMG", Replace = "Photo" };
        var r = BulkRenamer.Plan(new[] { "IMG_001.jpg", "IMG_002.JPG" }, opt);

        Assert.Equal(new[] { "Photo_001.jpg", "Photo_002.JPG" }, Names(r));
        Assert.All(r, x => Assert.Equal(BulkRenameStatus.Ok, x.Status));
    }

    [Fact]
    public void Replace_is_case_insensitive_by_default()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Replace, Find = "img", Replace = "x" };
        var r = BulkRenamer.Plan(new[] { "IMG_1.png" }, opt);
        Assert.Equal("x_1.png", r[0].NewName);
    }

    [Fact]
    public void Replace_case_sensitive_respects_case()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Replace, Find = "img", Replace = "x", CaseSensitive = true };
        var r = BulkRenamer.Plan(new[] { "IMG_1.png" }, opt);
        Assert.Equal(BulkRenameStatus.Unchanged, r[0].Status);
    }

    [Fact]
    public void Replace_can_target_extension_when_included()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Replace, Find = "jpeg", Replace = "jpg", IncludeExtension = true };
        var r = BulkRenamer.Plan(new[] { "a.jpeg" }, opt);
        Assert.Equal("a.jpg", r[0].NewName);
    }

    [Fact]
    public void Regex_uses_capture_groups()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Regex, Find = @"(\d+)", Replace = "[$1]" };
        var r = BulkRenamer.Plan(new[] { "file12.txt" }, opt);
        Assert.Equal("file[12].txt", r[0].NewName);
    }

    [Fact]
    public void Regex_invalid_pattern_marks_all_invalid()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Regex, Find = "(", Replace = "x" };
        var r = BulkRenamer.Plan(new[] { "a.txt", "b.txt" }, opt);
        Assert.All(r, x => Assert.Equal(BulkRenameStatus.Invalid, x.Status));
    }

    [Fact]
    public void Sequence_template_pads_and_increments()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Sequence, Template = "page_###", Start = 1, Step = 1 };
        var r = BulkRenamer.Plan(new[] { "a.jpg", "b.jpg", "c.jpg" }, opt);
        Assert.Equal(new[] { "page_001.jpg", "page_002.jpg", "page_003.jpg" }, Names(r));
    }

    [Fact]
    public void Sequence_star_keeps_original_base_and_step_applies()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Sequence, Template = "*-#", Start = 10, Step = 5 };
        var r = BulkRenamer.Plan(new[] { "x.png", "y.png" }, opt);
        Assert.Equal(new[] { "x-10.png", "y-15.png" }, Names(r));
    }

    [Fact]
    public void Sequence_keeps_extensionless_names()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Sequence, Template = "file#", Start = 1, Step = 1 };
        var r = BulkRenamer.Plan(new[] { "README" }, opt);
        Assert.Equal("file1", r[0].NewName);
    }

    [Fact]
    public void Unchanged_when_new_equals_old()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Replace, Find = "zzz", Replace = "q" };
        var r = BulkRenamer.Plan(new[] { "a.txt" }, opt);
        Assert.Equal(BulkRenameStatus.Unchanged, r[0].Status);
        Assert.Equal("a.txt", r[0].NewName);
    }

    [Fact]
    public void Case_only_change_is_not_unchanged()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Regex, Find = "abc", Replace = "ABC", CaseSensitive = true };
        var r = BulkRenamer.Plan(new[] { "abc.txt" }, opt);
        Assert.Equal(BulkRenameStatus.Ok, r[0].Status);
        Assert.Equal("ABC.txt", r[0].NewName);
    }

    [Fact]
    public void Duplicate_results_are_flagged()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Sequence, Template = "same", Start = 1, Step = 0 };
        var r = BulkRenamer.Plan(new[] { "a.txt", "b.txt" }, opt);
        Assert.All(r, x => Assert.Equal(BulkRenameStatus.Duplicate, x.Status));
    }

    [Fact]
    public void Collision_with_existing_file_is_flagged()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Replace, Find = "a", Replace = "b" };
        var r = BulkRenamer.Plan(new[] { "a.txt" }, opt, existingNames: new[] { "b.txt" });
        Assert.Equal(BulkRenameStatus.Duplicate, r[0].Status);
    }

    [Fact]
    public void Collision_with_unchanged_target_is_flagged()
    {
        // "b.txt" は検索語を含まないため変更なし。"a.txt"->"b.txt" はそこへ衝突する。
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Replace, Find = "a", Replace = "b" };
        var r = BulkRenamer.Plan(new[] { "a.txt", "b.txt" }, opt);
        Assert.Equal(BulkRenameStatus.Duplicate, r[0].Status);
        Assert.Equal(BulkRenameStatus.Unchanged, r[1].Status);
    }

    [Fact]
    public void Swap_among_renamed_items_is_allowed()
    {
        // 互いに名前を入れ替えるケースは両方変更されるため衝突扱いにしない。
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Regex, Find = "1", Replace = "2" };
        var r = BulkRenamer.Plan(new[] { "f1.txt" }, opt, existingNames: new[] { "f3.txt" });
        Assert.Equal(BulkRenameStatus.Ok, r[0].Status);
        Assert.Equal("f2.txt", r[0].NewName);
    }

    [Fact]
    public void Empty_result_name_is_invalid()
    {
        // 拡張子なしの名前を全消去 -> 空名 -> 不正
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Regex, Find = ".*", Replace = "" };
        var r = BulkRenamer.Plan(new[] { "README" }, opt);
        Assert.Equal(BulkRenameStatus.Invalid, r[0].Status);
    }

    [Fact]
    public void Invalid_characters_are_flagged()
    {
        var opt = new BulkRenameOptions { Mode = BulkRenameMode.Replace, Find = "a", Replace = "a/b" };
        var r = BulkRenamer.Plan(new[] { "a.txt" }, opt);
        Assert.Equal(BulkRenameStatus.Invalid, r[0].Status);
    }
}
