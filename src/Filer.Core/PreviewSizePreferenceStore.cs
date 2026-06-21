using System.Text.Encodings.Web;
using System.Text.Json;

namespace Filer.Core;

/// <summary>
/// プレビュー種別ごとの表示形態(全画面 / 1ペイン領域)を %APPDATA%\Filer\preview-size-prefs.json へ
/// 永続化し、次回同種別のプレビューを前回と同じ形態で開く。未保存・破損は既定(全画面)。
/// 起動時にファイルを読み込み、<see cref="Set"/> のたびに即保存する。
/// </summary>
public sealed class PreviewSizePreferenceStore
{
    private readonly string _filePath;
    // 種別 → 全画面か(未登録の種別は既定の全画面で開く)。
    private readonly Dictionary<PreviewKind, bool> _fullScreen;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public PreviewSizePreferenceStore(string filePath)
    {
        _filePath = filePath;
        _fullScreen = Load();
    }

    /// <summary>指定種別を全画面で開くか。保存が無ければ既定の全画面(true)。</summary>
    public bool IsFullScreen(PreviewKind kind) =>
        !_fullScreen.TryGetValue(kind, out var value) || value;

    /// <summary>指定種別の表示形態を記録し、ファイルへ保存する。</summary>
    public void Set(PreviewKind kind, bool fullScreen)
    {
        _fullScreen[kind] = fullScreen;
        Save();
    }

    /// <summary>保存済みの種別ごとの表示形態を読み込む。無ければ空(全種別が既定の全画面)。</summary>
    private Dictionary<PreviewKind, bool> Load()
    {
        if (!File.Exists(_filePath))
            return new();
        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new();
            var dto = JsonSerializer.Deserialize<Dictionary<string, bool>>(json, Options);
            if (dto is null)
                return new();
            var result = new Dictionary<PreviewKind, bool>();
            foreach (var (name, fullScreen) in dto)
                if (Enum.TryParse<PreviewKind>(name, ignoreCase: true, out var kind))
                    result[kind] = fullScreen;
            return result;
        }
        catch (JsonException)
        {
            return new();   // 破損ファイルは既定値で扱う
        }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        // 種別名(文字列)をキーに保存して可読性と enum 並び替え耐性を確保する。
        var dto = _fullScreen.ToDictionary(p => p.Key.ToString(), p => p.Value);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(dto, Options));
    }
}
