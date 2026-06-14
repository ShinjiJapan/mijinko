using Filer.Core;
using Xunit;

namespace Filer.Core.Tests;

public class SettingsTabNavigationTests
{
    [Theory]
    [InlineData(1, 6, 0)]
    [InlineData(2, 6, 1)]
    [InlineData(6, 6, 5)]
    public void 範囲内の数字はタブ番号に変換される(int digit, int tabCount, int expected)
    {
        Assert.Equal(expected, SettingsTabNavigation.IndexForDigit(digit, tabCount));
    }

    [Theory]
    [InlineData(0, 6)]   // 0 は対象外
    [InlineData(7, 6)]   // タブ数を超える
    [InlineData(9, 6)]
    public void 範囲外の数字は無効値を返す(int digit, int tabCount)
    {
        Assert.Equal(-1, SettingsTabNavigation.IndexForDigit(digit, tabCount));
    }
}
