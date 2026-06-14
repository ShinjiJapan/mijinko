namespace Filer.Core;

/// <summary>コピー/移動の種別。</summary>
public enum FileTransferKind { Copy, Move }

/// <summary>転送の進捗。全体バイト/件数に対する完了量と、処理中の項目名。</summary>
public sealed record FileTransferProgress(
    long TotalBytes,
    long DoneBytes,
    int TotalFiles,
    int DoneFiles,
    string CurrentName);

internal enum TransferItemKind { CopyFile, FastMove, ExtractArchive }

internal sealed record TransferItem(TransferItemKind Kind, string Src, string Dest, long Size);

/// <summary>
/// 転送計画。実行前に列挙したファイル一覧・作成すべきディレクトリ・総量を保持する。
/// 計画段階で同名衝突を検出して全件拒否するため、実行は半端な状態を残さない。
/// </summary>
public sealed class FileTransferPlan
{
    internal List<string> DirsToCreate { get; } = new();
    internal List<TransferItem> Items { get; } = new();
    internal List<string> SourcesToDeleteAfter { get; } = new();

    /// <summary>コピー対象の総バイト数(高速移動・書庫抽出は0として加算)。</summary>
    public long TotalBytes { get; internal set; }

    /// <summary>処理対象の総件数。</summary>
    public int TotalFiles { get; internal set; }

    /// <summary>対象が無ければ真。</summary>
    public bool IsEmpty => Items.Count == 0;
}

/// <summary>
/// 実ファイルシステムへの非同期コピー/移動エンジン(UI非依存)。
/// バッファ単位のバイトコピーで進捗を <see cref="IProgress{T}"/> 通知し、
/// <see cref="CancellationToken"/> でキャンセル可能。書庫(.zip)内項目は実フォルダーへ抽出する。
/// </summary>
public static class FileTransferService
{
    private const int BufferSize = 1024 * 1024;        // 1MB
    private const long ReportInterval = 4L * 1024 * 1024;   // 4MBごとに進捗通知(通知洪水の抑制)

    /// <summary>
    /// sources(ファイル/ディレクトリ/書庫内項目)を destDir 配下へ転送する計画を作る。
    /// 同一ディレクトリへのコピー・自身/配下への転送・同名衝突は例外で拒否する(フォールバックしない)。
    /// </summary>
    public static FileTransferPlan BuildPlan(IReadOnlyList<string> sources, string destDir, FileTransferKind kind)
    {
        var plan = new FileTransferPlan();
        var conflicts = new List<string>();

        foreach (var src in sources)
        {
            if (ArchivePath.IsInsideArchive(src))
            {
                // 書庫内項目は実フォルダーへ抽出(コピーのみ。移動は呼び出し側で拒否済み)。
                plan.Items.Add(new TransferItem(TransferItemKind.ExtractArchive, src, destDir, 0));
                plan.TotalFiles++;
                continue;
            }

            var name = NameOf(src);
            var target = Path.Combine(destDir, name);

            if (Directory.Exists(src))
            {
                EnsureNotSameOrUnder(src, target);

                if (kind == FileTransferKind.Move)
                {
                    if (Exists(target)) { conflicts.Add(name); continue; }
                    if (IsSameVolume(src, destDir))
                    {
                        plan.Items.Add(new TransferItem(TransferItemKind.FastMove, src, target, 0));
                        plan.TotalFiles++;
                    }
                    else
                    {
                        EnumerateDirectory(src, target, plan, conflicts, checkConflicts: false);
                        plan.SourcesToDeleteAfter.Add(src);
                    }
                }
                else
                {
                    EnumerateDirectory(src, target, plan, conflicts, checkConflicts: true);
                }
            }
            else if (File.Exists(src))
            {
                if (kind == FileTransferKind.Copy && PathEquals(Path.GetDirectoryName(src)!, destDir))
                    throw new IOException($"同一ディレクトリへはコピーできません: {src}");

                if (Exists(target)) { conflicts.Add(name); continue; }

                var size = new FileInfo(src).Length;
                if (kind == FileTransferKind.Move && IsSameVolume(src, destDir))
                {
                    plan.Items.Add(new TransferItem(TransferItemKind.FastMove, src, target, size));
                }
                else
                {
                    plan.Items.Add(new TransferItem(TransferItemKind.CopyFile, src, target, size));
                    if (kind == FileTransferKind.Move) plan.SourcesToDeleteAfter.Add(src);
                }
                plan.TotalFiles++;
                plan.TotalBytes += size;
            }
            else
            {
                throw new FileNotFoundException($"コピー/移動元が見つかりません: {src}");
            }
        }

        if (conflicts.Count > 0)
            throw new IOException("転送先に同名の項目が既に存在します:\n" + string.Join("\n", conflicts));

        return plan;
    }

