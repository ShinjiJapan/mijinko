namespace Filer.Core;

/// <summary>
/// 実ファイルシステムを読み取る <see cref="IDirectoryReader"/> 実装。
/// 先頭に親("..")、続いてディレクトリ(名前順)、ファイル(名前順)を返す。
/// </summary>
public sealed class DirectoryLister : IDirectoryReader
{
    public IReadOnlyList<FileEntry> Read(string path)
    {
        var di = new DirectoryInfo(path);
        var list = new List<FileEntry>();

        if (di.Parent is not null)
            list.Add(FileEntry.Parent(di.Parent.FullName));

        foreach (var d in di.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            list.Add(new FileEntry(d.Name, d.FullName, true, 0, d.LastWriteTime));

        foreach (var f in di.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            list.Add(new FileEntry(f.Name, f.FullName, false, f.Length, f.LastWriteTime)
            {
                IsArchive = ArchivePath.HasArchiveExtension(f.Name),
            });

        return list;
    }
}
