using System.IO;

namespace Filer.Core;

/// <summary>
/// 実ファイルシステムに対する <see cref="IFolderCompareSource"/> 実装。
/// リパースポイント(ジャンクション/シンボリックリンク)は潜らず列挙から除外し、リンク越しの無限ループを防ぐ。
/// </summary>
public sealed class FileSystemFolderCompareSource : IFolderCompareSource
{
    private const int BufferSize = 64 * 1024;

    public IReadOnlyList<CompareDirEntry> List(string directoryPath)
    {
        var di = new DirectoryInfo(directoryPath);
        var list = new List<CompareDirEntry>();

        foreach (var sub in di.EnumerateDirectories())
        {
            if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            list.Add(new CompareDirEntry(sub.Name, true, 0, sub.LastWriteTimeUtc));
        }
        foreach (var file in di.EnumerateFiles())
        {
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            list.Add(new CompareDirEntry(file.Name, false, file.Length, file.LastWriteTimeUtc));
        }
        return list;
    }

    public bool ContentEquals(string leftFilePath, string rightFilePath)
    {
        var li = new FileInfo(leftFilePath);
        var ri = new FileInfo(rightFilePath);
        if (li.Length != ri.Length) return false;

        using var left = li.OpenRead();
        using var right = ri.OpenRead();
        var bufLeft = new byte[BufferSize];
        var bufRight = new byte[BufferSize];

        while (true)
        {
            int readLeft = ReadFull(left, bufLeft);
            int readRight = ReadFull(right, bufRight);
            if (readLeft != readRight) return false;        // 同サイズなので通常一致するが保険
            if (readLeft == 0) return true;                 // 両方とも末尾
            if (!bufLeft.AsSpan(0, readLeft).SequenceEqual(bufRight.AsSpan(0, readRight))) return false;
        }
    }

    /// <summary>バッファが満たされるか EOF まで読み、読み取れたバイト数を返す。</summary>
    private static int ReadFull(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = stream.Read(buffer, total, buffer.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
