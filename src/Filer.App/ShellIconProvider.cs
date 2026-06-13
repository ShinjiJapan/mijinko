using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Filer.App;

/// <summary>
/// Windows のシェルアイコン(エクスプローラーと同じアイコン)を取得する。
/// 拡張子単位でキャッシュし、大量ファイルでも軽量に動作させる。
/// exe/lnk/ico 等の固有アイコンは実ファイル単位で取得する。
/// </summary>
public static class ShellIconProvider
{
    private const string DirectoryKey = "<DIR>";

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();

    // 実ファイルごとに固有アイコンを持つ拡張子(関連付け汎用アイコンでは表現できない)。
    private static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico", ".cur", ".ani", ".scr",
    };

    /// <summary>パス(またはディレクトリ)に対応するシェルアイコンを返す。取得不可なら null。</summary>
    public static ImageSource? GetIcon(string path, bool isDirectory)
    {
        if (isDirectory)
            return Cache.GetOrAdd(DirectoryKey, _ => LoadByAttributes("folder", directory: true));

        var ext = Path.GetExtension(path);
        if (PerFileExtensions.Contains(ext) && File.Exists(path))
            return LoadByPath(path);   // 固有アイコンはキャッシュせず実ファイルから

        var key = string.IsNullOrEmpty(ext) ? "." : ext.ToLowerInvariant();
        return Cache.GetOrAdd(key, k => LoadByAttributes("file" + (k == "." ? "" : k), directory: false));
    }

    private static ImageSource? LoadByPath(string path)
    {
        var info = default(SHFILEINFO);
        var ok = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON);
        return ToImageSource(ok, info);
    }

    private static ImageSource? LoadByAttributes(string dummyName, bool directory)
    {
        var attr = directory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        var info = default(SHFILEINFO);
        var ok = SHGetFileInfo(dummyName, attr, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
        return ToImageSource(ok, info);
    }

    private static ImageSource? ToImageSource(IntPtr ok, SHFILEINFO info)
    {
        if (ok == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
