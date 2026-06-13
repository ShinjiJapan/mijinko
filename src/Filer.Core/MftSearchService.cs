using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Filer.Core;

/// <summary>
/// NTFS の MFT 直読み(Everything 方式)によるファイル検索。
/// 初回はボリューム全体の MFT を列挙して <see cref="MftVolumeIndex"/> を構築し、
/// 以降は USN ジャーナルの差分だけを適用するため再検索がほぼ瞬時になる。
/// ボリュームの読み取りには管理者権限が必要。使えない場合は false と理由を返し、
/// 呼び出し側(FileSearcher)が通常のディレクトリ走査へ切り替える(理由は UI に明示)。
/// </summary>
public static class MftSearchService
{
    private sealed class CachedVolume
    {
        public MftVolumeIndex? Index;
        public bool JournalUsable;
    }

    private static readonly Dictionary<string, CachedVolume> _volumes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _gate = new();

    /// <summary>
    /// MFT 索引で検索する。索引を使えないボリューム・権限の場合は false と理由(note)を返す。
    /// 成功時の results は未ソート。キャンセル時は例外を投げず、それまでの結果を返す。
    /// </summary>
    public static bool TrySearch(FileSearchOptions options, string baseDir, FileNameMatcher matcher,
        CancellationToken token, Action<IReadOnlyList<FileEntry>>? onBatch,
        out List<FileEntry> results, out string? note)
    {
        results = new List<FileEntry>();

        var root = Path.GetPathRoot(baseDir);
        if (root is not { Length: 3 } || root[1] != ':')
        {
            note = "MFT: ローカルドライブ以外のため通常走査";
            return false;
        }

        var baseFrn = UsnInterop.GetFileReferenceNumber(baseDir);
        var rootFrn = UsnInterop.GetFileReferenceNumber(root);
        if (baseFrn == 0 || rootFrn == 0)
        {
            note = "MFT: 基準ディレクトリの ID を取得できないため通常走査";
            return false;
        }

        CachedVolume volume;
        lock (_gate)
        {
            if (!_volumes.TryGetValue(root, out volume!))
                _volumes[root] = volume = new CachedVolume();
        }

        MftVolumeIndex index;
        lock (volume)
        {
            if (!TryGetFreshIndex(volume, root, rootFrn, token, out index!, out note))
                return false;
            if (token.IsCancellationRequested)
                return true;   // 構築中キャンセル: MFT エンジン扱いで空結果

            // 索引走査(名前一致 + スコープ判定)はロック内で行う(索引はスレッド安全でないため)。
            var candidates = new List<(string Path, bool IsDir, bool Matched, bool IsZip)>();
            index.Scan(matcher, options.SearchArchives, baseFrn,
                options.IncludeFiles, options.IncludeDirectories,
                (path, isDir, matched, isZip) => candidates.Add((path, isDir, matched, isZip)),
                token);

            // 実体化(属性取得・zip 内検索)はファイル I/O なのでロック外・並列で行う。
            Materialize(candidates, options, baseDir, matcher, token, onBatch, results);
        }
        return true;
    }

    /// <summary>キャッシュ済み索引を USN 差分で最新化する。無ければ MFT 全列挙で構築する。</summary>
    private static bool TryGetFreshIndex(CachedVolume volume, string root, ulong rootFrn,
        CancellationToken token, out MftVolumeIndex? index, out string? note)
    {
        index = null;

        using var handle = UsnInterop.CreateFileW(@"\\.\" + root.TrimEnd('\\'),
            UsnInterop.GenericRead, UsnInterop.FileShareReadWriteDelete,
            IntPtr.Zero, UsnInterop.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            note = Marshal.GetLastWin32Error() == UsnInterop.ErrorAccessDenied
                ? "MFT: 管理者権限がないため通常走査"
                : $"MFT: ボリュームを開けないため通常走査 (エラー {Marshal.GetLastWin32Error()})";
            return false;
        }

        // USN ジャーナル位置の取得。無効なら差分更新できないため毎回全読込になる。
        var hasJournal = UsnInterop.DeviceIoControl(handle, UsnInterop.FsctlQueryUsnJournal,
            IntPtr.Zero, 0, out var journal, Marshal.SizeOf<UsnInterop.UsnJournalData>(), out _, IntPtr.Zero);

        if (volume.Index is { } cached && volume.JournalUsable && hasJournal &&
            cached.JournalId == journal.UsnJournalID &&
            TryApplyJournalDelta(handle, cached, root, token))
        {
            index = cached;
            note = "MFT索引(差分更新)";
            return true;
        }

        // 全構築。FSCTL_QUERY_FILE_LAYOUT(ハードリンクの全名前を取得できる)を第一候補とし、
        // 非対応のボリュームでは FSCTL_ENUM_USN_DATA(1 ファイル 1 名前)を使う。
        var fresh = new MftVolumeIndex(root, rootFrn);
        string? buildSuffix = null;
        var layout = TryEnumerateFileLayout(handle, fresh, token, out note);
        if (layout == LayoutResult.Unsupported)
        {
            fresh = new MftVolumeIndex(root, rootFrn);   // 部分的に入った内容を捨てて作り直す
            if (!TryEnumerateMft(handle, fresh, hasJournal ? journal.NextUsn : long.MaxValue, token, out note))
            {
                volume.Index = null;
                return false;
            }
            buildSuffix = "・ハードリンクの別名は対象外";
        }
        else if (layout == LayoutResult.Failed)
        {
            volume.Index = null;
            return false;
        }

        if (token.IsCancellationRequested)
        {
            volume.Index = null;   // 不完全な索引はキャッシュしない
            index = fresh;
            note = "MFT索引(構築中にキャンセル)";
            return true;
        }

        fresh.JournalId = journal.UsnJournalID;
        fresh.NextUsn = journal.NextUsn;
        volume.Index = fresh;
        volume.JournalUsable = hasJournal;
        index = fresh;
        note = (hasJournal ? "MFT索引(全読込)" : "MFT索引(ジャーナル無効: 毎回全読込)") + buildSuffix;
        return true;
    }

