using System.Linq;
using System.Windows;
using System.Windows.Media;
using Filer.Core;
using Microsoft.Win32;

namespace Filer.App;

/// <summary>
/// 外観テーマの実行時適用。アプリのマージ辞書にあるテーマ辞書(Themes/*.xaml)を差し替える。
/// 各ウィンドウは DynamicResource でブラシを参照するため、差し替えで即時反映される。
/// System は Windows のライト/ダーク設定へ解決する。
/// </summary>
public static class ThemeManager
{
    /// <summary>現在適用中のテーマ辞書(差し替え対象)。</summary>
    private static ResourceDictionary? _current;

    public static void Apply(AppTheme theme)
    {
        var effective = Resolve(theme);
        var dict = new ResourceDictionary
        {
            Source = new Uri($"Themes/{effective}.xaml", UriKind.Relative),
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        // App.xaml で読み込んだ既定テーマ辞書を初回に引き継いで差し替え対象にする。
        _current ??= merged.FirstOrDefault(
            d => d.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true);
        if (_current is not null)
            merged.Remove(_current);
        merged.Add(dict);
        _current = dict;

        // 開いている全ウィンドウのタイトルバー(DWM)も新しいテーマ色へ合わせる。
        WindowThemeHelper.ApplyToAll();
    }

    /// <summary>
    /// 現在適用中のテーマ辞書のブラシから Markdown プレビュー(HTML/CSS)用の配色を生成する。
    /// 背景輝度でライト/ダークを判定する。
    /// </summary>
    public static ThemeColors CurrentMarkdownColors()
    {
        var bg = ColorOf("App.Background");
        var isDark = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0 < 0.5;
        return new ThemeColors(
            Background: Hex(bg),
            Foreground: Hex(ColorOf("Fg.Primary")),
            Heading: Hex(ColorOf("Fg.Strong")),
            HeadingBorder: Hex(ColorOf("Border.Default")),
            Link: Hex(ColorOf("Accent.Text")),
            CodeBackground: Hex(ColorOf("Panel.Background")),
            PreBackground: Hex(ColorOf("Surface.Background")),
            PreBorder: Hex(ColorOf("Border.Default")),
            Blockquote: Hex(ColorOf("Fg.Muted")),
            BlockquoteBar: Hex(ColorOf("Border.Control")),
            TableBorder: Hex(ColorOf("Border.Default")),
            TableHeaderBackground: Hex(ColorOf("Panel.Background")),
            TableEvenRowBackground: Hex(ColorOf("Row.Hover")),
            ToolbarBackground: Hex(ColorOf("Panel.Background")),
            ToolbarBorder: Hex(ColorOf("Border.Control")),
            ToolbarText: Hex(ColorOf("Fg.Primary")),
            IsDark: isDark);
    }

    /// <summary>リソースキーのブラシ色を取得(見つからなければ黒)。マージ辞書も探索される。</summary>
    private static Color ColorOf(string key) =>
        Application.Current?.Resources[key] is SolidColorBrush b ? b.Color : Colors.Black;

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>System を Windows の設定からライト/ダークへ解決する。それ以外はそのまま。</summary>
    private static AppTheme Resolve(AppTheme theme) =>
        theme == AppTheme.System
            ? (IsWindowsLightMode() ? AppTheme.Light : AppTheme.Dark)
            : theme;

    /// <summary>Windows のアプリ用テーマがライトかどうか(レジストリ AppsUseLightTheme=1)。</summary>
    private static bool IsWindowsLightMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
    }
}
