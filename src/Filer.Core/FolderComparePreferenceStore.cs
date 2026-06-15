using System.Text.Json;

namespace Filer.Core;

/// <summary>
/// フォルダー比較オプション(比較基準・再帰・同一表示)を JSON へ永続化し、次回の既定値として使う。
/// 読み込み失敗(未保存・破損)は既定値(<see cref="FolderCompareOptions"/> の既定)を返す。
/// </summary>
public sealed class FolderComparePreferenceStore
{
    private readonly string _filePath;

    public FolderComparePreferenceStore(string filePath) => _filePath = filePath;

    /// <summary>前回のオプションを読み込む。無ければ既定値。</summary>
    public FolderCompareOptions Load()
    {
        if (!File.Exists(_filePath))
            return new FolderCompareOptions();
        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new FolderCompareOptions();
            return JsonSerializer.Deserialize<FolderCompareOptions>(json) ?? new FolderCompareOptions();
        }
        catch (JsonException)
        {
            return new FolderCompareOptions();   // 破損ファイルは既定値で扱う
        }
    }

    /// <summary>オプションを保存する。</summary>
    public void Save(FolderCompareOptions options)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath,
            JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
    }
}
