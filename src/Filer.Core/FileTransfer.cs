namespace Filer.Core;

/// <summary>コピー/移動の種別。</summary>
public enum FileTransferKind { Copy, Move }

/// <summary>転送先に同名ファイルがあったときの処理。</summary>
public enum ConflictAction
{
    /// <summary>既存ファイルを上書きする。</summary>
    Overwrite,
    /// <summary>転送元が新しいときだけ上書きする(古い・同じなら何もしない)。</summary>
    NewerOnly,
    /// <summary>別名で転送する(<see cref="ConflictDecision.NewName"/> を使用)。</summary>
    Rename,
    /// <summary>この項目は転送しない。</summary>
    Skip,
}

/// <summary>1件の同名衝突に対する処理の決定。Rename のときは NewName に変更後のファイル名を入れる。</summary>
public sealed record ConflictDecision(ConflictAction Action, string? NewName = null);

/// <summary>転送の進捗。全体バイト/件数に対する完了量と、処理中の項目名。</summary>
public sealed record FileTransferProgress(
    long TotalBytes,
    long DoneBytes,
    int TotalFiles,
    int DoneFiles,
    string CurrentName);

internal enum TransferItemKind { CopyFile, FastMove, ExtractArchive }

internal sealed record TransferItem(
    TransferItemKind Kind, string Src, string Dest, long Size,
    bool Overwrite = false, bool DeleteSourceAfter = false);

/// <summary>
/// 転送先に同名ファイルが既にある衝突。表示用にコピー元/コピー先の更新日時・サイズを保持する。
/// 解決方法は <see cref="FileTransferService.ResolveConflicts"/> に渡すリゾルバが決める。
/// </summary>
public sealed class TransferConflict
{
    /// <summary>転送元ファイルのフルパス。</summary>
    public string SourcePath { get; }
    /// <summary>転送先に既に存在する同名ファイルのフルパス。</summary>
    public string ExistingPath { get; }
    /// <summary>転送元の最終更新日時(UTC)。</summary>
    public DateTime SourceModifiedUtc { get; }
    /// <summary>既存ファイルの最終更新日時(UTC)。</summary>
    public DateTime ExistingModifiedUtc { get; }
    /// <summary>転送元のバイト数。</summary>
    public long SourceSize { get; }
    /// <summary>既存ファイルのバイト数。</summary>
    public long ExistingSize { get; }

    /// <summary>移動時、転送成功後に転送元を削除するか。</summary>
    internal bool DeleteSourceAfter { get; }

    internal TransferConflict(string source, string existing, bool deleteSourceAfter)
    {
        SourcePath = source;
        ExistingPath = existing;
        SourceModifiedUtc = File.GetLastWriteTimeUtc(source);
        ExistingModifiedUtc = File.GetLastWriteTimeUtc(existing);
        SourceSize = new FileInfo(source).Length;
        ExistingSize = new FileInfo(existing).Length;
        DeleteSourceAfter = deleteSourceAfter;
    }
}

/// <summary>
/// 転送計画。実行前に列挙したファイル一覧・作成すべきディレクトリ・総量を保持する。
/// 計画段階で検出した同名衝突は <see cref="Conflicts"/> に積み、リゾルバで解決してから実行する。
/// </summary>
public sealed class FileTransferPlan
{
    internal List<string> DirsToCreate { get; } = new();
    internal List<TransferItem> Items { get; } = new();
    internal List<string> SourceTreesToPrune { get; } = new();
    private readonly List<TransferConflict> _conflicts = new();

    /// <summary>計画作成時に検出した同名衝突。<see cref="FileTransferService.ResolveConflicts"/> で解決する。</summary>
    public IReadOnlyList<TransferConflict> Conflicts => _conflicts;

    /// <summary>未解決の同名衝突があるか。</summary>
    public bool HasConflicts => _conflicts.Count > 0;

