using Filer.Core;

namespace Filer.Core.Tests;

public sealed class KeyChordTests
{
    [Fact]
    public void TryParse_PlainKey_Succeeds()
    {
        Assert.True(KeyChord.TryParse("T", out var chord));
        Assert.False(chord.Ctrl);
        Assert.False(chord.Shift);
        Assert.False(chord.Alt);
        Assert.Equal("T", chord.KeyName);
    }

    [Fact]
    public void TryParse_WithModifiers_Succeeds()
    {
        Assert.True(KeyChord.TryParse("Ctrl+Shift+T", out var chord));
        Assert.True(chord.Ctrl);
        Assert.True(chord.Shift);
        Assert.False(chord.Alt);
        Assert.Equal("T", chord.KeyName);
    }

    [Theory]
    [InlineData("ctrl+t")]
    [InlineData("CTRL + T")]
    [InlineData("Control+T")]
    public void TryParse_IsCaseAndSpaceInsensitive_AcceptsControlAlias(string text)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.True(chord.Ctrl);
        Assert.Equal("T", chord.KeyName, ignoreCase: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl")]
    [InlineData("Shift+Alt")]
    [InlineData("Ctrl+T+X")]
    public void TryParse_Invalid_Fails(string text)
    {
        Assert.False(KeyChord.TryParse(text, out _));
    }

    [Fact]
    public void ToString_OrdersModifiers_CtrlShiftAlt()
    {
        Assert.True(KeyChord.TryParse("alt+shift+ctrl+F5", out var chord));
        Assert.Equal("Ctrl+Shift+Alt+F5", chord.ToString());
    }

    [Fact]
    public void Normalized_IgnoresCaseOfKeyName()
    {
        Assert.True(KeyChord.TryParse("ctrl+t", out var a));
        Assert.True(KeyChord.TryParse("Ctrl+T", out var b));
        Assert.Equal(a.Normalized, b.Normalized);
    }

    [Fact]
    public void Normalized_DistinguishesDifferentModifiers()
    {
        Assert.True(KeyChord.TryParse("T", out var plain));
        Assert.True(KeyChord.TryParse("Ctrl+T", out var ctrl));
        Assert.NotEqual(plain.Normalized, ctrl.Normalized);
    }

    [Theory]
    [InlineData("D1", "1")]
    [InlineData("NumPad1", "Num1")]
    [InlineData("Back", "BS")]
    [InlineData("Delete", "Del")]
    [InlineData("Escape", "Esc")]
    [InlineData("Left", "←")]
    [InlineData("Right", "→")]
    [InlineData("Up", "↑")]
    [InlineData("Down", "↓")]
    [InlineData("PageUp", "PgUp")]
    [InlineData("PageDown", "PgDn")]
    [InlineData("Enter", "Enter")]
    [InlineData("F5", "F5")]
    [InlineData("T", "T")]
    public void DisplayText_FormatsKeyName(string gesture, string expected)
    {
        Assert.True(KeyChord.TryParse(gesture, out var chord));
        Assert.Equal(expected, chord.DisplayText);
    }

    [Fact]
    public void DisplayText_IncludesModifiers()
    {
        Assert.True(KeyChord.TryParse("Ctrl+Left", out var chord));
        Assert.Equal("Ctrl+←", chord.DisplayText);
    }
}
