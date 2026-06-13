using System.Collections.Concurrent;
using System.IO.Compression;
using System.IO.Enumeration;
using System.Text.RegularExpressions;

namespace Filer.Core;

/// <summary>ファイル検索の条件。</summary>
public sealed record FileSearchOptions(string Pattern, string BaseDirectory)
{
    /// <summary>Pattern を正規表現として解釈するか(false なら部分一致。*? はワイルドカード)。</summary>
    public bool UseRegex { get; init; }

    /// <summary>ファイルを検索対象に含めるか。</summary>
    public bool IncludeFiles { get; init; } = true;

    /// <summary>ディレクトリを検索対象に含めるか。</summary>
    public bool IncludeDirectories { get; init; }

    /// <summary>書庫(.zip)内のエントリも検索するか。</summary>
    public bool SearchArchives { get; init; }

    /// <summary>
    /// NTFS の MFT 直読み(Everything 方式)を優先するか。
    /// 使えない環境(権限なし・非 NTFS 等)では自動的にディレクトリ走査になる(結果に理由を明示)。
    /// </summary>
    public bool PreferMft { get; init; } = true;
}

/// <summary>検索に使われたエンジン。</summary>
public enum FileSearchEngine
{
    /// <summary>ディレクトリの並列走査。</summary>
    DirectoryScan,
    /// <summary>NTFS MFT 索引(Everything 方式)。</summary>
    MftIndex,
}

/// <summary>検索結果と、使用エンジン・補足(MFT が使えなかった理由など)。</summary>
public sealed record FileSearchResult(
    IReadOnlyList<FileEntry> Entries, FileSearchEngine Engine, string? EngineNote);

/// <summary>ファイル名(パスを含まない)に対する一致判定。span 受けでアロケーションを抑える。</summary>
public delegate bool FileNameMatcher(ReadOnlySpan<char> name);

/// <summary>
/// 基準ディレクトリ配下を再帰的に検索する。複数ワーカーでディレクトリ単位に並列走査し、
/// 低アロケーションの <see cref="FileSystemEnumerable{TResult}"/> で列挙する(高速化のため)。
/// 結果の <see cref="FileEntry.Name"/> は基準ディレクトリからの相対パス。
/// </summary>
public static class FileSearcher
{
    /// <summary>
    /// 検索パターンから一致判定を作る。空=全一致、*? を含めば名前全体へのワイルドカード、
    /// それ以外は部分一致(大小無視)。useRegex なら正規表現(不正なら false と error)。
    /// </summary>
    public static bool TryCreateMatcher(string pattern, bool useRegex,
        out FileNameMatcher matcher, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(pattern))
        {
            matcher = static _ => true;
            return true;
        }

        if (useRegex)
        {
            Regex regex;
            try
            {
                regex = new Regex(pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            }
            catch (ArgumentException ex)
            {
                matcher = static _ => false;
                error = ex.Message;
                return false;
            }
            matcher = name => regex.IsMatch(name.ToString());
            return true;
        }

        if (pattern.AsSpan().IndexOfAny('*', '?') >= 0)
        {
            // ワイルドカードは名前全体に対して照合する("*.md" は末尾一致)。
            var translated = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            var regex = new Regex(translated,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            matcher = name => regex.IsMatch(name.ToString());
            return true;
        }

        matcher = name => name.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    /// <summary>
    /// 検索を実行し、見つかった全エントリを相対パス順で返す。
    /// <paramref name="onBatch"/> は発見バッチごとに呼ばれる(**ワーカースレッドから並行に**呼ばれうる)。
    /// キャンセル時は例外を投げず、それまでの結果を返す。
    /// </summary>
    public static IReadOnlyList<FileEntry> Search(FileSearchOptions options,
        CancellationToken token = default, Action<IReadOnlyList<FileEntry>>? onBatch = null)
        => SearchWithInfo(options, token, onBatch).Entries;

    /// <summary>
    /// <see cref="Search"/> と同じ検索を行い、使用エンジン(MFT 索引 or ディレクトリ走査)と
    /// 補足(MFT が使えなかった理由など)も返す。
    /// </summary>
    public static FileSearchResult SearchWithInfo(FileSearchOptions options,
        CancellationToken token = default, Action<IReadOnlyList<FileEntry>>? onBatch = null)
    {
        if (!TryCreateMatcher(options.Pattern, options.UseRegex, out var matcher, out var error))
            throw new ArgumentException(error, nameof(options));

        var baseDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.BaseDirectory));

        string? mftNote = null;
        if (options.PreferMft)
        {
            if (MftSearchService.TrySearch(options, baseDir, matcher, token, onBatch,
                    out var mftResults, out mftNote))
            {
                SortByName(mftResults);
                return new FileSearchResult(mftResults, FileSearchEngine.MftIndex, mftNote);
            }
            // mftNote に「使えなかった理由」が入る。通常走査で続行し、結果に理由を残す。
        }

        var ctx = new SearchContext(options, matcher, baseDir, token, onBatch);
        ctx.EnqueueDirectory(baseDir);

        var workerCount = Math.Clamp(Environment.ProcessorCount, 2, 16);
        var workers = new Task[workerCount];
        for (var i = 0; i < workerCount; i++)
            workers[i] = Task.Run(ctx.DrainQueue, CancellationToken.None);
        Task.WaitAll(workers);

        return new FileSearchResult(ctx.TakeSortedResults(), FileSearchEngine.DirectoryScan, mftNote);
    }