    /// <summary>コピー対象の総バイト数(高速移動・書庫抽出は0として加算)。</summary>
    public long TotalBytes { get; internal set; }

    /// <summary>処理対象の総件数。</summary>
    public int TotalFiles { get; internal set; }

    /// <summary>実行対象が無ければ真(衝突の有無は含まない。先に <see cref="HasConflicts"/> を解決すること)。</summary>
    public bool IsEmpty => Items.Count == 0;

    internal void AddConflict(TransferConflict c) => _conflicts.Add(c);
    internal void ClearConflicts() => _conflicts.Clear();
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
    /// 同一ディレクトリへのコピー・自身/配下への転送・種別不一致は例外で拒否する(フォールバックしない)。
    /// 転送先の同名ファイルは例外にせず <see cref="FileTransferPlan.Conflicts"/> に積む。
    /// </summary>
    public static FileTransferPlan BuildPlan(IReadOnlyList<string> sources, string destDir, FileTransferKind kind)
    {
        var plan = new FileTransferPlan();

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
            var deleteSource = kind == FileTransferKind.Move;

            if (Directory.Exists(src))
            {
                EnsureNotSameOrUnder(src, target);
                if (File.Exists(target))
                    throw new IOException($"転送先に同名のファイルがあるためフォルダーを転送できません: {name}");

                if (deleteSource && !Directory.Exists(target) && IsSameVolume(src, destDir))
                {
                    plan.Items.Add(new TransferItem(TransferItemKind.FastMove, src, target, 0));
                    plan.TotalFiles++;
                }
                else
                {
                    // 既存フォルダーへのマージ、またはボリュームを跨ぐ移動。中身を1ファイルずつ転送する。
                    EnumerateDirectory(src, target, plan, deleteSource);
                    if (deleteSource) plan.SourceTreesToPrune.Add(src);
                }
            }
            else if (File.Exists(src))
            {
                if (kind == FileTransferKind.Copy && PathEquals(Path.GetDirectoryName(src)!, destDir))
                    throw new IOException($"同一ディレクトリへはコピーできません: {src}");
                if (Directory.Exists(target))
                    throw new IOException($"転送先に同名のフォルダーがあるためファイルを転送できません: {name}");

                var size = new FileInfo(src).Length;
                if (File.Exists(target))
                {
                    plan.AddConflict(new TransferConflict(src, target, deleteSource));
                }
                else if (deleteSource && IsSameVolume(src, destDir))
                {
                    plan.Items.Add(new TransferItem(TransferItemKind.FastMove, src, target, size));
                    plan.TotalFiles++;
                    plan.TotalBytes += size;
                }
                else
                {
                    plan.Items.Add(new TransferItem(TransferItemKind.CopyFile, src, target, size, false, deleteSource));
                    plan.TotalFiles++;
                    plan.TotalBytes += size;
                }
            }
            else
            {
                throw new FileNotFoundException($"コピー/移動元が見つかりません: {src}");
            }
        }

