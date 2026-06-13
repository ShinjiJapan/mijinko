using Filer.Core;

namespace Filer.Core.Tests;

public sealed class ChildProcessEnvironmentTests
{
    [Fact]
    public void Scrub_RemovesElectronRunAsNode()
    {
        // Filer が VSCode 統合ターミナル等から起動されると ELECTRON_RUN_AS_NODE=1 を継承し、
        // 子の Code.exe が GUI ではなく Node 実行になってしまう。除去されること。
        var env = new Dictionary<string, string?>
        {
            ["ELECTRON_RUN_AS_NODE"] = "1",
            ["PATH"] = "C:\\bin",
        };

        ChildProcessEnvironment.Scrub(env);

        Assert.False(env.ContainsKey("ELECTRON_RUN_AS_NODE"));
        Assert.Equal("C:\\bin", env["PATH"]);   // 無関係な変数は残す
    }

    [Fact]
    public void Scrub_WhenVariableAbsent_LeavesEnvironmentUnchanged()
    {
        var env = new Dictionary<string, string?> { ["PATH"] = "C:\\bin" };

        ChildProcessEnvironment.Scrub(env);

        Assert.Single(env);
        Assert.Equal("C:\\bin", env["PATH"]);
    }
}