    private enum LayoutResult { Done, Unsupported, Failed }

    /// <summary>
    /// FSCTL_QUERY_FILE_LAYOUT で全ファイルの全名前(ハードリンク含む)を列挙して索引を構築する。
    /// </summary>
    private static LayoutResult TryEnumerateFileLayout(Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        MftVolumeIndex index, CancellationToken token, out string? note)
    {
        note = null;
        var input = new UsnInterop.QueryFileLayoutInput
        {
            FilterEntryCount = 0,
            Flags = UsnInterop.QueryFileLayoutRestart | UsnInterop.QueryFileLayoutIncludeNames,
            FilterType = 0,
        };
        var buffer = new byte[1 << 20];
        var first = true;

        while (true)
        {
            if (token.IsCancellationRequested) return LayoutResult.Done;

            if (!UsnInterop.DeviceIoControl(handle, UsnInterop.FsctlQueryFileLayout,
                    ref input, Marshal.SizeOf<UsnInterop.QueryFileLayoutInput>(),
                    buffer, buffer.Length, out var returned, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == UsnInterop.ErrorHandleEof) return LayoutResult.Done;   // 列挙完了
                if (first)
                    return LayoutResult.Unsupported;   // 古い OS・非対応ボリューム → USN 列挙へ
                note = $"MFT: ファイルレイアウト列挙に失敗したため通常走査 (エラー {error})";
                return LayoutResult.Failed;
            }

            ApplyFileLayout(buffer.AsSpan(0, returned), index);
            input.Flags = UsnInterop.QueryFileLayoutIncludeNames;   // RESTART は初回のみ
            first = false;
        }
    }

    /// <summary>QUERY_FILE_LAYOUT の 1 バッファ分(FILE_LAYOUT_ENTRY 列)を索引へ反映する。</summary>
    private static void ApplyFileLayout(ReadOnlySpan<byte> buffer, MftVolumeIndex index)
    {
        if (buffer.Length < Marshal.SizeOf<UsnInterop.QueryFileLayoutOutput>()) return;
        var header = MemoryMarshal.Read<UsnInterop.QueryFileLayoutOutput>(buffer);
        if (header.FileEntryCount == 0 || header.FirstFileOffset == 0) return;

        var entrySize = Marshal.SizeOf<UsnInterop.FileLayoutEntry>();
        var nameSize = Marshal.SizeOf<UsnInterop.FileLayoutNameEntry>();
        var fileOffset = (int)header.FirstFileOffset;

        while (fileOffset > 0 && fileOffset + entrySize <= buffer.Length)
        {
            var entry = MemoryMarshal.Read<UsnInterop.FileLayoutEntry>(buffer[fileOffset..]);
            var isDir = (entry.FileAttributes & UsnInterop.FileAttributeDirectory) != 0;

            var namePos = entry.FirstNameOffset == 0 ? 0 : fileOffset + (int)entry.FirstNameOffset;
            while (namePos > 0 && namePos + nameSize <= buffer.Length)
            {
                var nameEntry = MemoryMarshal.Read<UsnInterop.FileLayoutNameEntry>(buffer[namePos..]);
                // 8.3 別名(DOS のみ)はスキップ。それ以外(主名・追加のハードリンク名)を登録する。
                if (nameEntry.Flags != UsnInterop.FileLayoutNameEntryDos &&
                    namePos + nameSize + nameEntry.FileNameLength <= buffer.Length)
                {
                    var name = MemoryMarshal.Cast<byte, char>(
                        buffer.Slice(namePos + nameSize, (int)nameEntry.FileNameLength));
                    index.AddName(entry.FileReferenceNumber, nameEntry.ParentFileReferenceNumber, name, isDir);
                }
                namePos = nameEntry.NextNameOffset == 0 ? 0 : namePos + (int)nameEntry.NextNameOffset;
            }

            fileOffset = entry.NextFileOffset == 0 ? 0 : fileOffset + (int)entry.NextFileOffset;
        }
    }

