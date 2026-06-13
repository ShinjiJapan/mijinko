using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Filer.App;

/// <summary>
/// ウィンドウのタイトルバー(キャプション)を現在のテーマ色へ合わせる。
/// DWM 属性で背景・文字・枠色とダーク/ライトのボタン表示を設定する。
/// キャプション色の指定は Windows 11(build 22000+)以降で有効。古い OS では
/// ダーク/ライトのフラグのみ効き、色指定は無視される(エラーは握って無害化)。
/// </summary>
internal static class WindowThemeHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;   // タイトルバーのダーク/ライト(ボタン表示)
    private const int DWMWA_BORDER_COLOR = 34;              // ウィンドウ枠色(Win11+)
    private const int DWMWA_CAPTION_COLOR = 35;             // タイトルバー背景色(Win11+)
    private const int DWMWA_TEXT_COLOR = 36;                // タイトル文字色(Win11+)

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>指定ウィンドウのタイトルバーへ現在のテーマ色を適用する(HWND 未生成なら何もしない)。</summary>
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var isDark = Luminance(ColorOf("App.Background")) < 0.5;
        var darkFlag = isDark ? 1 : 0;
        var caption = ToColorRef(ColorOf("Surface.Background"));
        var text = ToColorRef(ColorOf("Fg.Primary"));
        var border = ToColorRef(ColorOf("Border.Default"));

        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    /// <summary>現在開いている全ウィンドウのタイトルバーへテーマ色を再適用する(テーマ切替時)。</summary>
    public static void ApplyToAll()
    {
        if (Application.Current is null) return;
        foreach (Window window in Application.Current.Windows)
            Apply(window);
    }

    /// <summary>Color → DWM の COLORREF(0x00BBGGRR)。</summary>
    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static Color ColorOf(string key) =>
        Application.Current?.Resources[key] is SolidColorBrush b ? b.Color : Colors.Black;
}
