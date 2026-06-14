using Filer.Core;

namespace Filer.Core.Tests;

public class CommandPaletteFilterTests
{
    private static IReadOnlyList<CommandPaletteItem> Items() => new[]
    {
        new CommandPaletteItem("file.copy", "コピー(相手ペインへ)", "ファイル操作", "C"),
        new CommandPaletteItem("file.move", "移動(相手ペインへ)", "ファイル操作", "M"),
        new CommandPaletteItem("file.rename", "名前の変更", "ファイル操作", "R"),
        new CommandPaletteItem("search.file", "ファイル検索", "基本操作", "F"),
        new CommandPaletteItem("settings.open", "設定を開く", "ツール", "Z"),
        new CommandPaletteItem("tool:vscode", "Visual Studio Code", "外部ツール", "V"),
    };

    [Fact]
    public void 空クエリは全件を定義順で返す()
    {
        var result = CommandPaletteFilter.Filter(Items(), "");
        Assert.Equal(Items().Select(i => i.Id), result.Select(i => i.Id));
    }

    [Fact]
    public void 空白のみのクエリも全件を返す()
    {
        var result = CommandPaletteFilter.Filter(Items(), "   ");
        Assert.Equal(6, result.Count);
    }

    [Fact]
    public void タイトルの部分一致で絞り込む()
    {
        var result = CommandPaletteFilter.Filter(Items(), "ペイン");
        Assert.Equal(new[] { "file.copy", "file.move" }, result.Select(i => i.Id));
    }

    [Fact]
    public void カテゴリでも一致する()
    {
        var result = CommandPaletteFilter.Filter(Items(), "外部ツール");
        Assert.Equal(new[] { "tool:vscode" }, result.Select(i => i.Id));
    }

    [Fact]
    public void Idでも一致する()
    {
        var result = CommandPaletteFilter.Filter(Items(), "rename");
        Assert.Equal(new[] { "file.rename" }, result.Select(i => i.Id));
    }

    [Fact]
    public void 大文字小文字を区別しない()
    {
        var result = CommandPaletteFilter.Filter(Items(), "CODE");
        Assert.Equal(new[] { "tool:vscode" }, result.Select(i => i.Id));
    }

    [Fact]
    public void 複数トークンは全て含む項目だけ返す_順不同()
    {
        // "studio" と "visual" の両方を含む
        var result = CommandPaletteFilter.Filter(Items(), "studio visual");
        Assert.Equal(new[] { "tool:vscode" }, result.Select(i => i.Id));
    }

    [Fact]
    public void 一致なしは空を返す()
    {
        Assert.Empty(CommandPaletteFilter.Filter(Items(), "存在しない語"));
    }

    [Fact]
    public void タイトル前方一致を部分一致より上位にする()
    {
        var items = new[]
        {
            new CommandPaletteItem("a", "再ファイル化", "x", ""),   // 部分一致(途中に「ファイル」)
            new CommandPaletteItem("b", "ファイル検索", "x", ""),   // 前方一致
        };
        var result = CommandPaletteFilter.Filter(items, "ファイル");
        Assert.Equal(new[] { "b", "a" }, result.Select(i => i.Id));
    }
}