    /// <summary>MFT 全列挙(FSCTL_ENUM_USN_DATA)で索引を構築する。</summary>
    private static bool TryEnumerateMft(Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        MftVolumeIndex index, long highUsn, CancellationToken token, out string? note)
    {
        var input = new UsnInterop.MftEnumData { StartFileReferenceNumber = 0, LowUsn = 0, HighUsn = highUsn };
        var buffer = new byte[1 << 20];

        while (true)
        {
            if (token.IsCancellationRequested) { note = null; return true; }

            if (!UsnInterop.DeviceIoControl(handle, UsnInterop.FsctlEnumUsnData,
                    ref input, Marshal.SizeOf<UsnInterop.MftEnumData>(),
                    buffer, buffer.Length, out var returned, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == UsnInterop.ErrorHandleEof) { note = null; return true; }   // 列挙完了
                note = $"MFT: 列挙に失敗したため通常走査 (エラー {error})";
                return false;
            }
            if (returned <= 8) { note = null; return true; }

            ApplyRecords(buffer.AsSpan(0, returned), index, applyDeletes: false);
            input.StartFileReferenceNumber = BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        }
    }

    /// <summary>前回位置からの USN ジャーナル差分を索引へ適用する。失敗(パージ済み等)なら false=全再構築。</summary>
    private static bool TryApplyJournalDelta(Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        MftVolumeIndex index, string root, CancellationToken token)
    {
        var input = new UsnInterop.ReadUsnJournalData
        {
            StartUsn = index.NextUsn,
            ReasonMask = 0xFFFFFFFF,
            ReturnOnlyOnClose = 0,
            Timeout = 0,
            BytesToWaitFor = 0,
            UsnJournalID = index.JournalId,
        };
        var buffer = new byte[1 << 20];
        var hardLinkChanged = new HashSet<ulong>();

        while (true)
        {
            if (token.IsCancellationRequested) return true;   // 部分適用でも NextUsn は読んだ分まで進んでいる

            if (!UsnInterop.DeviceIoControl(handle, UsnInterop.FsctlReadUsnJournal,
                    ref input, Marshal.SizeOf<UsnInterop.ReadUsnJournalData>(),
                    buffer, buffer.Length, out var returned, IntPtr.Zero))
                return false;   // 位置パージ・ID 不一致など。呼び出し側で全再構築する
            if (returned < 8) return false;

            var nextUsn = BinaryPrimitives.ReadInt64LittleEndian(buffer);
            ApplyRecords(buffer.AsSpan(0, returned), index, applyDeletes: true, hardLinkChanged);
            index.NextUsn = nextUsn;
            if (returned <= 8) break;   // 末尾(レコードなし)まで読み切った
            input.StartUsn = nextUsn;
        }

        ResolveHardLinkNames(index, root, hardLinkChanged, token);
        return true;
    }

    /// <summary>
    /// ハードリンクが増減したファイルの名前一覧を実ファイルシステムから取り直して索引を更新する。
    /// (USN レコードからは「どの名前が増えた/減ったか」を確実に判別できないため。)
    /// </summary>
    private static void ResolveHardLinkNames(MftVolumeIndex index, string root,
        HashSet<ulong> changed, CancellationToken token)
    {
        if (changed.Count == 0) return;
        var rootPrefix = root.TrimEnd('\\');                       // "C:"
        var parentFrnCache = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

        foreach (var frn in changed)
        {
            if (token.IsCancellationRequested) return;

            if (index.PrimaryPathOf(frn) is not { } currentPath) continue;   // 索引未登録(直後の作成等)
            var linkNames = UsnInterop.GetHardLinkNames(currentPath);
            if (linkNames is null || linkNames.Count == 0)
            {
                index.Remove(frn);                                 // 実体が消えている
                continue;
            }

            var firstName = true;
            foreach (var link in linkNames)                        // link は "\dir\name"(ルート相対)
            {
                var slash = link.LastIndexOf('\\');
                if (slash < 0) continue;
                var parentPath = slash == 0 ? root : rootPrefix + link[..slash];
                if (!parentFrnCache.TryGetValue(parentPath, out var parentFrn))
                    parentFrnCache[parentPath] = parentFrn = UsnInterop.GetFileReferenceNumber(parentPath);
                if (parentFrn == 0) continue;

                var name = link.AsSpan(slash + 1);
                if (firstName)
                {
                    index.Set(frn, parentFrn, name, isDirectory: false);   // 主名を置き換え(旧追加名は破棄)
                    firstName = false;
                }
                else
                {
                    index.AddName(frn, parentFrn, name, isDirectory: false);
                }
            }
        }
    }

