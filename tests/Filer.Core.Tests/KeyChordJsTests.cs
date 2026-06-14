using Filer.Core;

namespace Filer.Core.Tests;

public sealed class KeyChordJsTests
{
    [Fact]
    public void MatchExpression_FunctionKey_ChecksKeyNameAndNoModifiers()
    {
        var expr = KeyChordJs.MatchExpression(new[] { "F2" }, "e");
        Assert.Equal("(!e.ctrlKey && !e.shiftKey && !e.altKey && e.key === 'F2')", expr);
    }

    [Fact]
    public void MatchExpression_Letter_ComparesCaseInsensitively()
    {
        var expr = KeyChordJs.MatchExpression(new[] { "G" }, "e");
        Assert.Equal("(!e.ctrlKey && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 'g')", expr);
    }

    [Fact]
    public void MatchExpression_CtrlModifier_RequiresCtrl()
    {
        var expr = KeyChordJs.MatchExpression(new[] { "Ctrl+G" }, "ev");
        Assert.Equal("(ev.ctrlKey && !ev.shiftKey && !ev.altKey && ev.key.toLowerCase() === 'g')", expr);
    }

    [Fact]
    public void MatchExpression_TopRowDigit_ComparesDigitChar()
    {
        var expr = KeyChordJs.MatchExpression(new[] { "D1" }, "e");
        Assert.Equal("(!e.ctrlKey && !e.shiftKey && !e.altKey && e.key === '1')", expr);
    }

    [Fact]
    public void MatchExpression_ArrowKey_UsesDomKeyName()
    {
        var expr = KeyChordJs.MatchExpression(new[] { "Up" }, "e");
        Assert.Equal("(!e.ctrlKey && !e.shiftKey && !e.altKey && e.key === 'ArrowUp')", expr);
    }

    [Fact]
    public void MatchExpression_MultipleGestures_JoinedWithOr()
    {
        var expr = KeyChordJs.MatchExpression(new[] { "F1", "F2" }, "e");
        Assert.Equal(
            "(!e.ctrlKey && !e.shiftKey && !e.altKey && e.key === 'F1') || " +
            "(!e.ctrlKey && !e.shiftKey && !e.altKey && e.key === 'F2')", expr);
    }

    [Fact]
    public void MatchExpression_NoParsableGesture_ReturnsFalse()
    {
        Assert.Equal("false", KeyChordJs.MatchExpression(System.Array.Empty<string>(), "e"));
        Assert.Equal("false", KeyChordJs.MatchExpression(new[] { "" }, "e"));
    }
}
