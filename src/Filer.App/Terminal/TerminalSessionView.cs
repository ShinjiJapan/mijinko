using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Filer.Core;

namespace Filer.App.Terminal;

/// <summary>
/// ターミナル1タブ分のビュー。xterm.js(WebView2)と <see cref="ConPtySession"/> を橋渡しする。
/// ページからの input/resize はシェルへ、シェルの出力はページの term.write へ流す。
/// </summary>
public sealed class TerminalSessionView : Border
{
    private const string TerminalHost = "filer.terminal";

    private readonly WebView2 _web = new();
    private readonly ConPtySession _session;
    private readonly StringBuilder _pendingOutput = new();
    private bool _flushQueued;
    private bool _started;
    private bool _disposed;

    /// <summary>このタブのシェル種別(タブ見出しに使う)。</summary>
    public TerminalProfile Profile { get; }

    /// <summary>シェルプロセスが終了した(タブを閉じてよい)。UI スレッドで発火する。</summary>
    public event Action<TerminalSessionView>? SessionExited;

    /// <summary>ターミナル内で Ctrl+T が押された(ファイラーの一覧へフォーカスを戻す)。</summary>
    public event Action? FocusListRequested;

    /// <summary>ターミナル内で F1 が押された(表示形態を切り替える)。</summary>
    public event Action? CycleViewRequested;

    public TerminalSessionView(TerminalProfile profile, string workingDirectory)
    {
        Profile = profile;
        Child = _web;
        _session = new ConPtySession(profile.ExePath, profile.Arguments, workingDirectory,
            cols: 80, rows: 24);
        _session.Output += OnSessionOutput;
        _session.Exited += () => Dispatcher.BeginInvoke(() => SessionExited?.Invoke(this));
    }

    /// <summary>WebView2 を初期化し xterm ページを読み込む。シェルはページの ready 通知で起動する。</summary>
    public async Task InitializeAsync(CoreWebView2Environment environment, string assetsDir)
    {
        await _web.EnsureCoreWebView2Async(environment);
        _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            TerminalHost, assetsDir, CoreWebView2HostResourceAccessKind.Allow);
        _web.CoreWebView2.WebMessageReceived += OnWebMessage;
        _web.CoreWebView2.Navigate($"https://{TerminalHost}/terminal.html");
    }

    /// <summary>ターミナル(ページ内の xterm)へフォーカスを移す。</summary>
    public void FocusTerminal()
    {
        _web.Focus();
        Keyboard.Focus(_web);
        try { _web.CoreWebView2?.PostWebMessageAsJson("""{"type":"focus"}"""); }
        catch (InvalidOperationException) { }   // 初期化前は ready 後の term.focus() に任せる
    }

    /// <summary>シェルを強制終了し WebView を破棄する(タブを閉じる/アプリ終了時)。</summary>
    public void DisposeSession()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
        _web.Dispose();
    }

    /// <summary>ページからの通知(ready/input/resize/focus-list)を処理する。</summary>
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
        var root = doc.RootElement;
        switch (root.GetProperty("type").GetString())
        {
            case "ready":
                if (_started) break;
                _started = true;
                _session.Resize(root.GetProperty("cols").GetInt32(), root.GetProperty("rows").GetInt32());
                _session.Start();
                // 表示中(アクティブタブ)なら、ページ準備完了の時点でターミナルへフォーカスを移す。
                if (Visibility == System.Windows.Visibility.Visible)
                    FocusTerminal();
                break;
            case "input":
                _session.WriteInput(root.GetProperty("data").GetString() ?? string.Empty);
                break;
            case "resize":
                _session.Resize(root.GetProperty("cols").GetInt32(), root.GetProperty("rows").GetInt32());
                break;
            case "focus-list":
                FocusListRequested?.Invoke();
                break;
            case "cycle-view":
                CycleViewRequested?.Invoke();
                break;
        }
    }

    /// <summary>
    /// シェル出力をページへ流す。出力は読み取りスレッドから届くため、
    /// バッファに溜めて UI スレッドでまとめて送る(大量出力時のディスパッチ洪水を防ぐ)。
    /// </summary>
    private void OnSessionOutput(string text)
    {
        lock (_pendingOutput)
        {
            _pendingOutput.Append(text);
            if (_flushQueued) return;
            _flushQueued = true;
        }
        Dispatcher.BeginInvoke(FlushOutput);
    }

    private void FlushOutput()
    {
        string text;
        lock (_pendingOutput)
        {
            text = _pendingOutput.ToString();
            _pendingOutput.Clear();
            _flushQueued = false;
        }
        if (text.Length == 0 || _disposed) return;
        _web.CoreWebView2?.PostWebMessageAsString(text);
    }
}
