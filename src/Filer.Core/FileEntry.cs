namespace Filer.Core;

/// <summary>
/// ファイル一覧の1エントリ。ディレクトリ・ファイル・親ディレクトリ("..")を表す。
/// </summary>
public sealed record FileEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTime LastModified)
{
    /// <summary>親ディレクトリ("..")エントリかどうか。</summary>
    public bool IsParent => Name == "..";

    /// <summary>書庫ファイル(.zip)で、Enter でフォルダーのように内部へ潜れるかどうか。</summary>
    public bool IsArchive { get; init; }

    /// <summary>親ディレクトリ("..")エントリを生成する。FullPath には移動先(親)の絶対パスを持つ。</summary>
    public static FileEntry Parent(string parentFullPath) =>
        new("..", parentFullPath, true, 0, default);
}