    private static void SortByName(List<FileEntry> entries) =>
        entries.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 基準ディレクトリ配下のフルパスから相対パスを切り出す開始位置。
    /// 基準がドライブ直下("H:\")など区切りで終わる場合は、その先に区切りが無いので余分に1文字スキップしない
    /// (これをしないと相対パスの先頭1文字が欠ける)。
    /// </summary>
    public static int RelativeStart(string baseDir) =>
        baseDir.Length + (Path.EndsInDirectorySeparator(baseDir) ? 0 : 1);

    /// <summary>基準ディレクトリ配下のフルパスを基準相対のパスに変換する。</summary>
    public static string MakeRelative(string baseDir, string fullPath)
    {
        var start = RelativeStart(baseDir);
        return fullPath.Length > start ? fullPath[start..] : fullPath;
    }

    /// <summary>
    /// 書庫(.zip)内のエントリを検索して通知する(走査エンジン・MFT エンジン共用)。
    /// 仮想パスは「書庫パス\内部パス」、Name は基準ディレクトリ相対(relStart 文字目以降)。
    /// 開けない・壊れた書庫は検索対象外として読み飛ばす。
    /// </summary>
    internal static void ScanArchiveEntries(string zipPath, FileNameMatcher matcher,
        bool includeFiles, bool includeDirectories, int relStart,
        CancellationToken token, Action<FileEntry> add)
    {
        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(zipPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return;
        }

        using (zip)
        {
            foreach (var entry in zip.Entries)
            {
                if (token.IsCancellationRequested) return;

                var isDirEntry = entry.FullName.EndsWith('/');
                var name = entry.Name;
                if (isDirEntry)
                {
                    if (!includeDirectories) continue;
                    name = LastZipSegment(entry.FullName);
                }
                else if (!includeFiles)
                {
                    continue;
                }

                if (!matcher(name)) continue;

                var inner = entry.FullName.Replace('/', '\\').TrimEnd('\\');
                var full = zipPath + "\\" + inner;
                add(isDirEntry
                    ? new FileEntry(full[relStart..], full, true, 0, entry.LastWriteTime.LocalDateTime)
                    : new FileEntry(full[relStart..], full, false, entry.Length, entry.LastWriteTime.LocalDateTime));
            }
        }
    }

    private static string LastZipSegment(string zipEntryFullName)
    {
        var trimmed = zipEntryFullName.TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        return idx < 0 ? trimmed : trimmed[(idx + 1)..];
    }

    /// <summary>1回の検索で全ワーカーが共有する状態(キュー・結果・条件)。</summary>
    private sealed class SearchContext
    {
        private static readonly EnumerationOptions EnumOptions = new()
        {
            IgnoreInaccessible = true,
            AttributesToSkip = 0,            // 隠し・システムも検索対象
            RecurseSubdirectories = false,   // 再帰は自前キューで並列化する
        };

        private readonly FileSearchOptions _options;
        private readonly FileNameMatcher _matcher;
        private readonly string _baseDir;
        private readonly CancellationToken _token;
        private readonly Action<IReadOnlyList<FileEntry>>? _onBatch;

