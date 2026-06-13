using System.Diagnostics;
using System.IO.Pipes;
using System.Windows;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// カスタムエントリポイント。引数 <c>--mft-search-server &lt;pipe&gt; &lt;親PID&gt;</c> の場合は
/// WPF を起動せずヘッドレスで高速検索ヘルパー(<see cref="ElevatedSearchHost"/>)として動く。
/// それ以外は通常どおり WPF アプリを起動する。配布物は単一 exe のまま(別 exe を増やさない)。
/// </summary>
public static class Program
{
    /// <summary>ヘルパー起動を表す CLI 引数。</summary>
    public const string MftSearchServerArg = "--mft-search-server";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == MftSearchServerArg)
            return RunSearchHost(args[1], args.Length >= 3 ? args[2] : null);

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    /// <summary>
    /// 昇格ヘルパー本体。本体が立てた名前付きパイプ(サーバー)へクライアントとして接続し、
    /// 検索ループを回す。本体が終了したら自分も終了する(孤児防止)。
    /// </summary>
    private static int RunSearchHost(string pipeName, string? parentPidText)
    {
        if (int.TryParse(parentPidText, out var parentPid))
            WatchParent(parentPid);

        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(timeout: 15000);

            var host = new ElevatedSearchHost(pipe, FileSearcher.SearchWithInfo);
            host.RunAsync().GetAwaiter().GetResult();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>本体(親プロセス)の終了を監視し、終了したらヘルパーも終了する。</summary>
    private static void WatchParent(int parentPid)
    {
        try
        {
            var parent = Process.GetProcessById(parentPid);
            parent.EnableRaisingEvents = true;
            parent.Exited += (_, _) => Environment.Exit(0);
        }
        catch
        {
            // 既に終了している等。パイプ切断でも検知できるので無視する。
        }
    }
}
