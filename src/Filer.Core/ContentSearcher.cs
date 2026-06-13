using System.Text;
using System.Text.RegularExpressions;

namespace Filer.Core;

/// <summary>ファイル内容検索(grep)の条件。</summary>
public sealed record ContentSearchOptions(string Query, string BaseDirectory)
{
    /// <summary>検索対象を絞るファイル名パターン(空=全ファイル)。<see cref="FileSearcher"/> の照合規則を流用。</summary>
    public string NamePattern { get; init; } = "";

    /// <summary><see cref="NamePattern"/> を正規表現として解釈するか。</summary>
    public bool NameUseRegex { get; init; }

    /// <summary><see cref="Query"/> を正規表現として解釈するか(false なら部分一致)。</summary>
    public bool UseRegex { get; init; }

    /// <summary>大文字小文字を区別するか(false=無視。既定は無視)。</summary>
    public bool CaseSensitive { get; init; }

    /// <summary>このサイズ(バイト)を超えるファイルは内容検索の対象外にする。</summary>
    public long MaxFileSize { get; init; } = 32L * 1024 * 1024;

    /// <summary>1ファイルあたり収集するマッチ行数の上限。</summary>
    public int MaxMatchesPerFile { get; init; } = 100;

    /// <summary>表示用に1行を切り詰める最大文字数。</summary>
    public int MaxLineLength { get; init; } = 1000;
}

/// <summary>内容検索でマッチした1行(<see cref="LineNumber"/> は1始まり、<see cref="Text"/> は表示用に切り詰め済み)。</summary>
public sealed record ContentMatchLine(int LineNumber, string Text);

/// <summary>内容が一致したファイル1件と、そのマッチ行一覧。</summary>
public sealed record ContentMatch(FileEntry Entry, IReadOnlyList<ContentMatchLine> Lines);

/// <summary>内容検索の結果と、候補列挙に使ったエンジン・補足。</summary>
public sealed record ContentSearchResult(
    IReadOnlyList<ContentMatch> Matches, FileSearchEngine Engine, string? EngineNote);

/// <summary>1行が検索条件に一致するかの判定。</summary>
public delegate bool LineMatcher(string line);

/// <summary>
/// 基準ディレクトリ配下のファイルを再帰的に内容検索(grep)する。
/// 候補ファイルの列挙は <see cref="FileSearcher"/>(ファイル名フィルタ付き)に委譲し、
/// 各候補を並列に走査して一致行を集める。バイナリ(NUL を含む)・サイズ超過・開けないファイルは
/// 対象外として読み飛ばす。文字コードは BOM 自動判定、BOM 無しは UTF-8 として読む。
/// </summary>
public static class ContentSearcher
{
    /// <summary>
    /// 検索文字列から行一致判定を作る。空はエラー(内容検索では全行一致に意味がないため)。
    /// useRegex なら正規表現(不正なら false と error)、それ以外は部分一致。
    /// </summary>
    public static bool TryCreateLineMatcher(string query, bool useRegex, bool caseSensitive,
        out LineMatcher matcher, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(query))
        {
            matcher = static _ => false;
            error = "検索文字列を入力してください";
            return false;
        }

        if (useRegex)
        {
            var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
            if (!caseSensitive) options |= RegexOptions.IgnoreCase;
            Regex regex;
            try
            {
                regex = new Regex(query, options);
            }
            catch (ArgumentException ex)
            {
                matcher = static _ => false;
                error = ex.Message;
                return false;
            }
            matcher = line => regex.IsMatch(line);
            return true;
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        matcher = line => line.Contains(query, comparison);
        return true;
    }

    /// <summary>
    /// 内容検索を実行し、一致したファイルを相対パス順で返す。
    /// <paramref name="onMatch"/> はファイルが一致するたびに呼ばれる(**ワーカースレッドから並行に**呼ばれうる)。
    /// キャンセル時は例外を投げず、それまでの結果を返す。
    /// </summary>
    public static IReadOnlyList<ContentMatch> Search(ContentSearchOptions options,
        CancellationToken token = default, Action<ContentMatch>? onMatch = null)
        => SearchWithInfo(options, token, onMatch).Matches;

