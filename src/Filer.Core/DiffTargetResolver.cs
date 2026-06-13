using System.IO;

namespace Filer.Core;

/// <summary>差分対象の2ファイル(左/右)。</summary>
public sealed record DiffTargets(string LeftPath, string RightPath);

/// <summary>対象解決の結果。<see cref="Targets"/> か <see cref="Error"/> のどちらか一方が非 null。</summary>
public sealed record DiffResolution(DiffTargets? Targets, string? Error);

/// <summary>
/// 差分表示の対象2ファイルを決める(UI 非依存)。
/// アクティブペインでちょうど2件マークされていればその2件、マークが無ければ
/// 左ペインのカーソル項目 vs 右ペインのカーソル項目を対象とする。
/// </summary>
public static class DiffTargetResolver
{
    /// <summary>
    /// 対象を解決する。
    /// </summary>
    /// <param name="activeMarkedPaths">アクティブペインのマーク(一覧の並び順)のフルパス。</param>
    /// <param name="leftSelectedPath">左ペインのカーソル項目パス(".." 等はフォルダーパスになる)。</param>
    /// <param name="rightSelectedPath">右ペインのカーソル項目パス。</param>
    public static DiffResolution Resolve(
        IReadOnlyList<string> activeMarkedPaths, string leftSelectedPath, string rightSelectedPath)
    {
        string left, right;
        switch (activeMarkedPaths.Count)
        {
            case 2:
                left = activeMarkedPaths[0];
                right = activeMarkedPaths[1];
                break;
            case 0:
                left = leftSelectedPath;
                right = rightSelectedPath;
                break;
            default:
                return new DiffResolution(null,
                    "差分を表示するには、アクティブペインでファイルを2件マークするか、" +
                    "マークを外して両ペインのカーソルを比較したいファイルに合わせてください。");
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            return new DiffResolution(null, "同じファイル同士は比較できません。別々の2ファイルを指定してください。");

        if (NotAFile(left, out var leftError)) return new DiffResolution(null, leftError);
        if (NotAFile(right, out var rightError)) return new DiffResolution(null, rightError);

        return new DiffResolution(new DiffTargets(left, right), null);
    }

    /// <summary>パスが比較可能なファイルでなければ true(理由を <paramref name="error"/> に入れる)。</summary>
    private static bool NotAFile(string path, out string? error)
    {
        if (Directory.Exists(path))
        {
            error = $"フォルダーは比較できません:\n{path}";
            return true;
        }
        if (!File.Exists(path))
        {
            error = $"ファイルが見つかりません:\n{path}";
            return true;
        }
        error = null;
        return false;
    }
}