        return plan;
    }

    /// <summary>
    /// 計画中の同名衝突を、各衝突につき resolver の決定で解決し実行対象へ反映する。
    /// resolver が <see cref="OperationCanceledException"/> を投げると解決を中断する(計画は破棄して使わない)。
    /// </summary>
    public static void ResolveConflicts(FileTransferPlan plan, Func<TransferConflict, ConflictDecision> resolver)
    {
        foreach (var c in plan.Conflicts)
        {
            var decision = resolver(c);
            switch (decision.Action)
            {
                case ConflictAction.Skip:
                    break;

                case ConflictAction.NewerOnly:
                    if (c.SourceModifiedUtc > c.ExistingModifiedUtc)
                        AddResolvedCopy(plan, c.SourcePath, c.ExistingPath, c.SourceSize, overwrite: true, c.DeleteSourceAfter);
                    break;

                case ConflictAction.Overwrite:
                    AddResolvedCopy(plan, c.SourcePath, c.ExistingPath, c.SourceSize, overwrite: true, c.DeleteSourceAfter);
                    break;

                case ConflictAction.Rename:
                    var newName = decision.NewName
                        ?? throw new ArgumentException("Rename の決定には NewName が必要です。");
                    var dest = Path.Combine(Path.GetDirectoryName(c.ExistingPath)!, newName);
                    AddResolvedCopy(plan, c.SourcePath, dest, c.SourceSize, overwrite: false, c.DeleteSourceAfter);
                    break;
            }
        }
        plan.ClearConflicts();
    }

    /// <summary>dir 直下で name と衝突しない一意な名前を返す(「name (2).ext」形式で採番)。</summary>
    public static string MakeUniqueName(string dir, string name)
    {
        if (!Exists(Path.Combine(dir, name))) return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!Exists(Path.Combine(dir, candidate))) return candidate;
        }
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

        void CopyBytesTo(TransferItem item, string destPath, string name)
        {
            var buffer = new byte[BufferSize];
            using var input = new FileStream(item.Src, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
            var output = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize);
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
                TryDelete(destPath);   // 中断・失敗で半端なファイルを残さない
                throw;
            }
            // だいなファイラー等と同様、コピー後のファイル日時はコピー元を引き継ぐ(現在日時にしない)。
            File.SetCreationTimeUtc(destPath, File.GetCreationTimeUtc(item.Src));
            File.SetLastWriteTimeUtc(destPath, File.GetLastWriteTimeUtc(item.Src));
        }

        void CopyFile(TransferItem item, string name)
        {
            if (item.Overwrite && File.Exists(item.Dest))
            {
                // 既存を残したまま一時ファイルへ書き、完了後に置換する(失敗で既存を失わない)。
                var tmp = item.Dest + ".filer-tmp";
                TryDelete(tmp);
                CopyBytesTo(item, tmp, name);
                File.Move(tmp, item.Dest, overwrite: true);
            }
            else
            {
                CopyBytesTo(item, item.Dest, name);
            }
            if (item.DeleteSourceAfter) File.Delete(item.Src);
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

        // 移動で中身を1ファイルずつ移したフォルダーは、空になった元フォルダーだけを後始末する。
        // (スキップした項目が残っていれば空にならず、元フォルダーは残る)
        foreach (var tree in plan.SourceTreesToPrune)
            PruneEmptyDirs(tree);

        progress?.Report(new FileTransferProgress(plan.TotalBytes, done, plan.TotalFiles, plan.TotalFiles, ""));
    }

    private static void AddResolvedCopy(
        FileTransferPlan plan, string src, string dest, long size, bool overwrite, bool deleteSource)
    {
        plan.Items.Add(new TransferItem(TransferItemKind.CopyFile, src, dest, size, overwrite, deleteSource));
        plan.TotalFiles++;
        plan.TotalBytes += size;
    }

    private static void EnumerateDirectory(
        string srcDir, string targetDir, FileTransferPlan plan, bool deleteSource)
    {
        plan.DirsToCreate.Add(targetDir);
        foreach (var sub in Directory.GetDirectories(srcDir, "*", SearchOption.AllDirectories))
            plan.DirsToCreate.Add(Path.Combine(targetDir, Path.GetRelativePath(srcDir, sub)));

        foreach (var file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcDir, file);
            var dest = Path.Combine(targetDir, rel);
            if (File.Exists(dest))
            {
                plan.AddConflict(new TransferConflict(file, dest, deleteSource));
                continue;
            }

            var size = new FileInfo(file).Length;
            plan.Items.Add(new TransferItem(TransferItemKind.CopyFile, file, dest, size, false, deleteSource));
            plan.TotalFiles++;
            plan.TotalBytes += size;
        }
    }

    private static void PruneEmptyDirs(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        if (!Directory.EnumerateFileSystemEntries(root).Any())
            Directory.Delete(root);
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
