namespace Filer.Core;

/// <summary>外部ツールの起動方式。</summary>
public enum ExternalToolKind
{
    /// <summary>実行ファイル(exe/cmd 等)を引数付きで起動する。</summary>
    Executable,

    /// <summary>ストアアプリ(UWP/パッケージアプリ)を AUMID でファイルアクティベーションする。</summary>
    StoreApp,
}

/// <summary>
/// ユーザー定義の外部ツール1つ。キー割り当ては <see cref="Gestures"/> に保持し、
/// アクション Id は <c>tool:&lt;Id&gt;</c> としてキーバインド表に載る。
/// </summary>
/// <param name="Id">安定した識別子(キー割り当ての参照キー)。重複不可。</param>
/// <param name="Label">表示名(メニュー・フッター・キー割り当て一覧で使う)。</param>
/// <param name="Kind">起動方式。</param>
/// <param name="Target">Executable なら実行ファイルパス(またはPATH上の名前)、StoreApp なら AUMID。</param>
/// <param name="Arguments">引数テンプレート(<see cref="ToolMacroExpander"/> のマクロを使える)。</param>
/// <param name="Gestures">割り当てキー(ジェスチャ文字列。空なら未割り当て)。</param>
public sealed record ExternalTool(
    string Id,
    string Label,
    ExternalToolKind Kind,
    string Target,
    string Arguments,
    IReadOnlyList<string> Gestures);

/// <summary>既定の外部ツール一式(初回起動時・設定リセット時に使う)。</summary>
public static class ExternalTools
{
    /// <summary>SkimDown(ストアアプリ)の既定 AppUserModelID。</summary>
    public const string SkimDownAumid = "45014okazuki.SkimDown_r82gs1ecy8g7c!App";

    /// <summary>従来どおりの VSCode / Windows Terminal / Git Bash / SkimDown を既定として返す。</summary>
    public static IReadOnlyList<ExternalTool> Defaults() => new[]
    {
        new ExternalTool("vscode", "VSCode", ExternalToolKind.Executable,
            "Code.exe", "$MF", new[] { "V" }),
        new ExternalTool("windows-terminal", "Windows Terminal", ExternalToolKind.Executable,
            "wt.exe", "-d \"$C\"", new[] { "G" }),
        new ExternalTool("git-bash", "Git Bash", ExternalToolKind.Executable,
            "git-bash.exe", "--cd=\"$C\"", new[] { "B" }),
        new ExternalTool("skimdown", "SkimDown", ExternalToolKind.StoreApp,
            SkimDownAumid, "$MF", new[] { "Shift+K" }),
    };
}
