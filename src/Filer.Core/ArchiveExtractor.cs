using System.IO.Compression;

namespace Filer.Core;

/// <summary>
/// 仮想パス(<c>...\archive.zip\inner\file</c>)が指す ZIP 内エントリを、
/// バイト列読み取り・実フォルダーへの抽出として取り出す。
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>書庫内エントリ(ファイル)の内容をバイト列で読み取る。</summary>
    public static byte[] ReadEntryBytes(string virtualPath)
    {
        if (!ArchivePath.TrySplit(virtualPath, out var archivePath, out var entryPath))
            throw new InvalidOperationException($"書庫内パスではありません: {virtualPath}");

        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry(entryPath.Replace('\\', '/'))
            ?? throw new FileNotFoundException($"書庫内に項目が見つかりません: {entryPath}");

        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>
    /// 書庫内のファイル/ディレクトリ(virtualPath)を実フォルダー destDir 直下へ抽出する。
    /// ディレクトリは配下を再帰的に展開する。同名が既存なら拒否する(上書きしない)。
    /// </summary>
    public static void ExtractTo(string virtualPath, string destDir)
    {
        if (!ArchivePath.TrySplit(virtualPath, out var archivePath, out var entryPath))
            throw new InvalidOperationException($"書庫内パスではありません: {virtualPath}");

        var entryRel = entryPath.Replace('\\', '/');
        using var archive = ZipFile.OpenRead(archivePath);

        var fileEntry = archive.GetEntry(entryRel);
        if (fileEntry is not null && !string.IsNullOrEmpty(fileEntry.Name))
        {
            Directory.CreateDirectory(destDir);
            var dest = Path.Combine(destDir, fileEntry.Name);
            RejectIfExists(dest, fileEntry.Name);
            fileEntry.ExtractToFile(dest, overwrite: false);
            return;
        }

        // ディレクトリ: 配下のエントリを prefix 一致で再帰展開する。
        var prefix = entryRel.TrimEnd('/') + "/";
        var baseName = LastSegment(entryRel);
        var targetRoot = Path.Combine(destDir, baseName);
        RejectIfExists(targetRoot, baseName);

        var any = false;
        foreach (var e in archive.Entries)
        {
            var name = e.FullName.Replace('\\', '/');
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            any = true;

            var outPath = Path.Combine(targetRoot, name[prefix.Length..].Replace('/', '\\'));
            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(outPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                e.ExtractToFile(outPath, overwrite: false);
            }
        }

        if (!any)
            throw new FileNotFoundException($"書庫内に項目が見つかりません: {entryPath}");
    }

    private static void RejectIfExists(string path, string name)
    {
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException($"同名の項目が既に存在します: {name}");
    }

    private static string LastSegment(string entryRel)
    {
        var trimmed = entryRel.TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        return idx < 0 ? trimmed : trimmed[(idx + 1)..];
    }
}
