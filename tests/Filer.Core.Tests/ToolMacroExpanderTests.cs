using Filer.Core;

namespace Filer.Core.Tests;

public sealed class ToolMacroExpanderTests
{
    private static ToolMacroContext Context(
        string cursorName = "report.txt",
        string activeDir = @"C:\work",
        string otherDir = @"D:\other",
        string leftDir = @"C:\work",
        string rightDir = @"D:\other",
        string? activeCursorDir = null,
        string[]? activeMarkedNames = null,
        string[]? activeMarkedFull = null,
        string[]? otherMarkedFull = null)
        => new(
            cursorName,
            activeDir, activeCursorDir ?? activeDir, otherDir, leftDir, rightDir,
            activeMarkedNames ?? new[] { cursorName },
            activeMarkedFull ?? new[] { activeDir + "\\" + cursorName },
            otherMarkedFull ?? Array.Empty<string>());

    [Theory]
    [InlineData("$F", "report.txt")]
    [InlineData("$W", "report")]
    [InlineData("$E", "txt")]
    [InlineData("$P", @"C:\work")]
    [InlineData("$O", @"D:\other")]
    [InlineData("$L", @"C:\work")]
    [InlineData("$R", @"D:\other")]
    public void Expand_SingleValueMacros(string template, string expected)
    {
        Assert.Equal(expected, ToolMacroExpander.Expand(template, Context()));
    }

    [Fact]
    public void Expand_CursorDir_OnFolder_IsThatFolder()
    {
        // $C: カーソルが実フォルダー上ならそのフォルダー、そうでなければ自窓パス。
        var onFolder = Context(activeDir: @"C:\work", activeCursorDir: @"C:\work\sub");
        Assert.Equal(@"C:\work\sub", ToolMacroExpander.Expand("$C", onFolder));

        var onFile = Context(activeDir: @"C:\work");   // activeCursorDir 省略=自窓パス
        Assert.Equal(@"C:\work", ToolMacroExpander.Expand("$C", onFile));
    }

    [Theory]
    // カーソルが実フォルダー: そのフルパス(末尾 \ 除去)。
    [InlineData(true, false, @"C:\work\sub\", @"C:\work", @"C:\work\sub")]
    // カーソルがファイル: 自窓パス。
    [InlineData(false, false, @"C:\work\a.txt", @"C:\work", @"C:\work")]
    // カーソルが "..": 自窓パス。
    [InlineData(true, true, @"C:\", @"C:\work", @"C:\work")]
    public void ResolveCursorDir_PicksFolderOrPaneDir(
        bool isDir, bool isParent, string cursorFullPath, string paneDir, string expected)
    {
        Assert.Equal(expected, ToolMacroExpander.ResolveCursorDir(isDir, isParent, cursorFullPath, paneDir));
    }

    [Fact]
    public void Expand_CombinesMacrosAndLiterals()
    {
        Assert.Equal(@"-d ""C:\work""", ToolMacroExpander.Expand(@"-d ""$P""", Context()));
        Assert.Equal(@"C:\work\report.txt", ToolMacroExpander.Expand(@"$P\$F", Context()));
    }

    [Fact]
    public void Expand_DollarDollar_IsLiteralDollar()
    {
        Assert.Equal("$F", ToolMacroExpander.Expand("$$F", Context()));
        Assert.Equal("a$b", ToolMacroExpander.Expand("a$$b", Context()));
    }

    [Fact]
    public void Expand_UnknownMacro_KeptVerbatim()
    {
        Assert.Equal("$Z", ToolMacroExpander.Expand("$Z", Context()));
    }

    [Fact]
    public void Expand_DotFileCursor_HasNoExtension()
    {
        var ctx = Context(cursorName: ".gitignore");
        Assert.Equal(".gitignore", ToolMacroExpander.Expand("$F", ctx));
        Assert.Equal(".gitignore", ToolMacroExpander.Expand("$W", ctx));
        Assert.Equal("", ToolMacroExpander.Expand("$E", ctx));
    }

    [Fact]
    public void Expand_MarkedNames_QuotedAndSpaceJoined()
    {
        var ctx = Context(activeMarkedNames: new[] { "a.txt", "b c.txt" });
        Assert.Equal("\"a.txt\" \"b c.txt\"", ToolMacroExpander.Expand("$MS", ctx));
    }

    [Fact]
    public void Expand_MarkedFullPaths_QuotedAndSpaceJoined()
    {
        var ctx = Context(activeMarkedFull: new[] { @"C:\a.txt", @"C:\b c.txt" });
        Assert.Equal("\"C:\\a.txt\" \"C:\\b c.txt\"", ToolMacroExpander.Expand("$MF", ctx));
    }

    [Fact]
    public void Expand_OtherMarked_WhenPresent_ListsFullPaths()
    {
        var ctx = Context(otherMarkedFull: new[] { @"D:\x.txt" });
        Assert.Equal("\"D:\\x.txt\"", ToolMacroExpander.Expand("$MO", ctx));
        Assert.Equal("\"D:\\x.txt\"", ToolMacroExpander.Expand("$mO", ctx));
    }

    [Fact]
    public void Expand_UppercaseMO_WhenOtherEmpty_CancelsCommand()
    {
        // $MO で他方にマークが無ければコマンド自体がキャンセル(null を返す)。
        Assert.Null(ToolMacroExpander.Expand("code $MO", Context(otherMarkedFull: Array.Empty<string>())));
    }

    [Fact]
    public void Expand_LowercaseMo_WhenOtherEmpty_BecomesEmpty()
    {
        // $mO で他方にマークが無ければヌル(空文字)に置換し、コマンドは継続。
        Assert.Equal("code ", ToolMacroExpander.Expand("code $mO", Context(otherMarkedFull: Array.Empty<string>())));
    }

    [Fact]
    public void Expand_ParentCursor_FileMacrosEmpty()
    {
        // カーソルが ".." の想定: 呼び出し側が空名・ディレクトリfallbackを渡す。
        var ctx = Context(cursorName: "", activeMarkedNames: new[] { "work" },
            activeMarkedFull: new[] { @"C:\work" });
        Assert.Equal("", ToolMacroExpander.Expand("$F", ctx));
        Assert.Equal("\"work\"", ToolMacroExpander.Expand("$MS", ctx));
        Assert.Equal("\"C:\\work\"", ToolMacroExpander.Expand("$MF", ctx));
    }

    [Fact]
    public void ExpandToSinglePath_ReturnsFirstUnquotedPath()
    {
        // ストアアプリ用: テンプレート展開結果から最初のパスを取り出す。
        var ctx = Context(activeMarkedFull: new[] { @"C:\a.txt", @"C:\b.txt" });
        Assert.Equal(@"C:\a.txt", ToolMacroExpander.ExpandToSinglePath("$MF", ctx));
        Assert.Equal(@"C:\work\report.txt", ToolMacroExpander.ExpandToSinglePath(@"$P\$F", ctx));
    }

    [Fact]
    public void ExpandToSinglePath_CancelledOrEmpty_ReturnsNull()
    {
        Assert.Null(ToolMacroExpander.ExpandToSinglePath("$MO", Context(otherMarkedFull: Array.Empty<string>())));
        Assert.Null(ToolMacroExpander.ExpandToSinglePath("$mO", Context(otherMarkedFull: Array.Empty<string>())));
    }
}
