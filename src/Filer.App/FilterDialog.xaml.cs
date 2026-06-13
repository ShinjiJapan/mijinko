using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Filer.App;

/// <summary>
/// フィルター表示ダイアログ(Shift+F)。入力のたびに適用コールバックを呼び、
/// 呼び出し側(MainWindow)がアクティブペインを絞り込む(例 "*.jpg" だけ表示)。
/// Enter・決定=確定、Esc・×=開いた時点のフィルターへ戻す。空欄で解除。
/// </summary>
public partial class FilterDialog : Window
{
    private readonly Func<string, int> _apply;   // (pattern) → 一致件数(".." を除く)
    private readonly int _initialCount;

    public FilterDialog(string initial, int initialCount, Func<string, int> apply)
    {
        InitializeComponent();
        _apply = apply;
        _initialCount = initialCount;
        // コンストラクタ(IsLoaded=false)で初期値を入れる。TextChanged は走るが RunApply は早期 return するため
        // 開いている最中にメイン窓へコールバックしない(=位置確定を乱さない / 他ダイアログと同じ作法)。
        Pattern.Text = initial;
        Loaded += (_, _) =>
        {
            PlaceOverOwner();
            CountText.Text = Pattern.Text.Trim().Length == 0 ? string.Empty : $"{_initialCount} 件";
            Pattern.SelectAll();
            Pattern.Focus();
        };
    }

    /// <summary>
    /// オーナーの右下寄り(一覧に被りにくい位置)へ、オーナーのモニター作業領域内に収めて表示する。
    /// マルチ DPI でも破綻しないよう配置はすべてデバイスピクセル(Win32)で行う。
    /// </summary>
    private void PlaceOverOwner()
    {
        if (Owner is null) return;
        var ownerHwnd = new WindowInteropHelper(Owner).Handle;
        var selfHwnd = new WindowInteropHelper(this).Handle;
        if (ownerHwnd == IntPtr.Zero || selfHwnd == IntPtr.Zero) return;
        if (!GetWindowRect(ownerHwnd, out var owner) || !GetWindowRect(selfHwnd, out var self)) return;

        var dlgW = self.Right - self.Left;
        var dlgH = self.Bottom - self.Top;
        var x = owner.Right - dlgW - 32;
        var y = owner.Bottom - dlgH - 64;

        // オーナーのいるモニターの作業領域内へクランプ(画面外へ出さない)。
        var mon = MonitorFromWindow(ownerHwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(mon, ref mi))
        {
            x = Math.Max(mi.rcWork.Left, Math.Min(x, mi.rcWork.Right - dlgW));
            y = Math.Max(mi.rcWork.Top, Math.Min(y, mi.rcWork.Bottom - dlgH));
        }
        SetWindowPos(selfHwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void Pattern_TextChanged(object sender, TextChangedEventArgs e) => RunApply();

    private void RunApply()
    {
        if (!IsLoaded) return;   // InitializeComponent / 初期値設定中の発火は無視
        var count = _apply(Pattern.Text);
        CountText.Text = Pattern.Text.Trim().Length == 0 ? string.Empty : $"{count} 件";
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    // ---- Win32(マルチ DPI でも確実な配置のためデバイスピクセルで扱う) ----
    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, int dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
