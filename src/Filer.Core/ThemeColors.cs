namespace Filer.Core;

/// <summary>
/// Markdown プレビュー(HTML/CSS)用のテーマ配色。WPF 非依存の色文字列(<c>#RRGGBB</c>)で持つ。
/// アプリ側で現在のテーマ辞書のブラシから生成して <see cref="MarkdownRenderer.ToHtmlDocument(string, ThemeColors)"/> に渡す。
/// </summary>
public sealed record ThemeColors(
    string Background,
    string Foreground,
    string Heading,
    string HeadingBorder,
    string Link,
    string CodeBackground,
    string PreBackground,
    string PreBorder,
    string Blockquote,
    string BlockquoteBar,
    string TableBorder,
    string TableHeaderBackground,
    string TableEvenRowBackground,
    string ToolbarBackground,
    string ToolbarBorder,
    string ToolbarText,
    bool IsDark)
{
    /// <summary>既定(ダーク)。テーマ未指定時のフォールバック。</summary>
    public static readonly ThemeColors Dark = new(
        Background: "#1E1E1E",
        Foreground: "#DDDDDD",
        Heading: "#FFFFFF",
        HeadingBorder: "#444444",
        Link: "#4FC1FF",
        CodeBackground: "#2D2D30",
        PreBackground: "#252526",
        PreBorder: "#333333",
        Blockquote: "#AAAAAA",
        BlockquoteBar: "#555555",
        TableBorder: "#444444",
        TableHeaderBackground: "#2D2D30",
        TableEvenRowBackground: "#242424",
        ToolbarBackground: "#2D2D30",
        ToolbarBorder: "#555555",
        ToolbarText: "#DDDDDD",
        IsDark: true);
}