    /// <summary>
    /// <see cref="Search"/> と同じ検索を行い、候補列挙に使ったエンジン(通常走査 or MFT 索引)と補足も返す。
    /// </summary>
    public static ContentSearchResult SearchWithInfo(ContentSearchOptions options,
        CancellationToken token = default, Action<ContentMatch>? onMatch = null)
    {
        if (!TryCreateLineMatcher(options.Query, options.UseRegex, options.CaseSensitive,
                out var lineMatcher, out var error))
            throw new ArgumentException(error, nameof(options));

        // 1) 候補ファイルを列挙する。内容読込が支配的なため列挙は通常走査(PreferMft=false)で十分。
        var fileOptions = new FileSearchOptions(options.NamePattern, options.BaseDirectory)
        {
            UseRegex = options.NameUseRegex,
            IncludeFiles = true,
            IncludeDirectories = false,
            SearchArchives = false,
            PreferMft = false,
        };
        var enumeration = FileSearcher.SearchWithInfo(fileOptions, token);

        // 2) 各候補を並列に grep する。
        var matches = new List<ContentMatch>();
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8),
        };
        try
        {
            Parallel.ForEach(enumeration.Entries, parallelOptions, entry =>
            {
                var lines = GrepFile(entry.FullPath, entry.Size, lineMatcher, options, token);
                if (lines is not { Count: > 0 }) return;

                var match = new ContentMatch(entry, lines);
                lock (matches) matches.Add(match);
                onMatch?.Invoke(match);
            });
        }
        catch (OperationCanceledException)
        {
            // キャンセルは例外を投げず、それまでの結果を返す(FileSearcher と同じ流儀)。
        }

        matches.Sort(static (a, b) =>
            string.Compare(a.Entry.Name, b.Entry.Name, StringComparison.OrdinalIgnoreCase));
        return new ContentSearchResult(matches, enumeration.Engine, enumeration.EngineNote);
    }

    /// <summary>
    /// 1ファイルを grep して一致行を返す(なければ null)。サイズ超過・バイナリ・開けないファイルは null。
    /// </summary>
    private static List<ContentMatchLine>? GrepFile(string path, long size, LineMatcher match,
        ContentSearchOptions options, CancellationToken token)
    {
        if (size > options.MaxFileSize) return null;

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;   // 開けない(ロック・権限なし等)ファイルは対象外。
        }

        using (stream)
        {
            // 先頭サンプルでバイナリ判定とエンコーディング判定を行う(1回の読みで両方)。
            var sample = ReadSample(stream, DetectionSampleSize);
            if (IsBinary(sample)) return null;
            var encoding = DetectEncoding(sample);
            stream.Position = 0;

            using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
            List<ContentMatchLine>? hits = null;
            var lineNumber = 0;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (token.IsCancellationRequested) break;
                lineNumber++;
                if (!match(line)) continue;

                (hits ??= new()).Add(new ContentMatchLine(lineNumber, Truncate(line, options.MaxLineLength)));
                if (hits.Count >= options.MaxMatchesPerFile) break;
            }
            return hits;
        }
    }

    /// <summary>バイナリ判定・エンコーディング判定に使う先頭サンプルのバイト数。</summary>
    private const int DetectionSampleSize = 64 * 1024;

    /// <summary>BOM 無しの非 UTF-8 ファイルの既定エンコーディング(日本語 Windows の Shift-JIS)。</summary>
    private static readonly Encoding ShiftJis = CreateFallbackEncoding();

    private static Encoding CreateFallbackEncoding()
    {
        // .NET(Core 以降)は既定で Shift-JIS 等のコードページを持たないため明示登録する。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);   // Shift-JIS(Windows-31J)
    }

    /// <summary>ストリーム先頭から最大 <paramref name="maxBytes"/> バイトを読む(バイナリ・エンコーディング判定用)。</summary>
    private static byte[] ReadSample(Stream stream, int maxBytes)
    {
        var length = (int)Math.Min(maxBytes, stream.Length);
        var buffer = new byte[length];
        var total = 0;
        while (total < length)
        {
            var read = stream.Read(buffer, total, length - total);
            if (read == 0) break;
            total += read;
        }
        return total == buffer.Length ? buffer : buffer[..total];
    }

    /// <summary>
    /// 先頭サンプルからバイナリ(NUL を含む)かどうかを判定する。
    /// テキストの BOM(UTF-8/UTF-16/UTF-32)がある場合はテキストとして扱う(ASCII でも NUL が出るため)。
    /// </summary>
    private static bool IsBinary(ReadOnlySpan<byte> sample)
    {
        if (HasTextBom(sample)) return false;
        return sample.IndexOf((byte)0) >= 0;
    }

    private static bool HasTextBom(ReadOnlySpan<byte> b) =>
        (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) ||   // UTF-8
        (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) ||                   // UTF-16/UTF-32 LE
        (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF);                     // UTF-16 BE

    /// <summary>
    /// 先頭サンプルからエンコーディングを判定する。BOM があればそれに従い、無ければ
    /// UTF-8 として妥当なら UTF-8、そうでなければ Shift-JIS とみなす(日本語 Windows の慣例)。
    /// </summary>
    private static Encoding DetectEncoding(ReadOnlySpan<byte> sample)
    {
        if (sample.Length >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF)
            return Encoding.UTF8;
        if (sample.Length >= 4 && sample[0] == 0xFF && sample[1] == 0xFE && sample[2] == 0x00 && sample[3] == 0x00)
            return Encoding.UTF32;
        if (sample.Length >= 2 && sample[0] == 0xFF && sample[1] == 0xFE)
            return Encoding.Unicode;            // UTF-16 LE
        if (sample.Length >= 2 && sample[0] == 0xFE && sample[1] == 0xFF)
            return Encoding.BigEndianUnicode;   // UTF-16 BE
        return IsValidUtf8(sample) ? Encoding.UTF8 : ShiftJis;
    }

    /// <summary>
    /// バイト列が UTF-8 として妥当かを判定する。サンプル末尾でマルチバイト列が途中で切れている場合は、
    /// 切り詰めによる誤判定を避けるため妥当とみなす。純 ASCII は UTF-8 として妥当(true)。
    /// </summary>
    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        var i = 0;
        while (i < bytes.Length)
        {
            var b = bytes[i];
            int continuation;
            if (b <= 0x7F) { i++; continue; }            // ASCII
            else if (b is >= 0xC2 and <= 0xDF) continuation = 1;
            else if (b is >= 0xE0 and <= 0xEF) continuation = 2;
            else if (b is >= 0xF0 and <= 0xF4) continuation = 3;
            else return false;                            // 不正な先頭バイト(0x80..0xC1, 0xF5..0xFF)

            if (i + continuation >= bytes.Length) return true;   // サンプル末尾で途中切れ → 妥当扱い

            for (var k = 1; k <= continuation; k++)
                if ((bytes[i + k] & 0xC0) != 0x80) return false;  // 継続バイトは 10xxxxxx

            i += continuation + 1;
        }
        return true;
    }

    private static string Truncate(string line, int maxLength) =>
        line.Length <= maxLength ? line : line[..maxLength] + "…";
}
