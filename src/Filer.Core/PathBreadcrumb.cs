namespace Filer.Core;

/// <summary>
/// パンくず(階層ナビゲーション)の1区切り。<see cref="Name"/> は表示名、
/// <see cref="Path"/> はその区切りをクリックしたときの移動先絶対パス。
/// </summary>
public sealed record BreadcrumbSegment(string Name, string Path);

/// <summary>
/// 絶対パスを「ルート→各階層」の累積パンくず列に分割する。
/// エクスプローラー風のパス表示(階層ごとに移動可能)に使う。
/// 書庫(.zip)内の仮想パスも通常パスと同様に区切り単位で分割する。
/// </summary>
public static class PathBreadcrumb
{
    public static IReadOnlyList<BreadcrumbSegment> Build(string path)
    {
        var segments = new List<BreadcrumbSegment>();
        if (string.IsNullOrWhiteSpace(path)) return segments;

        var trimmed = path.Replace('/', '\\').TrimEnd('\\');
        if (trimmed.Length == 0) return segments;

        string root;
        string remainder;

        if (trimmed.StartsWith(@"\\"))
        {
            // UNC: \\server\share[\sub...] の \\server\share をルートとする。
            var afterPrefix = trimmed[2..];
            var firstSlash = afterPrefix.IndexOf('\\');
            if (firstSlash < 0)
            {
                segments.Add(new BreadcrumbSegment(trimmed, trimmed));
                return segments;
            }
            var secondSlash = afterPrefix.IndexOf('\\', firstSlash + 1);
            if (secondSlash < 0)
            {
                root = trimmed;
                remainder = "";
            }
            else
            {
                root = @"\\" + afterPrefix[..secondSlash];
                remainder = afterPrefix[(secondSlash + 1)..];
            }
        }
        else
        {
            var colon = trimmed.IndexOf(':');
            if (colon >= 0)
            {
                root = trimmed[..(colon + 1)] + "\\";   // "G:\"
                remainder = colon + 1 < trimmed.Length
                    ? trimmed[(colon + 1)..].TrimStart('\\')
                    : "";
            }
            else
            {
                root = "";
                remainder = trimmed;
            }
        }

        string accum;
        if (root.Length > 0)
        {
            segments.Add(new BreadcrumbSegment(root, root));
            accum = root.TrimEnd('\\');
        }
        else
        {
            accum = "";
        }

        foreach (var part in remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            accum = accum.Length == 0 ? part : accum + "\\" + part;
            segments.Add(new BreadcrumbSegment(part, accum));
        }

        return segments;
    }
}
