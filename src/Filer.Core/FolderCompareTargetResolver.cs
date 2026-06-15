using System.IO;

namespace Filer.Core;

/// <summary>比較対象の2フォルダー(左/右)。</summary>
public sealed record FolderCompareTargets(string LeftPath, string RightPath);

/// <summary>対象解決の結果。<see cref="Targets"/> か <see cref="Error"/> のどちらか一方が非 null。</summary>
public sealed record FolderCompareResolution(FolderCompareTargets? Targets, string? Error);

/// <summary>
/// フォルダー比較の対象2フォルダーを決める(UI 非依存)。
/// 左ペインのフォルダー vs 右ペインのフォルダーを対象とし、同一パス・不存在は理由付きでエラーにする。
/// </summary>
public static class FolderCompareTargetResolver
{
    public static FolderCompareResolution Resolve(string leftPath, string rightPath)
    {
        if (string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase))
            return new FolderCompareResolution(null, "同じフォルダー同士は比較できません。左右のペインに別々のフォルダーを開いてください。");

        if (NotADirectory(leftPath, out var leftError)) return new FolderCompareResolution(null, leftError);
        if (NotADirectory(rightPath, out var rightError)) return new FolderCompareResolution(null, rightError);

        return new FolderCompareResolution(new FolderCompareTargets(leftPath, rightPath), null);
    }

    /// <summary>パスが存在するフォルダーでなければ true(理由を <paramref name="error"/> に入れる)。</summary>
    private static bool NotADirectory(string path, out string? error)
    {
        if (!Directory.Exists(path))
        {
            error = File.Exists(path)
                ? $"ファイルはフォルダー比較できません:\n{path}"
                : $"フォルダーが見つかりません:\n{path}";
            return true;
        }
        error = null;
        return false;
    }
}
