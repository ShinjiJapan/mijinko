namespace Filer.Core;

/// <summary>組み込みターミナルで起動できるシェル1種。</summary>
/// <param name="Name">表示名(タブ見出し・種類選択に使う)。</param>
/// <param name="ExePath">実行ファイルの絶対パス。</param>
/// <param name="Arguments">起動引数(不要なら空文字)。</param>
public sealed record TerminalProfile(string Name, string ExePath, string Arguments);

/// <summary>
/// この PC で利用できるターミナルの種類(シェル)を既知のインストール先から検出する。
/// 先頭の項目を既定のシェルとして扱う。
/// </summary>
public static class TerminalProfiles
{
    /// <summary>実環境のファイル存在判定で検出する。</summary>
    public static IReadOnlyList<TerminalProfile> Detect() => Detect(File.Exists);

    /// <summary>存在判定を差し替えて検出する(テスト用)。</summary>
    public static IReadOnlyList<TerminalProfile> Detect(Func<string, bool> fileExists)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var candidates = new[]
        {
            new TerminalProfile("PowerShell",
                Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"), string.Empty),
            new TerminalProfile("PowerShell 7",
                Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"), string.Empty),
            new TerminalProfile("コマンド プロンプト",
                Path.Combine(system, "cmd.exe"), string.Empty),
            new TerminalProfile("Git Bash",
                Path.Combine(programFiles, "Git", "bin", "bash.exe"), "--login -i"),
        };
        return candidates.Where(p => fileExists(p.ExePath)).ToList();
    }
}
