using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Filer.Core;

/// <summary>
/// アプリ設定全体(キー割り当ての上書き+外部ツール一覧+外観テーマ+UIA軽量化+操作確認)。
/// LightweightListAutomation=true でファイル一覧の行を UI Automation の子として公開しない
/// (UIAクライアント常駐環境でのフォルダー移動を高速化。スクリーンリーダー等から行が見えなくなる)。
/// ConfirmMove=移動時、ConfirmRecycle=ごみ箱送り(D)時、ConfirmPermanentDelete=完全削除(Shift+D)時に
/// 確認ダイアログを出すか(いずれも既定 true)。
/// EnableElevatedFastSearch=非管理者起動時に昇格ヘルパー経由の高速検索(MFT 直読み)を使えるようにするか
/// (既定 true)。false なら高速検索ボタンを出さず UAC も起こさない。管理者起動時はこの設定によらず無関係。
/// MarkupPreviewMode=Markdown/HTML プレビューの初期表示モード(テキスト/ハイライト/レンダリング。既定はハイライト)。
/// </summary>
public sealed record AppSettings(
    IReadOnlyDictionary<string, string[]> KeyBindingOverrides,
    IReadOnlyList<ExternalTool> Tools,
    AppTheme Theme = AppTheme.Dark,
    bool LightweightListAutomation = false,
    bool ConfirmMove = true,
    bool ConfirmRecycle = true,
    bool ConfirmPermanentDelete = true,
    bool EnableElevatedFastSearch = true,
    MarkupPreviewMode MarkupPreviewMode = MarkupPreviewMode.Highlight)
{
    public static AppSettings CreateDefault() =>
        new(new Dictionary<string, string[]>(), ExternalTools.Defaults());
}

/// <summary>
/// アプリ設定の JSON 永続化(%APPDATA%\Filer\settings.json)。
/// 読み込み失敗(未保存・破損)は既定値。キー割り当ては既定と異なる操作だけを保存する。
/// 外部ツールはユーザー定義一覧をそのまま保存する。
/// </summary>
public sealed class AppSettingsStore
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 日本語を可読のまま保存
        Converters = { new JsonStringEnumConverter() },
    };

    public AppSettingsStore(string filePath) => _filePath = filePath;

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
            return AppSettings.CreateDefault();
        try
        {
            var dto = JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(_filePath), Options);
            if (dto is null)
                return AppSettings.CreateDefault();
            return new AppSettings(
                dto.KeyBindings ?? new Dictionary<string, string[]>(),
                // tools キーが無い(未設定)なら既定ツール、空配列なら「ツール無し」を尊重する。
                dto.ExternalTools is null
                    ? ExternalTools.Defaults()
                    : dto.ExternalTools.Where(t => t is not null).Select(ToModel).ToList(),
                // 未知/未設定のテーマは既定(Dark)。
                Enum.TryParse<AppTheme>(dto.Theme, ignoreCase: true, out var theme) ? theme : AppTheme.Dark,
                dto.LightweightListAutomation ?? false,
                dto.ConfirmMove ?? true,
                dto.ConfirmRecycle ?? true,
                dto.ConfirmPermanentDelete ?? true,
                dto.EnableElevatedFastSearch ?? true,
                // 未知/未設定の値は既定(ハイライト)。
                Enum.TryParse<MarkupPreviewMode>(dto.MarkupPreviewMode, ignoreCase: true, out var markupMode)
                    ? markupMode : MarkupPreviewMode.Highlight);
        }
        catch (JsonException)
        {
            return AppSettings.CreateDefault();   // 破損ファイルは既定値で扱う
        }
    }

    public void Save(AppSettings settings)
    {
        var dto = new SettingsDto
        {
            KeyBindings = DiffFromDefaults(settings.KeyBindingOverrides),
            ExternalTools = settings.Tools.Select(ToDto).ToList(),
            Theme = settings.Theme.ToString(),
            LightweightListAutomation = settings.LightweightListAutomation,
            ConfirmMove = settings.ConfirmMove,
            ConfirmRecycle = settings.ConfirmRecycle,
            ConfirmPermanentDelete = settings.ConfirmPermanentDelete,
            EnableElevatedFastSearch = settings.EnableElevatedFastSearch,
            MarkupPreviewMode = settings.MarkupPreviewMode.ToString(),
        };

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(dto, Options));
    }

    /// <summary>既定とまったく同じ割り当ての上書きは保存対象から外す。</summary>
    private static Dictionary<string, string[]> DiffFromDefaults(
        IReadOnlyDictionary<string, string[]> overrides)
    {
        var result = new Dictionary<string, string[]>();
        foreach (var (id, gestures) in overrides)
        {
            var action = KeyBindingActions.Find(id);
            if (action is not null && NormalizedEquals(gestures, action.DefaultGestures))
                continue;
            result[id] = gestures;
        }
        return result;
    }

    private static bool NormalizedEquals(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        static IEnumerable<string> Norm(IReadOnlyList<string> list) =>
            list.Select(g => KeyChord.TryParse(g, out var c) ? c.Normalized : g.ToUpperInvariant());
        return Norm(a).SequenceEqual(Norm(b));
    }

    private static ExternalTool ToModel(ToolDto t) => new(
        t.Id ?? "",
        t.Label ?? "",
        Enum.TryParse<ExternalToolKind>(t.Kind, ignoreCase: true, out var kind) ? kind : ExternalToolKind.Executable,
        t.Target ?? "",
        t.Arguments ?? "",
        t.Gestures ?? Array.Empty<string>());

    private static ToolDto ToDto(ExternalTool t) => new()
    {
        Id = t.Id,
        Label = t.Label,
        Kind = t.Kind.ToString(),
        Target = t.Target,
        Arguments = t.Arguments,
        Gestures = t.Gestures.ToArray(),
    };

    private sealed class SettingsDto
    {
        public Dictionary<string, string[]>? KeyBindings { get; set; }
        public List<ToolDto>? ExternalTools { get; set; }
        public string? Theme { get; set; }
        public bool? LightweightListAutomation { get; set; }
        public bool? ConfirmMove { get; set; }
        public bool? ConfirmRecycle { get; set; }
        public bool? ConfirmPermanentDelete { get; set; }
        public bool? EnableElevatedFastSearch { get; set; }
        public string? MarkupPreviewMode { get; set; }
    }

    private sealed class ToolDto
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public string? Kind { get; set; }
        public string? Target { get; set; }
        public string? Arguments { get; set; }
        public string[]? Gestures { get; set; }
    }
}
