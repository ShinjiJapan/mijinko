using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Filer.App.ExternalTools;

/// <summary>
/// UWP/ストアアプリ(AUMID)でファイル/フォルダーを開く。
///
/// SkimDown のような単一インスタンスのビューアは、コンソールホスト(powershell/cmd)から
/// 起動されたと判定すると自身を再起動し、Windows.File アクティベーションが通常のコマンドライン
/// 起動に変質して「開いているウィンドウが新しいファイルに切り替わらない」問題が起きる。
/// Filer は GUI プロセス(コンソールホストではない)なので、ここから直接アクティベーションすれば
/// エクスプローラーの「プログラムから開く」と同じく正しくファイルを差し替えて開ける。
///
/// 参考: vscode-open-with-app 拡張の OpenWithAppHost.cs
/// </summary>
public static class UwpLauncher
{
    public static void Open(string aumid, string path)
    {
        var progId = ResolveProgId(aumid, path);
        if (progId != null)
        {
            // 拡張子に登録された ProgID(Windows.File コントラクト)経由で開く。
            ShellOpen(progId, path);
        }
        else
        {
            // フォルダーや ProgID 未登録のパッケージアプリはアクティベーション API で開く。
            UwpActivate(aumid, path);
        }
    }

    // ---- ShellExecuteEx + SEE_MASK_CLASSNAME(登録済み ProgID でファイルを開く) ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize; public uint fMask; public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpDirectory;
        public int nShow; public IntPtr hInstApp; public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpClass;
        public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon; public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteExW(ref SHELLEXECUTEINFO info);

    private static void ShellOpen(string progId, string file)
    {
        var s = new SHELLEXECUTEINFO();
        s.cbSize = Marshal.SizeOf(s);
        s.fMask = 0x00000001;   // SEE_MASK_CLASSNAME
        s.lpClass = progId;
        s.lpVerb = "open";
        s.lpFile = file;
        s.nShow = 1;
        if (!ShellExecuteExW(ref s))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    // この AUMID に紐づく拡張子 ProgID(エクスプローラーと同じ探索)を返す。
    private static string? ResolveProgId(string aumid, string path)
    {
        if (Directory.Exists(path)) return null;
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return null;

        var cands = new List<string>();
        var roots = new[]
        {
            Registry.ClassesRoot.OpenSubKey(ext + "\\OpenWithProgids"),
            Registry.CurrentUser.OpenSubKey("Software\\Classes\\" + ext + "\\OpenWithProgids"),
        };
        foreach (var r in roots)
        {
            if (r == null) continue;
            foreach (var n in r.GetValueNames())
                if (!string.IsNullOrEmpty(n) && !cands.Contains(n)) cands.Add(n);
            r.Close();
        }
        foreach (var c in cands)
        {
            using var k = Registry.ClassesRoot.OpenSubKey(c + "\\shell\\open");
            if (k?.GetValue("AppUserModelID") is string a &&
                string.Equals(a, aumid, StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }

    // ---- UWP アクティベーション(フォルダーや ProgID 未登録時) ----
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { void BindToHandler(); void GetParent(); void GetDisplayName(); void GetAttributes(); void Compare(); }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray { }

    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        int ActivateApplication(string appUserModelId, string arguments, int options, out uint processId);
        int ActivateForFile(string appUserModelId, IShellItemArray itemArray, string? verb, out uint processId);
        int ActivateForProtocol(string appUserModelId, IShellItemArray itemArray, out uint processId);
    }

    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager { }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHCreateShellItemArrayFromShellItem(IShellItem psi, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppv);

    private static Guid _iidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static Guid _iidShellItemArray = new("b63ea76d-1f85-456f-a19c-48159efa858b");

    private static void UwpActivate(string aumid, string path)
    {
        var mgr = (IApplicationActivationManager)new ApplicationActivationManager();
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref _iidShellItem, out var item);
            SHCreateShellItemArrayFromShellItem(item, ref _iidShellItemArray, out var arr);
            var hr = mgr.ActivateForFile(aumid, arr, null, out _);
            if (hr != 0) throw new Exception($"ActivateForFile 0x{hr:X8}");
        }
        catch
        {
            // ファイルコントラクトで開けない場合は引数として渡して起動する。
            var hr = mgr.ActivateApplication(aumid, path, 0, out _);
            if (hr != 0) throw new Exception($"ActivateApplication 0x{hr:X8}");
        }
    }
}
