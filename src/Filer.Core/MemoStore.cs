namespace Filer.Core;

/// <summary>
/// メモ(反対ペインに表示する書き捨てテキスト)を1つのテキストファイルへ永続化する。
/// VSCode のように入力内容を保存し、次回起動時に復元するための単純な読み書き。
/// </summary>
public sealed class MemoStore
{
    private readonly string _filePath;

    public MemoStore(string filePath) => _filePath = filePath;

    /// <summary>保存済みのメモ本文を読み込む。ファイルが無ければ空文字。</summary>
    public string Load() => File.Exists(_filePath) ? File.ReadAllText(_filePath) : "";

    /// <summary>メモ本文を保存する(全置き換え)。フォルダーが無ければ作成する。</summary>
    public void Save(string text)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, text);
    }
}
