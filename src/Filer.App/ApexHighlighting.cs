using System.Reflection;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Filer.App;

/// <summary>
/// テキストエディター(AvalonEdit)用の Apex(.cls/.apex)ハイライト定義。埋め込み xshd を読み込み、
/// 名前付き色の foreground を現在のテーマ配色へ差し替えて返す。プレビュー(highlight.js apex)に近い配色に揃える。
/// </summary>
internal static class ApexHighlighting
{
    private const string ResourceName = "Filer.App.Assets.apex.xshd";

    /// <summary>埋め込み xshd を読み込む(都度新規インスタンス。色をテーマ別に書き換えるため共有しない)。</summary>
    private static IHighlightingDefinition Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"埋め込みリソースが見つかりません: {ResourceName}");
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    /// <summary>テーマ配色を反映した Apex ハイライト定義を生成する。</summary>
    public static IHighlightingDefinition ForTheme(bool isDark)
    {
        var def = Load();
        if (isDark)
        {
            SetColor(def, "Comment", "#6A9955");
            SetColor(def, "String", "#CE9178");
            SetColor(def, "Keyword", "#569CD6");
            SetColor(def, "Type", "#4EC9B0");
            SetColor(def, "Number", "#B5CEA8");
            SetColor(def, "Soql", "#C586C0");
            SetColor(def, "Annotation", "#DCDCAA");
        }
        else
        {
            SetColor(def, "Comment", "#3F7E3F");
            SetColor(def, "String", "#A31515");
            SetColor(def, "Keyword", "#0000FF");
            SetColor(def, "Type", "#267F99");
            SetColor(def, "Number", "#098658");
            SetColor(def, "Soql", "#AF00DB");
            SetColor(def, "Annotation", "#7A5C00");
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
