namespace Filer.Core;

/// <summary>
/// 実ファイルシステムに対するファイル操作。コピー/移動/削除/リネームを提供する。
/// 危険な操作(自己上書き・既存名への変更)は例外で拒否する。
/// </summary>
public sealed class FileOperations
{
    /// <summary>src(ファイル/ディレクトリ)を destDir 配下へコピーする。元は残す。</summary>
    public void Copy(string src, string destDir)
    {
        var name = NameOf(src);
        var target = Path.Combine(destDir, name);

        if (Directory.Exists(src))
        {
            EnsureNotSameOrUnder(src, target);
            CopyDirectory(src, target);
        }
        else
        {
            if (PathEquals(Path.GetDirectoryName(src)!, destDir))
                throw new IOException($"同一ディレクトリへはコピーできません: {src}");
            File.Copy(src, target, overwrite: false);
        }
    }

    /// <summary>src(ファイル/ディレクトリ)を destDir 配下へ移動する。元は消える。</summary>
    public void Move(string src, string destDir)
    {
        var name = NameOf(src);
        var target = Path.Combine(destDir, name);

        if (Directory.Exists(src))
        {
            EnsureNotSameOrUnder(src, target);
            Directory.Move(src, target);
        }
        else
        {
            File.Move(src, target, overwrite: false);
        }
    }

    /// <summary>ファイル/ディレクトリを完全削除する。ディレクトリは再帰削除。ごみ箱には入らない。</summary>
    public void Delete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else
            File.Delete(path);
    }

    /// <summary>ファイル/ディレクトリをごみ箱へ送る(復元可能)。</summary>
    public void DeleteToRecycleBin(string path) => RecycleBin.Send(path);

    /// <summary>同一ディレクトリ内で名前を変更する。既存名への変更は拒否する。</summary>
    public void Rename(string path, string newName)
    {
        var dir = Path.GetDirectoryName(path)
                  ?? throw new IOException($"親ディレクトリを特定できません: {path}");
        var target = Path.Combine(dir, newName);

        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException($"同名のファイル/ディレクトリが既に存在します: {newName}");

        if (Directory.Exists(path))
            Directory.Move(path, target);
        else
            File.Move(path, target);
    }

    /// <summary>parentDir の直下に name のフォルダーを作成する。同名のファイル/フォルダーがあれば拒否する。</summary>
    public void CreateDirectory(string parentDir, string name)
    {
        var target = Path.Combine(parentDir, name);
        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException($"同名のファイル/ディレクトリが既に存在します: {name}");
        Directory.CreateDirectory(target);
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
    }

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
