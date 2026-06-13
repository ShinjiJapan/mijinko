using System.Text;
using Filer.Core;

namespace Filer.Core.Tests;

public sealed class ConPtySessionTests
{
    /// <summary>output に expected が現れるまで待つ(タイムアウトで false)。</summary>
    private static bool WaitForOutput(StringBuilder output, string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (output)
                if (output.ToString().Contains(expected)) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    [Fact]
    public void Start_CmdEcho_RaisesOutputAndExited()
    {
        var output = new StringBuilder();
        using var exited = new ManualResetEventSlim();
        using var session = new ConPtySession(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            "/c echo conpty-marker-ok", Environment.SystemDirectory, cols: 80, rows: 24);
        session.Output += chunk => { lock (output) output.Append(chunk); };
        session.Exited += () => exited.Set();

        session.Start();

        // Exited は全出力の処理後に発火する契約。
        Assert.True(exited.Wait(TimeSpan.FromSeconds(20)), "プロセスが時間内に終了しませんでした。");
        lock (output)
            Assert.Contains("conpty-marker-ok", output.ToString());
    }

    [Fact]
    public void WriteInput_CmdReceivesCommand_OutputsResult()
    {
        var output = new StringBuilder();
        using var exited = new ManualResetEventSlim();
        using var session = new ConPtySession(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            "", Environment.SystemDirectory, cols: 80, rows: 24);
        session.Output += chunk => { lock (output) output.Append(chunk); };
        session.Exited += () => exited.Set();

        session.Start();
        // 対話シェルなのでプロンプトが出てから入力する(実利用と同じ流れ)。
        Assert.True(WaitForOutput(output, ">", TimeSpan.FromSeconds(15)), "プロンプトが表示されません。");
        session.WriteInput("echo input-marker-ok\r");
        Assert.True(WaitForOutput(output, "input-marker-ok", TimeSpan.FromSeconds(15)),
            "入力したコマンドの出力がありません。");
        session.WriteInput("exit\r");

        Assert.True(exited.Wait(TimeSpan.FromSeconds(20)), "プロセスが時間内に終了しませんでした。");
    }

    [Fact]
    public void Dispose_RunningProcess_TerminatesWithoutHang()
    {
        using var exited = new ManualResetEventSlim();
        var session = new ConPtySession(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            "", Environment.SystemDirectory, cols: 80, rows: 24);
        session.Exited += () => exited.Set();
        session.Start();

        session.Dispose();   // 対話シェルを強制終了する

        Assert.True(exited.Wait(TimeSpan.FromSeconds(20)), "Dispose 後もプロセスが残っています。");
    }

    [Fact]
    public void Resize_WhileRunning_DoesNotThrow()
    {
        var output = new StringBuilder();
        using var exited = new ManualResetEventSlim();
        using var session = new ConPtySession(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            "", Environment.SystemDirectory, cols: 80, rows: 24);
        session.Output += chunk => { lock (output) output.Append(chunk); };
        session.Exited += () => exited.Set();
        session.Start();
        Assert.True(WaitForOutput(output, ">", TimeSpan.FromSeconds(15)), "プロンプトが表示されません。");

        session.Resize(120, 40);
        session.WriteInput("exit\r");

        Assert.True(exited.Wait(TimeSpan.FromSeconds(20)));
    }
}
