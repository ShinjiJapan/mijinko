using System.Text.Json;

namespace Filer.Core;

/// <summary>
/// 開いたフォルダーの履歴(最近使った順)を JSON ファイルへ永続化する。
/// 新しいものが先頭。大文字小文字を無視して重複を排除し、最大件数を超えた古い履歴は捨てる。
/// </summary>
public sealed class HistoryStore
{
    private readonly string _filePath;
    private readonly int _maxEntries;

    public HistoryStore(string filePath, int maxEntries = 50)
    {
        _filePath = filePath;
        _maxEntries = Math.Max(1, maxEntries);
    }

    /// <summary>履歴を新しい順に取得する。</summary>
    public IReadOnlyList<string> GetAll() => Load();

    /// <summary>
    /// フォルダーを履歴へ記録する。既存(大文字小文字無視)は先頭へ移動し、
    /// 最大件数を超えた末尾は捨てる。空文字は無視する。
    /// </summary>
    public void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var list = Load();
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > _maxEntries)
            list.RemoveRange(_maxEntries, list.Count - _maxEntries);
        Save(list);
    }

    private List<string> Load()
    {
        if (!File.Exists(_filePath))
            return new List<string>();
        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private void Save(List<string> list)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }
}