    /// <summary>バッファ中の USN_RECORD_V2 列を索引へ反映する(先頭 8 バイトは次位置なのでスキップ)。</summary>
    private static void ApplyRecords(ReadOnlySpan<byte> buffer, MftVolumeIndex index,
        bool applyDeletes, HashSet<ulong>? hardLinkChanged = null)
    {
        var offset = 8;
        while (offset + 60 <= buffer.Length)
        {
            var record = MemoryMarshal.Read<UsnInterop.UsnRecordV2>(buffer[offset..]);
            if (record.RecordLength < 60 || offset + record.RecordLength > buffer.Length)
                break;
            if (record.MajorVersion == 2)
            {
                if (applyDeletes && (record.Reason & UsnInterop.UsnReasonFileDelete) != 0)
                {
                    index.Remove(record.FileReferenceNumber);
                    hardLinkChanged?.Remove(record.FileReferenceNumber);
                }
                else if (hardLinkChanged is not null &&
                         (record.Reason & UsnInterop.UsnReasonHardLinkChange) != 0)
                {
                    // リンクの増減はレコード単体で増えた名前か消えた名前か判別できないため、
                    // 差分適用後に実 FS から名前一覧を取り直す。
                    hardLinkChanged.Add(record.FileReferenceNumber);
                }
                else
                {
                    var name = MemoryMarshal.Cast<byte, char>(
                        buffer.Slice(offset + record.FileNameOffset, record.FileNameLength));
                    index.Set(record.FileReferenceNumber, record.ParentFileReferenceNumber, name,
                        (record.FileAttributes & UsnInterop.FileAttributeDirectory) != 0);
                }
            }
            offset += (int)record.RecordLength;
        }
    }

    /// <summary>
    /// 索引が返した候補パスを FileEntry へ実体化する(属性取得は並列)。
    /// 属性を取得できないもの(削除済み・アクセス不可)は通常走査の IgnoreInaccessible と同様にスキップ。
    /// </summary>
    private static void Materialize(List<(string Path, bool IsDir, bool Matched, bool IsZip)> candidates,
        FileSearchOptions options, string baseDir, FileNameMatcher matcher,
        CancellationToken token, Action<IReadOnlyList<FileEntry>>? onBatch, List<FileEntry> results)
    {
        var relStart = baseDir.Length + 1;
        try
        {
            Parallel.ForEach(candidates,
                new ParallelOptions { CancellationToken = token },
                () => new List<FileEntry>(),
                (candidate, _, local) =>
                {
                    if (candidate.Matched &&
                        UsnInterop.GetFileAttributesExW(ToExtendedPath(candidate.Path), 0, out var data))
                    {
                        var size = candidate.IsDir ? 0 : ((long)data.FileSizeHigh << 32) | data.FileSizeLow;
                        local.Add(new FileEntry(candidate.Path[relStart..], candidate.Path,
                            candidate.IsDir, size, UsnInterop.ToDateTime(data.LastWriteTime))
                        {
                            IsArchive = !candidate.IsDir && ArchivePath.HasArchiveExtension(candidate.Path),
                        });
                    }
                    if (candidate.IsZip)
                        FileSearcher.ScanArchiveEntries(candidate.Path, matcher,
                            options.IncludeFiles, options.IncludeDirectories, relStart, token, local.Add);
                    return local;
                },
                local =>
                {
                    if (local.Count == 0) return;
                    lock (results) results.AddRange(local);
                    onBatch?.Invoke(local);
                });
        }
        catch (OperationCanceledException)
        {
            // キャンセル: マージ済みの分だけ返す(通常走査エンジンと同じ振る舞い)
        }
    }

    /// <summary>MAX_PATH 超のパスは \\?\ プレフィックスを付けて属性取得する。</summary>
    private static string ToExtendedPath(string path) =>
        path.Length < 260 ? path : @"\\?\" + path;
}
