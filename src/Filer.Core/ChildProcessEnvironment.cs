namespace Filer.Core;

/// <summary>
/// 外部ツールを子プロセスとして起動する際の環境変数を整える。
/// </summary>
public static class ChildProcessEnvironment
{
    /// <summary>
    /// Electron アプリ(VSCode 等)を GUI ではなく純粋な Node.js 実行へ切り替えてしまう変数。
    /// Filer 自身が VSCode の統合ターミナル・拡張ホスト等から起動されると継承され、
    /// 子の Code.exe が GUI を出さず引数をモジュールとして読もうとして即終了する。
    /// </summary>
    private static readonly string[] StripVars = { "ELECTRON_RUN_AS_NODE" };

    /// <summary>
    /// Electron 系ツールが Node 実行へ化けないよう、原因となる環境変数を取り除く。
    /// </summary>
    public static void Scrub(IDictionary<string, string?> environment)
    {
        foreach (var name in StripVars)
            environment.Remove(name);
    }
}
