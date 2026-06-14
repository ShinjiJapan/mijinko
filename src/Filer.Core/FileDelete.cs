namespace Filer.Core;

/// <summary>削除の種別。</summary>
public enum DeleteKind { Recycle, Permanent }

/// <summary>
/// 削除計画。完全削除は配下の全ファイル/ディレクトリを、ごみ箱送りはトップレベル項目を保持する。
/// 進捗は件数(<see cref="FileTransferProgress"/> の TotalFiles/DoneFiles)で表す。
/// </summary>
public sealed class FileDeletePlan
{
    internal List<string> RecycleTargets { get; } = new();
    internal List<string> Files { get; } = new();
    internal List<string> Dirs { get; } = new();

    /// <summary>進捗の分母となる総件数。</summary>
    public int TotalItems { get; internal set; }

    /// <summary>対象が無ければ真。</summary>
    public bool IsEmpty => TotalItems == 0;
}

/// <summary>
/// 非同期削除エンジン(UI非依存)。完全削除は配下を再帰列挙して1件ずつ削除し件数で進捗通知、
/// ごみ箱送りはトップレベル項目ごとにシェルへ送る。<see cref="CancellationToken"/> でキャンセル可能。
/// **リパースポイント(ジャンクション/シンボリックリンク)の内部へは潜らず、リンク自体を削除する**
/// (リンク越しに実体を消す事故を防ぐ)。
/// </summary>
public static class FileDeleteService
{
    private const int ReportEvery = 64;   // 件数throttle(通知洪水の抑制)

    /// <summary>sources を削除する計画を作る。見つからない元は例外で拒否する。</summary>
    public static FileDeletePlan BuildPlan(IReadOnlyList<string> sources, DeleteKind kind)
    {
        var plan = new FileDeletePlan();

        foreach (var src in sources)
        {
            var exists = File.Exists(src) || Directory.Exists(src);
            if (!exists)
                throw new FileNotFoundException($"削除対象が見つかりません: {src}");

            if (kind == DeleteKind.Recycle)
            {
                plan.RecycleTargets.Add(src);
                continue;
            }

            if (Directory.Exists(src))
            {
                if (IsReparsePoint(src))
                    plan.Dirs.Add(src);          // リンク自体を削除(中身は追わない)
                else
                    Enumerate(src, plan);
            }
            else
            {
                plan.Files.Add(src);
            }
        }

        plan.TotalItems = kind == DeleteKind.Recycle
            ? plan.RecycleTargets.Count
            : plan.Files.Count + plan.Dirs.Count;
        return plan;
    }

    /// <summary>計画を実行する。件数で進捗通知し、項目間でキャンセルを確認する。</summary>
    public static void Execute(
        FileDeletePlan plan, DeleteKind kind,
        IProgress<FileTransferProgress>? progress, CancellationToken token)
    {
        var total = plan.TotalItems;
        var done = 0;

        void Report(string name) => progress?.Report(new FileTransferProgress(0, 0, total, done, name));

        Report("");

        if (kind == DeleteKind.Recycle)
        {
            foreach (var target in plan.RecycleTargets)
            {
                token.ThrowIfCancellationRequested();
                RecycleBin.Send(target);
                done++;
                if (done % ReportEvery == 0) Report(NameOf(target));
            }
        }
        else
        {
            foreach (var file in plan.Files)
            {
                token.ThrowIfCancellationRequested();
                File.Delete(file);
                done++;
                if (done % ReportEvery == 0) Report(NameOf(file));
            }
            // 深い順に空ディレクトリを除去する(子→親)。
            foreach (var dir in plan.Dirs.OrderByDescending(d => d.Length))
            {
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: false);
                done++;
                if (done % ReportEvery == 0) Report(NameOf(dir));
            }
        }

        progress?.Report(new FileTransferProgress(0, 0, total, total, ""));
    }

    /// <summary>ディレクトリ配下を再帰列挙してファイル/ディレクトリを計画へ積む(リパースは潜らない)。</summary>
    private static void Enumerate(string dir, FileDeletePlan plan)
    {
        foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo sub)
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                    plan.Dirs.Add(sub.FullName);   // リンク自体
                else
                    Enumerate(sub.FullName, plan);
            }
            else
            {
                plan.Files.Add(entry.FullName);
            }
        }
        plan.Dirs.Add(dir);
    }

    private static bool IsReparsePoint(string dir) =>
        (new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0;

    private static string NameOf(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}
