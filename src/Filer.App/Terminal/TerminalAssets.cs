using System.IO;
using System.Reflection;

namespace Filer.App.Terminal;

/// <summary>
/// 組み込みターミナルの Web アセット(xterm.js 一式と terminal.html)を
/// 作業フォルダー(%LOCALAPPDATA%\Filer\terminal)へ展開する。
/// WebView2 の仮想ホスト経由で同一オリジンとして読み込む前提。
/// </summary>
public static class TerminalAssets
{
    /// <summary>アセットを展開し、展開先フォルダーのパスを返す(展開済みならスキップ)。</summary>
    public static string EnsureExtracted()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Filer", "terminal");
        Directory.CreateDirectory(dir);

        ExtractResource(dir, "xterm.min.js");
        ExtractResource(dir, "xterm.css");
        ExtractResource(dir, "xterm-addon-fit.min.js");

        var htmlPath = Path.Combine(dir, "terminal.html");
        if (!File.Exists(htmlPath) || File.ReadAllText(htmlPath) != Html)
            File.WriteAllText(htmlPath, Html);
        return dir;
    }

    /// <summary>埋め込みリソースを展開先へ書き出す(サイズ一致なら最新とみなしスキップ)。</summary>
    private static void ExtractResource(string dir, string fileName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = Array.Find(asm.GetManifestResourceNames(),
            n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"埋め込みリソース {fileName} が見つかりません。");

        using var resource = asm.GetManifestResourceStream(resourceName)!;
        var dest = Path.Combine(dir, fileName);
        if (File.Exists(dest) && new FileInfo(dest).Length == resource.Length) return;

        using var file = File.Create(dest);
        resource.CopyTo(file);
    }

    // xterm.js をホストするページ。キー入力(onData)・サイズ(fit)・フォーカス戻し(Ctrl+T)を
    // chrome.webview.postMessage の JSON でホスト側とやり取りする。
    // ホスト→ページ: 文字列メッセージ=端末出力(term.write)、JSON {type:'focus'}=フォーカス要求。
    private const string Html = """
<!DOCTYPE html>
<html lang="ja">
<head>
<meta charset="utf-8">
<link rel="stylesheet" href="xterm.css">
<style>
  html, body { margin: 0; padding: 0; height: 100%; background: #1E1E1E; overflow: hidden; }
  #term { width: 100%; height: 100%; }
  .xterm .xterm-viewport { background-color: #1E1E1E; }
</style>
</head>
<body>
<div id="term"></div>
<script src="xterm.min.js"></script>
<script src="xterm-addon-fit.min.js"></script>
<script>
const term = new Terminal({
  fontFamily: 'Consolas, "MS Gothic", monospace',
  fontSize: 14,
  cursorBlink: true,
  theme: {
    background: '#1E1E1E', foreground: '#DDDDDD',
    cursor: '#FFFFFF', selectionBackground: '#264F78'
  }
});
const fit = new FitAddon.FitAddon();
term.loadAddon(fit);
term.open(document.getElementById('term'));

function post(obj) { window.chrome.webview.postMessage(JSON.stringify(obj)); }
function doFit() {
  if (document.getElementById('term').clientWidth <= 0) return;
  try { fit.fit(); post({ type: 'resize', cols: term.cols, rows: term.rows }); } catch (e) { }
}
window.addEventListener('resize', doFit);

term.onData(d => post({ type: 'input', data: d }));
// Ctrl+T はシェルへ送らず、ファイラーの一覧へフォーカスを戻す合図にする。
// F1 はターミナル表示の切替(1画面 ⇄ 全画面)。アプリ全体の表示トグルに合わせる。
term.attachCustomKeyEventHandler(ev => {
  if (ev.type !== 'keydown') return true;
  if (ev.ctrlKey && !ev.shiftKey && !ev.altKey && ev.key.toLowerCase() === 't') {
    post({ type: 'focus-list' });
    return false;
  }
  if (ev.key === 'F1') {
    post({ type: 'cycle-view' });
    return false;
  }
  return true;
});

window.chrome.webview.addEventListener('message', ev => {
  if (typeof ev.data === 'string') term.write(ev.data);          // 端末出力
  else if (ev.data && ev.data.type === 'focus') term.focus();    // フォーカス要求
});

doFit();
post({ type: 'ready', cols: term.cols, rows: term.rows });
term.focus();
</script>
</body>
</html>
""";
}
