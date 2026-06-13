using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Filer.Core;

namespace Filer.App.ExternalTools;

/// <summary>
/// ユーザー定義の外部ツール(<see cref="ExternalTool"/>)を起動する。
/// 実行ファイルは引数テンプレート(<see cref="ToolMacroExpander"/> のマクロ)を展開して起動し、
/// ストアアプリは AUMID でファイルアクティベーション(<see cref="UwpLauncher"/>)する。
/// 実行ファイルの解決: 絶対パスはそのまま(実在必須)、名前だけなら既知の場所→PATH/シェル経由。
/// </summary>
public sealed class ExternalToolLauncher
{
    /// <summary>
    /// ツールを起動する。引数テンプレートを文脈で展開する。
    /// $MO が空でキャンセルされた場合など、展開結果が無ければ何もしない。
    /// </summary>
    public void Launch(ExternalTool tool, ToolMacroContext context)
    {
        if (tool.Kind == ExternalToolKind.StoreApp)
        {
            var path = ToolMacroExpander.ExpandToSinglePath(tool.Arguments, context);
            if (path is null) return;   // 対象なし(キャンセル)
            UwpLauncher.Open(tool.Target.Trim(), path);
            return;
        }

        // Executable
        var args = ToolMacroExpander.Expand(tool.Arguments, context);
        if (args is null) return;       // $MO が空 → コマンド自体キャンセル

        var exe = ResolveExecutable(tool.Target);
        if (exe is not null)
        {
            var psi = new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = false };
            // Filer が VSCode 統合ターミナル等(ELECTRON_RUN_AS_NODE=1)から起動されると、
            // 継承により子の Code.exe が GUI ではなく Node 実行になって即終了する。原因変数を除去。
            ChildProcessEnvironment.Scrub(psi.Environment);
            Start(psi);
        }
        else
            // 絶対パスでなく既知の場所にも無い → PATH 上にある想定でシェル経由起動。
            Start(new ProcessStartInfo { FileName = tool.Target.Trim(), Arguments = args, UseShellExecute = true });
    }

    /// <summary>
    /// 実行ファイルのパスを解決する。絶対パスは実在必須でそのまま、
    /// 名前だけ(例: Code.exe)なら既知の場所を探す。見つからなければ null(シェル経由起動へ)。
    /// </summary>
    private static string? ResolveExecutable(string target)
    {
        var t = target.Trim();
        if (t.Length == 0)
            throw new InvalidOperationException("外部ツールの実行ファイルが設定されていません。設定(Z)で指定してください。");

        if (Path.IsPathRooted(t))
        {
            if (!File.Exists(t))
                throw new FileNotFoundException(
                    $"設定された実行ファイルが見つかりません:\n{t}\n設定(Z)で確認してください。");
            return t;
        }

        // 名前だけ指定: 既知の場所を探す(無ければ null=シェル経由)。
        return ResolveKnownExecutable(t);
    }

    /// <summary>よく使う実行ファイル名を既知のインストール先から解決する(無ければ null)。</summary>
    private static string? ResolveKnownExecutable(string fileName)
    {
        if (KnownLocations.TryGetValue(fileName, out var candidates))
            return Array.Find(candidates, File.Exists);
        return null;
    }

    private static readonly Dictionary<string, string[]> KnownLocations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code.exe"] = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft VS Code", "Code.exe"),
        },
        ["wt.exe"] = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "wt.exe"),
        },
        ["git-bash.exe"] = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Git", "git-bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Git", "git-bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Git", "git-bash.exe"),
        },
    };

    private static void Start(ProcessStartInfo psi)
    {
        psi.WindowStyle = ProcessWindowStyle.Normal;
        Process.Start(psi)?.Dispose();   // 起動のみ。プロセスハンドルは即解放(プロセス自体は継続)
    }
}