    /// <summary>計画を実行する。進捗通知とキャンセルに対応。途中失敗時は処理中ファイルを残さない。</summary>
    public static void Execute(
        FileTransferPlan plan, FileTransferKind kind,
        IProgress<FileTransferProgress>? progress, CancellationToken token)
    {
        foreach (var dir in plan.DirsToCreate)
            Directory.CreateDirectory(dir);

        long done = 0;
        var doneFiles = 0;
        long lastReported = 0;

        void Report(string name, bool force)
        {
            if (!force && done - lastReported < ReportInterval) return;
            lastReported = done;
            progress?.Report(new FileTransferProgress(plan.TotalBytes, done, plan.TotalFiles, doneFiles, name));
        }

        void CopyFile(TransferItem item, string name)
        {
            var buffer = new byte[BufferSize];
            using var input = new FileStream(item.Src, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
            var output = new FileStream(item.Dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize);
            try
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    done += read;
                    Report(name, force: false);
                }
                output.Dispose();
            }
            catch
            {
                output.Dispose();
                TryDelete(item.Dest);   // 中断・失敗で半端なファイルを残さない
                throw;
            }
            // だいなファイラー等と同様、コピー後のファイル日時はコピー元を引き継ぐ(現在日時にしない)。
            File.SetCreationTimeUtc(item.Dest, File.GetCreationTimeUtc(item.Src));
            File.SetLastWriteTimeUtc(item.Dest, File.GetLastWriteTimeUtc(item.Src));
        }

        Report("", force: true);

        foreach (var item in plan.Items)
        {
            token.ThrowIfCancellationRequested();
            var name = NameOf(item.Src);
            Report(name, force: true);

            switch (item.Kind)
            {
                case TransferItemKind.FastMove:
                    if (Directory.Exists(item.Src)) Directory.Move(item.Src, item.Dest);
                    else File.Move(item.Src, item.Dest);
                    done += item.Size;
                    break;

                case TransferItemKind.ExtractArchive:
                    ArchiveExtractor.ExtractTo(item.Src, item.Dest);
                    break;

                case TransferItemKind.CopyFile:
                    CopyFile(item, name);
                    break;
            }

            doneFiles++;
            Report(name, force: true);
        }

        if (kind == FileTransferKind.Move)
            foreach (var src in plan.SourcesToDeleteAfter)
            {
                if (Directory.Exists(src)) Directory.Delete(src, recursive: true);
                else if (File.Exists(src)) File.Delete(src);
            }

        progress?.Report(new FileTransferProgress(plan.TotalBytes, done, plan.TotalFiles, plan.TotalFiles, ""));
    }

    private static void EnumerateDirectory(
        string srcDir, string targetDir, FileTransferPlan plan, List<string> conflicts, bool checkConflicts)
    {
        plan.DirsToCreate.Add(targetDir);
        foreach (var sub in Directory.GetDirectories(srcDir, "*", SearchOption.AllDirectories))
            plan.DirsToCreate.Add(Path.Combine(targetDir, Path.GetRelativePath(srcDir, sub)));

        foreach (var file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcDir, file);
            var dest = Path.Combine(targetDir, rel);
            if (checkConflicts && File.Exists(dest)) { conflicts.Add(rel); continue; }

            var size = new FileInfo(file).Length;
            plan.Items.Add(new TransferItem(TransferItemKind.CopyFile, file, dest, size));
            plan.TotalFiles++;
            plan.TotalBytes += size;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* 後始末の失敗は本来の例外を隠さない */ }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool IsSameVolume(string a, string b) =>
        string.Equals(
            Path.GetPathRoot(Path.GetFullPath(a)),
            Path.GetPathRoot(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    private static void EnsureNotSameOrUnder(string src, string target)
    {
        var s = NormalizeDir(src);
        var t = NormalizeDir(target);
        if (PathEquals(s, t) ||
            t.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"自身または配下へはコピー/移動できません: {src}");
    }

    private static string NameOf(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static string NormalizeDir(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathEquals(string a, string b) =>
        string.Equals(NormalizeDir(a), NormalizeDir(b), StringComparison.OrdinalIgnoreCase);
}

/// <summary>進捗ダイアログ向けの人間可読フォーマット(サイズ・速度・残り時間)。</summary>
public static class TransferFormat
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>バイト数を「1.5 GB」形式へ。1KB未満は「123 B」。</summary>
    public static string Size(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.0} {Units[u]}";
    }

    /// <summary>転送速度を「1.5 MB/s」形式へ。</summary>
    public static string Rate(double bytesPerSecond) => Size((long)bytesPerSecond) + "/s";

    /// <summary>残り時間を「残り 1分23秒」形式へ。</summary>
    public static string Eta(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"残り {(int)t.TotalHours}時間{t.Minutes}分";
        if (t.TotalMinutes >= 1) return $"残り {t.Minutes}分{t.Seconds}秒";
        return $"残り {t.Seconds}秒";
    }
}
