using System.Collections.Generic;
using System.Windows.Input;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// Core の <see cref="KeyChord"/>(ジェスチャ文字列)と WPF の Key/ModifierKeys の相互変換。
/// </summary>
public static class KeyChordWpf
{
    /// <summary>ジェスチャ文字列を WPF のキー+修飾へ変換する。解析できなければ false。</summary>
    public static bool TryToWpf(string gesture, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;
        if (!KeyChord.TryParse(gesture, out var chord))
            return false;
        if (!Enum.TryParse(chord.KeyName, ignoreCase: true, out key) || key == Key.None)
            return false;
        if (chord.Ctrl) modifiers |= ModifierKeys.Control;
        if (chord.Shift) modifiers |= ModifierKeys.Shift;
        if (chord.Alt) modifiers |= ModifierKeys.Alt;
        return true;
    }

    /// <summary>押されたキーをジェスチャ文字列(標準形)へ変換する。修飾キー単独・IME 処理中などは null。</summary>
    public static string? FromKeyEvent(Key key, ModifierKeys modifiers)
    {
        if (IsModifier(key) || key is Key.None or Key.ImeProcessed or Key.DeadCharProcessed)
            return null;
        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(KeyNameOf(key));
        return string.Join("+", parts);
    }

    /// <summary>押されたキーを設定マップで解決し、割り当て中のアクション Id を返す(無ければ null)。</summary>
    public static string? Resolve(KeyBindingMap map, Key key, ModifierKeys modifiers)
    {
        var pressed = FromKeyEvent(key, modifiers);
        return pressed is null ? null : map.TryResolve(pressed);
    }

    /// <summary>修飾キーそのもの(Ctrl/Shift/Alt/Win)か。</summary>
    public static bool IsModifier(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin or Key.System;

    /// <summary>
    /// Key を標準キー名へ。Return/Prior/Next 等は enum の別名どちらが返るか不定のため明示変換する。
    /// </summary>
    private static string KeyNameOf(Key key) => key switch
    {
        Key.Return => "Enter",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        Key.Capital => "CapsLock",
        _ => key.ToString(),
    };
}
