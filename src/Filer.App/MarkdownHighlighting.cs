using System.Reflection;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Filer.App;

/// <summary>
/// メモ欄(AvalonEdit)用の Markdown ハイライト定義。埋め込み xshd を読み込み、
/// 名前付き色の foreground を現在のテーマ配色へ差し替えて返す。
/// xshd の色は静的なため、テーマ追従はここで実行時に上書きして実現する。
/// </summary>
internal static class MarkdownHighlighting
{
    private const string ResourceName = "Filer.App.Assets.markdown.xshd";

    /// <summary>埋め込み xshd を読み込む(都度新規インスタンス。色をテーマ別に書き換えるため共有しない)。</summary>
    private static IHighlightingDefinition Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {ResourceName}");
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    /// <summary>テーマ配色を反映した Markdown ハイライト定義を生成する。</summary>
    public static IHighlightingDefinition ForTheme(bool isDark)
    {
        var def = Load();
        if (isDark)
        {
            SetColor(def, "Heading", "#569CD6");
            SetColor(def, "Bold", "#DCDCAA");
            SetColor(def, "Italic", "#C8C8C8");
            SetColor(def, "Code", "#CE9178");
            SetColor(def, "Link", "#4EC9B0");
            SetColor(def, "ListMarker", "#D7BA7D");
            SetColor(def, "Blockquote", "#6A9955");
            SetColor(def, "Rule", "#808080");
        }
        else
        {
            SetColor(def, "Heading", "#0B5394");
            SetColor(def, "Bold", "#7A5C00");
            SetColor(def, "Italic", "#444444");
            SetColor(def, "Code", "#A31515");
            SetColor(def, "Link", "#0969DA");
            SetColor(def, "ListMarker", "#B45309");
            SetColor(def, "Blockquote", "#3F7E3F");
            SetColor(def, "Rule", "#999999");
        }
        return def;
    }

    private static void SetColor(IHighlightingDefinition def, string name, string hex)
    {
        var color = def.GetNamedColor(name);
        if (color is not null)
            color.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(hex));
    }
}
