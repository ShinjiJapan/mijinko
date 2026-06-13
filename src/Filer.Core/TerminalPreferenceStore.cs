using System.Text.Json;

namespace Filer.Core;

/// <summary>ターミナルの設定(前回開いたシェル種別名)。</summary>
public sealed record TerminalPreference(string LastProfileName);

/// <summary>
/// 最後に開いたターミナルの種類を JSON ファイルへ永続化し、次回の既定シェルとして使う。
/// 読み込み失敗(未保存・破損)は null を返す(呼び出し側で先頭シェルを既定にする)。
/// </summary>
public sealed class TerminalPreferenceStore
{
    private readonly string _filePath;

    public TerminalPreferenceStore(string filePath) => _filePath = filePath;

    /// <summary>前回開いたシェル種別名を読み込む。無ければ null。</summary>
    public string? LoadLastProfileName()
    {
        if (!File.Exists(_filePath))
            return null;
        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            var pref = JsonSerializer.Deserialize<TerminalPreference>(json);
            return string.IsNullOrEmpty(pref?.LastProfileName) ? null : pref.LastProfileName;
        }
        catch (JsonException)
        {
            return null;   // 破損ファイルは既定値で扱う
        }
    }

    /// <summary>開いたシェル種別名を保存する。</summary>
    public void SaveLastProfileName(string name)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath,
            JsonSerializer.Serialize(new TerminalPreference(name),
                new JsonSerializerOptions { WriteIndented = true }));
    }
}
