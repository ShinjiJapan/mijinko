namespace Filer.Core;

/// <summary>
/// ディレクトリの内容を読み取る抽象。実ファイルシステムとテスト用フェイクを差し替えるために用いる。
/// </summary>
public interface IDirectoryReader
{
    /// <summary>
    /// 指定パスのエントリ一覧を返す。先頭に親("..")、続いてディレクトリ、ファイルの順で返すこと。
    /// </summary>
    IReadOnlyList<FileEntry> Read(string path);
}
