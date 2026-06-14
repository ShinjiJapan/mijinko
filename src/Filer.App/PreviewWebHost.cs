using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Filer.App;

/// <summary>
/// アプリ内 WebView2 ビュー(プレビュー/差分)で共有する下回り。作業フォルダーの確保・
/// WebView2 環境生成・仮想ホスト名・プレビューを重ねるペイン領域の矩形計算をまとめる。
/// </summary>
internal static class PreviewWebHost
{
    /// <summary>仮想ホスト名(作業フォルダーを https オリジンとして公開するときのホスト)。</summary>
    public const string Host = "filer.preview";

    /// <summary>
    /// Markdown が参照する画像など、表示対象ファイル自身のフォルダーを公開する仮想ホスト名。
    /// 相対パス画像を解決するため、<see cref="Host"/>(作業フォルダー)とは別に張る。
    /// </summary>
    public const string DocHost = "filer.doc";

    /// <summary>%LocalAppData%\Filer を確保して返す。</summary>
    public static string FilerLocalDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Filer");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>HTML/PDF 等の一時ファイルを書き出す作業フォルダー(%LocalAppData%\Filer\preview)。</summary>
    public static string PreviewDir()
    {
        var dir = Path.Combine(FilerLocalDir(), "preview");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>共有のユーザーデータフォルダーで WebView2 環境を生成する。</summary>
    public static Task<CoreWebView2Environment> CreateEnvironmentAsync() =>
        CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(FilerLocalDir(), "WebView2"));

    /// <summary>
    /// プレビューを重ねるペイン領域(反対側ペイン)のスクリーン矩形(DIP)を求める。取得できなければ null。
    /// </summary>
    public static Rect? GetPaneRegionRect(FrameworkElement? paneRegion)
    {
        if (paneRegion is null || !paneRegion.IsLoaded
            || paneRegion.ActualWidth <= 0 || paneRegion.ActualHeight <= 0)
            return null;

        var source = PresentationSource.FromVisual(paneRegion);
        if (source?.CompositionTarget is null) return null;

        // PointToScreen は物理ピクセル。Window.Left/Top/Width/Height は DIP なので変換する。
        var topLeftDevice = paneRegion.PointToScreen(new Point(0, 0));
        var bottomRightDevice = paneRegion.PointToScreen(
            new Point(paneRegion.ActualWidth, paneRegion.ActualHeight));
        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var topLeft = fromDevice.Transform(topLeftDevice);
        var bottomRight = fromDevice.Transform(bottomRightDevice);
        return new Rect(topLeft, bottomRight);
    }
}
