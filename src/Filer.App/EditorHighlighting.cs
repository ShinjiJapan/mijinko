using System.IO;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Filer.App;

/// <summary>
/// テキストエディター(AvalonEdit)の拡張子別シンタックスハイライトを解決する。
/// Markdown / Apex はテーマ追従の自前定義、その他は AvalonEdit 組み込み定義を使う
/// (組み込みに無い拡張子は近い言語へ寄せ、対応が無ければハイライト無し)。
/// </summary>
internal static class EditorHighlighting
{
    /// <summary>パスの拡張子に対応するハイライト定義を返す(無ければ null=ハイライトなし)。</summary>
    public static IHighlightingDefinition? ForPath(string path, bool isDark)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".md":
            case ".markdown":
                return MarkdownHighlighting.ForTheme(isDark);
            case ".cls":
            case ".apex":
                return ApexHighlighting.ForTheme(isDark);
        }

        // 組み込み定義が直接対応しない拡張子は、表記の近い言語へ寄せる。
        var lookup = ext switch
        {
            ".ts" or ".tsx" or ".jsx" or ".json" => ".js",
            ".kt" => ".java",
            ".go" or ".rs" => ".cpp",
            ".xaml" or ".csproj" => ".xml",
            _ => ext,
        };
        return HighlightingManager.Instance.GetDefinitionByExtension(lookup);
    }
}
