namespace Filer.Core;

/// <summary>設定ダイアログのタブをキーボードで切り替えるための判定。</summary>
public static class SettingsTabNavigation
{
    /// <summary>
    /// Ctrl+数字で押された <paramref name="digit"/>(1 始まり)を 0 始まりのタブ番号へ変換する。
    /// 範囲外(0 以下、またはタブ数を超える)なら -1 を返す。
    /// </summary>
    public static int IndexForDigit(int digit, int tabCount)
    {
        if (digit < 1 || digit > tabCount) return -1;
        return digit - 1;
    }
}
