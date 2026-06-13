using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Filer.Core;

/// <summary>
/// MFT/USN ジャーナル読み取り用の Win32 P/Invoke。
/// ボリュームハンドル(\\.\C:)の取得には管理者権限が必要。
/// </summary>
internal static class UsnInterop
{
    public const uint GenericRead = 0x80000000;
    public const uint FileReadAttributes = 0x0080;
    public const uint FileShareReadWriteDelete = 0x1 | 0x2 | 0x4;
    public const uint OpenExisting = 3;
    public const uint FileFlagBackupSemantics = 0x02000000;

    public const uint FsctlQueryUsnJournal = 0x000900F4;
    public const uint FsctlEnumUsnData = 0x000900B3;
    public const uint FsctlReadUsnJournal = 0x000900BB;
    public const uint FsctlQueryFileLayout = 0x00090277;

    // QUERY_FILE_LAYOUT_INPUT.Flags
    public const uint QueryFileLayoutRestart = 0x1;
    public const uint QueryFileLayoutIncludeNames = 0x2;

    // FILE_LAYOUT_NAME_ENTRY.Flags(DOS のみ = 8.3 別名なのでスキップする)
    public const uint FileLayoutNameEntryPrimary = 0x1;
    public const uint FileLayoutNameEntryDos = 0x2;

    public const int ErrorHandleEof = 38;
    public const int ErrorAccessDenied = 5;
    public const int ErrorJournalNotActive = 0x49B;          // 1179
    public const int ErrorJournalDeleteInProgress = 0x49A;   // 1178
    public const int ErrorJournalEntryDeleted = 0x49D;       // 1181(読み出し位置がパージ済み)

    public const uint FileAttributeDirectory = 0x10;
    public const uint UsnReasonFileDelete = 0x00000200;
    public const uint UsnReasonHardLinkChange = 0x00004000;

    [StructLayout(LayoutKind.Sequential)]
    public struct UsnJournalData
    {
        public ulong UsnJournalID;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MftEnumData
    {
        public ulong StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ReadUsnJournalData
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
    }

    /// <summary>USN_RECORD_V2 の固定部(名前は FileNameOffset から FileNameLength バイトの UTF-16)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsnRecordV2
    {
        public uint RecordLength;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ulong FileReferenceNumber;
        public ulong ParentFileReferenceNumber;
        public long Usn;
        public long TimeStamp;
        public uint Reason;
        public uint SourceInfo;
        public uint SecurityId;
        public uint FileAttributes;
        public ushort FileNameLength;
        public ushort FileNameOffset;
    }

    /// <summary>
    /// QUERY_FILE_LAYOUT_INPUT(フィルターなし)。union 部は最大の固定長(32 バイト)を 0 で確保する。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct QueryFileLayoutInput
    {
        public uint FilterEntryCount;   // 0 = フィルターなし
        public uint Flags;
        public uint FilterType;         // 0 = QUERY_FILE_LAYOUT_FILTER_TYPE_NONE
        public uint Reserved;
        public ulong FilterPad0, FilterPad1, FilterPad2, FilterPad3;
    }

    /// <summary>QUERY_FILE_LAYOUT_OUTPUT ヘッダー(後続に FILE_LAYOUT_ENTRY 列)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct QueryFileLayoutOutput
    {
        public uint FileEntryCount;
        public uint FirstFileOffset;    // バッファ先頭からのオフセット
        public uint Flags;
        public uint Reserved;
    }

    /// <summary>FILE_LAYOUT_ENTRY 固定部(オフセットはこのエントリ先頭からの相対)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FileLayoutEntry
    {
        public uint Version;
        public uint NextFileOffset;     // 0 = 最後
        public uint Flags;
        public uint FileAttributes;
        public ulong FileReferenceNumber;
        public uint FirstNameOffset;    // 0 = 名前なし
        public uint FirstStreamOffset;
        public uint ExtraInfoOffset;
        public uint ExtraInfoLength;
    }

    /// <summary>FILE_LAYOUT_NAME_ENTRY 固定部(直後に FileNameLength バイトの UTF-16 名)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FileLayoutNameEntry
    {
        public uint NextNameOffset;     // 0 = 最後(このエントリ先頭からの相対)
        public uint Flags;
        public ulong ParentFileReferenceNumber;
        public uint FileNameLength;     // バイト数
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Win32FileAttributeData
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess,
        uint shareMode, IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
        ref MftEnumData inBuffer, int inBufferSize, byte[] outBuffer, int outBufferSize,
        out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
        ref ReadUsnJournalData inBuffer, int inBufferSize, byte[] outBuffer, int outBufferSize,
        out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
        IntPtr inBuffer, int inBufferSize, out UsnJournalData outBuffer, int outBufferSize,
        out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
        ref QueryFileLayoutInput inBuffer, int inBufferSize, byte[] outBuffer, int outBufferSize,
        out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetFileInformationByHandle(SafeFileHandle file,
        out ByHandleFileInformation fileInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindFirstFileNameW(string fileName, uint flags,
        ref uint stringLength, char[] linkName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool FindNextFileNameW(IntPtr findStream, ref uint stringLength, char[] linkName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FindClose(IntPtr findStream);

    public static readonly IntPtr InvalidHandleValue = new(-1);

    /// <summary>
    /// ファイルの全ハードリンク名(ボリュームルート相対の "\dir\name" 形式)を列挙する。
    /// 開けない・消えている場合は null。
    /// </summary>
    public static List<string>? GetHardLinkNames(string filePath)
    {
        var buffer = new char[1024];
        var length = (uint)buffer.Length;
        var handle = FindFirstFileNameW(filePath, 0, ref length, buffer);
        if (handle == InvalidHandleValue) return null;

        var names = new List<string>();
        try
        {
            names.Add(BufferToString(buffer));
            while (true)
            {
                length = (uint)buffer.Length;
                if (!FindNextFileNameW(handle, ref length, buffer)) break;
                names.Add(BufferToString(buffer));
            }
        }
        finally
        {
            FindClose(handle);
        }
        return names;

        // StringLength の終端 NUL の扱いが紛らわしいため、NUL までを名前として取り出す。
        static string BufferToString(char[] buffer)
        {
            var nul = Array.IndexOf(buffer, '\0');
            return new string(buffer, 0, nul < 0 ? buffer.Length : nul);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetFileAttributesExW(string fileName, int infoLevelId,
        out Win32FileAttributeData fileInformation);

    /// <summary>FILETIME → DateTime(ローカル)。無効値は default。</summary>
    public static DateTime ToDateTime(System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        var ticks = ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
        return ticks <= 0 ? default : DateTime.FromFileTime(ticks);
    }

    /// <summary>ディレクトリ/ファイルの FRN(64bit ファイル ID)を取得する。失敗時 0。</summary>
    public static ulong GetFileReferenceNumber(string path)
    {
        using var handle = CreateFileW(path, FileReadAttributes, FileShareReadWriteDelete,
            IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) return 0;
        if (!GetFileInformationByHandle(handle, out var info)) return 0;
        return ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
    }
}
