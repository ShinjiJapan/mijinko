using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Filer.Core;

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
    // 未処理の生成要求。後着優先(LIFO)+ パス重複排除 + 容量上限。
    //  - LIFO: スクロールを止めた瞬間に見えている最新要求を最優先で処理し、下端でも待たせない。
    //  - 容量上限: スクロールで画面外へ流れた古い要求を捨て、誰も見ていないサムネ生成でワーカーを
    //    占有し続けない(無制限だと繰り返しスクロールで在庫が膨らみ、見える要求まで待たされた)。
    //    捨てた項目はスクロールで戻れば呼び出し側が再要求する。
    private static readonly object Sync = new();
    private static readonly BoundedLifoQueue<string, Request> Pending = new(MaxPending);
    // 同時生成数の上限(大量画像フォルダーで CPU/ディスクを飽和させない)。
    private static readonly int MaxWorkers = Math.Max(2, Environment.ProcessorCount / 2);
    // 未処理要求の保持上限(数画面分の先読みで十分。超過分=古い画面外要求は捨てる)。
    private const int MaxPending = 512;
    // 稼働中ワーカー数(MaxWorkers まで)。
    private static int _activeWorkers;
    // キャッシュ上限(超えたら丸ごと破棄してメモリを抑える。性能最適化なので素朴で十分)。
    private const int CacheCap = 2000;

    private readonly record struct Request(
        string Path, bool IsDirectory, int Size, Action<ImageSource> OnLoaded, Action OnDropped);

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
    public static void LoadAsync(
        string path, bool isDirectory, int size, Action<ImageSource> onLoaded, Action onDropped)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        // 後着優先で積み(古い画面外要求は容量上限で捨てる)、空きワーカーがあれば起動する。
        // 実在チェック(disk I/O)はワーカー側で行い、UI スレッドを止めない。
        bool dropped;
        Request droppedReq;
        lock (Sync)
            dropped = Pending.Push(Key(path, size), new Request(path, isDirectory, size, onLoaded, onDropped), out droppedReq);
        // 容量超過で捨てた要求は呼び出し側へ通知し、再表示時に再要求できるようにする(UI スレッド)。
        if (dropped) droppedReq.OnDropped();
        EnsureWorker(dispatcher);
    }

    /// <summary>ワーカー数が上限未満なら1つ起動する(キューを処理し切るまで回す)。</summary>
    private static void EnsureWorker(Dispatcher dispatcher)
    {
        while (true)
        {
            var current = Volatile.Read(ref _activeWorkers);
            if (current >= MaxWorkers) return;
            if (Interlocked.CompareExchange(ref _activeWorkers, current + 1, current) == current) break;
        }
        Task.Run(() => Worker(dispatcher));
    }

    /// <summary>後着優先で要求を取り出し、サムネイルを生成して UI スレッドへ反映する。</summary>
    private static void Worker(Dispatcher dispatcher)
    {
        try
        {
            while (true)
            {
                Request req;
                lock (Sync) { if (!Pending.TryPop(out req)) break; }

                // 実ファイル/実フォルダーのみ対象(書庫内の仮想パス等はシェルが解決できない)。背景で判定。
                if (!File.Exists(req.Path) && !Directory.Exists(req.Path)) continue;

                var key = Key(req.Path, req.Size);
                if (!Cache.TryGetValue(key, out var image))
                {
                    image = Create(req.Path, req.IsDirectory, req.Size);
                    if (image is null) continue;
                    if (Cache.Count >= CacheCap) Cache.Clear();
                    Cache[key] = image;
                }
                var result = image;
                var callback = req.OnLoaded;
                _ = dispatcher.BeginInvoke(new Action(() => callback(result)));
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeWorkers);
            // 取り出し失敗と減算の隙間に積まれた要求を取りこぼさない。
            bool hasMore;
            lock (Sync) hasMore = Pending.Count > 0;
            if (hasMore) EnsureWorker(dispatcher);
        }
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
