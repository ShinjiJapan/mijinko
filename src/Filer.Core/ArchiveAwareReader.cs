using System.IO.Compression;

namespace Filer.Core;

/// <summary>
/// 仮想パス(<c>...\archive.zip\inner\dir</c>)の書庫境界を解決するヘルパー。
/// </summary>
public static class ArchivePath
{
    /// <summary>拡張子が書庫(.zip)かどうか。</summary>
    public static bool HasArchiveExtension(string nameOrPath) =>
        string.Equals(Path.GetExtension(nameOrPath), ".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// パスが書庫の内部を指すなら、書庫ファイルのパスと書庫内パスへ分解する。
    /// 末尾から親へ辿り、実在する .zip ファイルを境界とする。書庫外なら false。
    /// </summary>
    public static bool TrySplit(string path, out string archivePath, out string entryPath)
    {
        archivePath = string.Empty;
        entryPath = string.Empty;

        var current = path;
        while (!string.IsNullOrEmpty(current))
        {
            if (HasArchiveExtension(current) && File.Exists(current))
            {
                archivePath = current;
                entryPath = path.Length > current.Length
                    ? path[current.Length..].Trim('\\', '/')
                    : string.Empty;
                return true;
            }
            current = Path.GetDirectoryName(current);
        }
        return false;
    }

    /// <summary>
    /// パスが書庫の内部(書庫内のフォルダー・ファイル)を指すなら true。
    /// 書庫ファイル自体は実ファイルであり「内部」ではないため false。
    /// </summary>
    public static bool IsInsideArchive(string path) =>
        TrySplit(path, out _, out var entryPath) && entryPath.Length > 0;
}

/// <summary>
/// 実ファイルシステムと ZIP 書庫の双方を読み取る <see cref="IDirectoryReader"/>。
/// 書庫内パスは ZIP を、それ以外は内部の FS リーダーへ委譲する。
/// </summary>
public sealed class ArchiveAwareReader : IDirectoryReader
{
    private readonly IDirectoryReader _fileSystem;

    public ArchiveAwareReader(IDirectoryReader fileSystem) => _fileSystem = fileSystem;

    public IReadOnlyList<FileEntry> Read(string path)
    {
        if (ArchivePath.TrySplit(path, out var archivePath, out var entryPath))
            return ReadArchive(archivePath, entryPath);
        return _fileSystem.Read(path);
    }

    /// <summary>ZIP 書庫内の指定ディレクトリ(entryPath、ルートは空)の直下を列挙する。</summary>
    private static IReadOnlyList<FileEntry> ReadArchive(string archivePath, string entryPath)
    {
        var prefix = entryPath.Length == 0
            ? string.Empty
            : entryPath.Replace('\\', '/').TrimEnd('/') + "/";

        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(string Name, long Size, DateTime LastModified)>();

        using (var archive = ZipFile.OpenRead(archivePath))
        {
            foreach (var entry in archive.Entries)
            {
                var fullName = entry.FullName.Replace('\\', '/');
                if (!fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var remainder = fullName[prefix.Length..];
                if (remainder.Length == 0)
                    continue;   // ディレクトリエントリ自身

                var slash = remainder.IndexOf('/');
                if (slash >= 0)
                {
                    dirs.Add(remainder[..slash]);   // 直下のサブディレクトリ
                }
                else if (!string.IsNullOrEmpty(entry.Name))
                {
                    files.Add((entry.Name, entry.Length, entry.LastWriteTime.LocalDateTime));
                }
            }
        }

        var list = new List<FileEntry> { FileEntry.Parent(ParentPath(archivePath, entryPath)) };
        foreach (var dir in dirs)
            list.Add(new FileEntry(dir, ChildPath(archivePath, entryPath, dir), true, 0, default));
        foreach (var (name, size, modified) in files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            list.Add(new FileEntry(name, ChildPath(archivePath, entryPath, name), false, size, modified));

        return list;
    }

    /// <summary>".." の移動先。書庫ルートなら書庫を含む FS フォルダー、内部なら 1 つ上の書庫内ディレクトリ。</summary>
    private static string ParentPath(string archivePath, string entryPath)
    {
        if (entryPath.Length == 0)
            return Path.GetDirectoryName(archivePath) ?? archivePath;

        var idx = entryPath.LastIndexOfAny(new[] { '\\', '/' });
        return idx < 0 ? archivePath : $"{archivePath}\\{entryPath[..idx]}";
    }

    private static string ChildPath(string archivePath, string entryPath, string name) =>
        entryPath.Length == 0 ? $"{archivePath}\\{name}" : $"{archivePath}\\{entryPath}\\{name}";
}
