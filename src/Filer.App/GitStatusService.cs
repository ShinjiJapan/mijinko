using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// git.exe を起動してリポジトリのステータスを取得する。git 未インストール・リポジトリ外は null。
/// </summary>
public static class GitStatusService
{
    /// <summary>git が見つからなかったら以後のプロセス起動を省略する。</summary>
    private static bool _gitUnavailable;

    public sealed record Result(string RepositoryRoot, GitStatusSnapshot Snapshot);

    public static async Task<Result?> QueryAsync(string directoryPath)
    {
        if (_gitUnavailable) return null;

        var root = GitRepositoryLocator.FindRoot(
            directoryPath, p => Directory.Exists(p) || File.Exists(p));
        if (root is null) return null;

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        // --no-optional-locks: 状態表示のためにインデックスを書き換えない(index.lock を作らない)
        psi.ArgumentList.Add("--no-optional-locks");
        psi.ArgumentList.Add("status");
        psi.ArgumentList.Add("--porcelain=v2");
        psi.ArgumentList.Add("--branch");
        psi.ArgumentList.Add("--ignored=matching");
        psi.ArgumentList.Add("-z");

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return null;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var output = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0) return null;   // .git はあるが有効なリポジトリでない等
            return new Result(root, GitStatusParser.Parse(output));
        }
        catch (Win32Exception)
        {
            _gitUnavailable = true;
            return null;
        }
    }
}
