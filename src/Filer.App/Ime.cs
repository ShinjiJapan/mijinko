using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Filer.App;

/// <summary>
/// 文字入力欄を持たないウィンドウで IME を無効化するヘルパー。
/// 日本語入力 ON のとき、IME が有効な要素にフォーカスがあると英字キーが
/// Key.ImeProcessed に化けてショートカットが効かず、未確定文字欄が画面左上に開いてしまう。
/// WPF はフォーカス移動のたびにフォーカス要素の InputMethod.IsInputMethodEnabled を見て
/// IME コンテキストを付け外しする(継承されない添付プロパティなので、ウィンドウに
/// 設定しただけでは ListViewItem 等の子要素フォーカスで IME が有効に戻る)。
/// そのため、フォーカスを受けた要素自身へ毎回 false を設定する。
/// WebView2(ターミナル・Markdown プレビュー)は別 HWND が独自に IME を扱うため対象外で、
/// その中での日本語入力はそのまま使える(WebView2 コントロールが HwndHost 派生であることが前提。
/// HwndHost 非依存の WebView2CompositionControl へ移行する場合は除外条件の見直しが必要)。
/// </summary>
internal static class Ime
{
    /// <summary>ウィンドウ内のどの要素にフォーカスが移っても IME を無効のままにする。</summary>
    public static void Disable(Window window)
    {
        InputMethod.SetIsInputMethodEnabled(window, false);
        window.AddHandler(Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnGotFocus), handledEventsToo: true);
    }

    private static void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is UIElement el and not HwndHost && InputMethod.GetIsInputMethodEnabled(el))
            InputMethod.SetIsInputMethodEnabled(el, false);
    }
}
