namespace Filer.Core;

/// <summary>一覧のソート方法。</summary>
public enum SortKey
{
    Name,
    Extension,
    Date,
    Size,
}

/// <summary>
/// ファイル一覧を指定のキー・昇降順で並べ替える(UI 非依存)。
/// 親("..")は常に先頭、続いてディレクトリ群、ファイル群の順を保ち、各群内をキーで並べる。
/// </summary>
public static class EntrySorter
{
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<FileEntry> Sort(
        IReadOnlyList<FileEntry> entries, SortKey key, bool descending)
    {
        var result = new List<FileEntry>(entries.Count);
        result.AddRange(entries.Where(e => e.IsParent));
        result.AddRange(Order(entries.Where(e => e.IsDirectory && !e.IsParent), key, descending));
        result.AddRange(Order(entries.Where(e => !e.IsDirectory), key, descending));
        return result;
    }

    private static IEnumerable<FileEntry> Order(
        IEnumerable<FileEntry> items, SortKey key, bool descending)
    {
        IOrderedEnumerable<FileEntry> ordered = key switch
        {
            SortKey.Extension => descending
                ? items.OrderByDescending(e => FileNameParts.Split(e.Name).Extension, Cmp)
                : items.OrderBy(e => FileNameParts.Split(e.Name).Extension, Cmp),
            SortKey.Date => descending
                ? items.OrderByDescending(e => e.LastModified)
                : items.OrderBy(e => e.LastModified),
            SortKey.Size => descending
                ? items.OrderByDescending(e => e.Size)
                : items.OrderBy(e => e.Size),
            _ => descending
                ? items.OrderByDescending(e => e.Name, Cmp)
                : items.OrderBy(e => e.Name, Cmp),
        };
        // 同値は名前順で安定させる(名前キー時は実質無効)。
        return ordered.ThenBy(e => e.Name, Cmp);
    }
}
