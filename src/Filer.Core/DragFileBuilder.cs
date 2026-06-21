namespace Filer.Core;

/// <summary>
/// アプリ外へのファイルドラッグ用に、ドラッグ対象の実パス一覧を組み立てる。
/// 書庫内項目は一時フォルダーへ抽出してそのパスを返す(実ファイルはパスをそのまま使う)。
/// </summary>
public static class DragFileBuilder
{
    /// <summary>ドラッグ対象(<paramref name="entries"/>)の実パス一覧。書庫内項目は一時フォルダーへ抽出する。</summary>
    public static string[] Build(IEnumerable<FileEntry> entries)
    {
        var files = new List<string>();
        string? tempDir = null;
        foreach (var entry in entries)
        {
            if (ArchivePath.TrySplit(entry.FullPath, out _, out _))
            {
                tempDir ??= CreateTempDir();
                ArchiveExtractor.ExtractTo(entry.FullPath, tempDir);
                files.Add(Path.Combine(tempDir, entry.Name));
            }
            else
            {
                files.Add(entry.FullPath);
            }
        }
        return files.ToArray();
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Filer", "drag_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
