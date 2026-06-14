using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Filer.App;

/// <summary>
/// Windows シェルのサムネイル(エクスプローラーと同じ)を取得する。画像は実サムネイル、
/// それ以外のファイルは種別に応じた大きいアイコンが返る(API 既定の挙動)。
/// グリッド(サムネイル)表示で使う。生成は重いため非同期で行い、結果をキャッシュする。
/// 書庫内の仮想パスや実在しないパスは取得対象外(呼び出し側がアイコンへフォールバックする)。
/// </summary>
public static class ShellThumbnailProvider
{
    // path|size → 生成済みサムネイル。
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new();
    // 同時生成数を抑える(大量画像フォルダーで CPU/ディスクを飽和させない)。
    private static readonly SemaphoreSlim Gate = new(Math.Max(2, Environment.ProcessorCount / 2));
    // キャッシュ上限(超えたら丸ごと破棄してメモリを抑える。性能最適化なので素朴で十分)。
    private const int CacheCap = 2000;

    private static string Key(string path, int size) => $"{path}|{size}";

    /// <summary>生成済みサムネイルがあれば返す(無ければ false)。同期で即時表示する用。</summary>
    public static bool TryGetCached(string path, int size, out ImageSource image)
    {
        if (Cache.TryGetValue(Key(path, size), out var cached))
        {
            image = cached;
            return true;
        }
        image = null!;
        return false;
    }

    /// <summary>
    /// サムネイルを非同期に生成し、完了したら UI スレッドで <paramref name="onLoaded"/> を呼ぶ。
    /// 生成できない(仮想パス・実在しない・非対応)場合は呼ばない(呼び出し側のアイコン表示のまま)。
    /// </summary>
    public static void LoadAsync(string path, bool isDirectory, int size, Action<ImageSource> onLoaded)
    {
        // 実ファイル/実フォルダーのみ対象(書庫内の仮想パス等はシェルが解決できない)。
        if (!File.Exists(path) && !Directory.Exists(path)) return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        Task.Run(async () =>
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var image = Create(path, isDirectory, size);
                if (image is null) return;

                if (Cache.Count >= CacheCap) Cache.Clear();
                Cache[Key(path, size)] = image;
                _ = dispatcher.BeginInvoke(new Action(() => onLoaded(image)));
            }
            finally
            {
                Gate.Release();
            }
        });
    }

    /// <summary>シェル API でサムネイル(または大きいアイコン)を生成する。失敗時は null。</summary>
    private static ImageSource? Create(string path, bool isDirectory, int size)
    {
        try
        {
            var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, IID_IShellItem, out var item);
            if (hr != 0 || item is null) return null;
            try
            {
                var factory = (IShellItemImageFactory)item;
                // フォルダーはアイコンのみ(中身を覗くサムネイル生成は遅いことがある)。
                // ファイルは既定(サムネイル→無ければアイコン)。
                var flags = isDirectory ? SIIGBF_ICONONLY : SIIGBF_RESIZETOFIT;
                factory.GetImage(new SIZE { cx = size, cy = size }, flags, out var hbitmap);
                if (hbitmap == IntPtr.Zero) return null;
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally
                {
                    DeleteObject(hbitmap);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (COMException)
        {
            return null;   // サムネイル非対応・生成失敗はアイコン表示のままにする
        }
    }

    // ---- COM / Win32 interop ----

    private const uint SIIGBF_RESIZETOFIT = 0x00000000;
    private const uint SIIGBF_ICONONLY = 0x00000004;

    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, uint flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}
