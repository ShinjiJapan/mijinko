using System.Text.Json;

namespace Filer.Core;

/// <summary>1ペイン分の保存状態(開いている全タブのパス・アクティブタブ位置・一覧表示モード・グリッドのタイルサイズ)。</summary>
public sealed record SessionPane(
    IReadOnlyList<string> TabPaths,
    int ActiveTabIndex,
    PaneViewMode ViewMode = PaneViewMode.Details,
    GridTileSize GridSize = GridTileSize.Normal);

/// <summary>前回終了時のウィンドウ位置・サイズ(最大化時は復元用の通常サイズ)。</summary>
public sealed record WindowBounds(double Left, double Top, double Width, double Height, bool Maximized);

/// <summary>前回終了時の2ペインの状態(各ペインのタブ構成とアクティブ側、ウィンドウ位置)。</summary>
public sealed record SessionState(SessionPane Left, SessionPane Right, bool IsLeftActive, WindowBounds? Window = null);

/// <summary>
/// 終了時のペイン状態を JSON ファイルへ永続化し、次回起動時に復元する。
/// 読み込みに失敗した場合(未保存・破損)は null を返す(呼び出し側で既定値を使う)。
/// </summary>
public sealed class SessionStore
{
    private readonly string _filePath;

    public SessionStore(string filePath) => _filePath = filePath;

    /// <summary>保存済みのセッション状態を読み込む。無ければ null。</summary>
    public SessionState? Load()
    {
        if (!File.Exists(_filePath))
            return null;
        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var state = JsonSerializer.Deserialize<SessionState>(json);
            // 旧形式・欠損データ(タブ情報なし)は復元しない。呼び出し側で既定値を使う。
            if (state?.Left is null || state.Right is null)
                return null;
            return state;
        }
        catch (JsonException)
        {
            // 破損したセッションファイルは復元せず既定値で起動する(起動を妨げない)。
            return null;
        }
    }

    /// <summary>セッション状態を保存する。</summary>
    public void Save(SessionState state)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }
}
