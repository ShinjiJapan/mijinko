using System.Runtime.InteropServices;

namespace Filer.Core;

/// <summary>
/// Windows シェルの SHFileOperation を用いてファイル/ディレクトリをごみ箱へ送る。
/// 確認ダイアログ・進捗UI・エラーUI を出さず(FOF_*)、UNDO 可能(FOF_ALLOWUNDO)で削除する。
/// </summary>
internal static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    /// <summary>絶対パスの項目をごみ箱へ送る。失敗時(存在しない等)は IOException。</summary>
    public static void Send(string path)
    {
        // 相対パスはカレントディレクトリ基準に解釈されるため絶対化する。
        var full = Path.GetFullPath(path);
        if (!File.Exists(full) && !Directory.Exists(full))
            throw new IOException($"対象が存在しません: {full}");

        // pFrom は複数パスを '\0' で区切り末尾を二重 '\0' で終端する API 仕様。
        // マネージ文字列のマーシャリングは末尾に1つ '\0' を付与するため、ここでは1つだけ足す。
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = full + '\0',
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
        };

        var result = SHFileOperation(ref op);
        if (result != 0)
            throw new IOException($"ごみ箱への移動に失敗しました (code=0x{result:X}): {full}");
        if (op.fAnyOperationsAborted != 0)
            throw new IOException($"ごみ箱への移動が中断されました: {full}");
    }
}