        private readonly ConcurrentQueue<string> _directories = new();
        private readonly List<FileEntry> _results = new();
        private int _pendingDirectories;

        public SearchContext(FileSearchOptions options, FileNameMatcher matcher,
            string baseDir, CancellationToken token, Action<IReadOnlyList<FileEntry>>? onBatch)
        {
            _options = options;
            _matcher = matcher;
            _baseDir = baseDir;
            _token = token;
            _onBatch = onBatch;
        }

        public void EnqueueDirectory(string path)
        {
            Interlocked.Increment(ref _pendingDirectories);
            _directories.Enqueue(path);
        }

        /// <summary>キューが空になる(=全ディレクトリ処理完了)かキャンセルされるまでディレクトリを処理し続ける。</summary>
        public void DrainQueue()
        {
            var spin = new SpinWait();
            while (!_token.IsCancellationRequested)
            {
                if (_directories.TryDequeue(out var dir))
                {
                    try
                    {
                        ScanDirectory(dir);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // 走査中に消えたディレクトリはスキップ(検索対象の同時変更は正常系)
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _pendingDirectories);
                    }
                    spin.Reset();
                }
                else if (Volatile.Read(ref _pendingDirectories) == 0)
                {
                    return;   // キューが空かつ処理中のディレクトリなし=完了
                }
                else
                {
                    spin.SpinOnce();
                }
            }
        }

        private void ScanDirectory(string dir)
        {
            var includeFiles = _options.IncludeFiles;
            var includeDirs = _options.IncludeDirectories;
            var searchArchives = _options.SearchArchives;

            var entries = new FileSystemEnumerable<ScanItem>(dir, Transform, EnumOptions)
            {
                // ディレクトリは(一致に関わらず)再帰のため常に通す。ファイルは一致した場合と、
                // 書庫内検索が有効な .zip の場合のみ通す=不一致ファイルの文字列生成を省く。
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                    entry.IsDirectory
                    || (includeFiles && _matcher(entry.FileName))
                    || (searchArchives && IsZipName(entry.FileName)),
            };

            List<FileEntry>? found = null;
            foreach (var item in entries)
            {
                if (_token.IsCancellationRequested) return;

                if (item.IsDirectory)
                {
                    if (includeDirs && _matcher(item.Name))
                        (found ??= new()).Add(new FileEntry(Relative(item.FullPath), item.FullPath, true, 0, item.LastModified));
                    // リパースポイント(シンボリックリンク・ジャンクション)は循環防止のため潜らない。
                    if (!item.IsReparsePoint)
                        EnqueueDirectory(item.FullPath);
                }
                else
                {
                    var isZip = IsZipName(item.Name);
                    if (includeFiles && _matcher(item.Name))
                        (found ??= new()).Add(new FileEntry(Relative(item.FullPath), item.FullPath, false, item.Size, item.LastModified)
                        {
                            IsArchive = isZip,
                        });
                    if (searchArchives && isZip)
                    {
                        var local = found ??= new();
                        ScanArchiveEntries(item.FullPath, _matcher,
                            _options.IncludeFiles, _options.IncludeDirectories,
                            RelativeStart(_baseDir), _token, local.Add);
                    }
                }
            }

            if (found is { Count: > 0 })
                Publish(found);
        }

        private void Publish(List<FileEntry> found)
        {
            lock (_results)
                _results.AddRange(found);
            _onBatch?.Invoke(found);
        }

        public IReadOnlyList<FileEntry> TakeSortedResults()
        {
            lock (_results)
            {
                _results.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                return _results;
            }
        }

        private string Relative(string fullPath) => MakeRelative(_baseDir, fullPath);

        private static bool IsZipName(ReadOnlySpan<char> name) =>
            name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        /// <summary>列挙1件分。必要な値だけ取り出す(FileInfo を作らない)。</summary>
        private readonly record struct ScanItem(
            string Name, string FullPath, bool IsDirectory, long Size, DateTime LastModified, bool IsReparsePoint);

        private static ScanItem Transform(ref FileSystemEntry entry) => new(
            entry.FileName.ToString(),
            entry.ToFullPath(),
            entry.IsDirectory,
            entry.IsDirectory ? 0 : entry.Length,
            entry.LastWriteTimeUtc.LocalDateTime,
            (entry.Attributes & FileAttributes.ReparsePoint) != 0);
    }
}
