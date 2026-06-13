namespace Filer.Core;

/// <summary>アプリの外観テーマ。System は実行時に Windows の設定(ライト/ダーク)へ解決する。</summary>
public enum AppTheme
{
    /// <summary>暗い配色(既定)。</summary>
    Dark,
    /// <summary>明るい配色。</summary>
    Light,
    /// <summary>暖色ベージュのアースカラー(明るい)。</summary>
    Beige,
    /// <summary>セージ/フォレストのアースグリーン(明るい)。</summary>
    Green,
    /// <summary>Nord 配色(寒色のダーク)。</summary>
    Nord,
    /// <summary>Solarized Light(やわらかな暖色ライト)。</summary>
    Solarized,
    /// <summary>Dracula 配色(紫系のダーク)。</summary>
    Dracula,
    /// <summary>Windows の外観設定(ライト/ダーク)に合わせる。</summary>
    System,
}
