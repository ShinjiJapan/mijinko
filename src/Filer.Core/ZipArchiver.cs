using System.IO.Compression;

namespace Filer.Core;

/// <summary>
/// 実ファイル/フォルダーを ZIP 書庫へ圧縮する。各ソースは書庫ルート直下に元の名前で入り、
/// フォルダーは配下を再帰的に格納する。圧縮先が既存なら拒否する(上書きしない)。
/// </summary>
public static class ZipArchiver
{
    /// <summary>sourcePaths(ファイル/フォルダー)を destZipPath へ圧縮する。</summary>
    public static void Create(IReadOnlyList<string> sourcePaths, string destZipPath)
    {
        if (sourcePaths.Count == 0)
            throw new InvalidOperationException("圧縮対象がありません。");
        if (File.Exists(destZipPath) || Directory.Exists(destZipPath))
            throw new IOException($"同名のファイル/フォルダーが既に存在します: {Path.GetFileName(destZipPath)}");

        using var fs = File.Create(destZipPath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        foreach (var src in sourcePaths)
        {
            if (Directory.Exists(src))
                AddDirectory(archive, src, Path.GetFileName(src.TrimEnd('\\', '/')));
            else if (File.Exists(src))
                archive.CreateEntryFromFile(src, Path.GetFileName(src), CompressionLevel.Optimal);
            else
                throw new FileNotFoundException($"圧縮対象が見つかりません: {src}");
        }
    }

    /// <summary>
    /// 圧縮後ファイル名の既定値。元の拡張子は無視し、常に <c>.zip</c> を付ける
    /// (例: <c>report.docx</c> → <c>report.zip</c>、フォルダー <c>myfolder</c> → <c>myfolder.zip</c>)。
    /// </summary>
    public static string DefaultZipName(string firstSourceName) =>
        Path.GetFileNameWithoutExtension(firstSourceName.TrimEnd('\\', '/')) + ".zip";

    private static void AddDirectory(ZipArchive archive, string dir, string entryPrefix)
    {
        var any = false;
        foreach (var file in Directory.GetFiles(dir))
        {
            archive.CreateEntryFromFile(file, entryPrefix + "/" + Path.GetFileName(file), CompressionLevel.Optimal);
            any = true;
        }
        foreach (var sub in Directory.GetDirectories(dir))
        {
            AddDirectory(archive, sub, entryPrefix + "/" + Path.GetFileName(sub));
            any = true;
        }
        // 空フォルダーはディレクトリエントリとして残す。
        if (!any)
            archive.CreateEntry(entryPrefix + "/");
    }
}
