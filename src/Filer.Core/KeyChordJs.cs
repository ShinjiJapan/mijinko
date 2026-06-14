namespace Filer.Core;

/// <summary>
/// キージェスチャを WebView2 内 JS のイベント判定式へ変換する。
/// プレビュー/ターミナルの HTML に埋め込み、設定した表示切替キーを JS 側でも発火させるために使う。
/// </summary>
public static class KeyChordJs
{
    /// <summary>
    /// ジェスチャ群のいずれかに一致すれば true となる JS 式を返す(<paramref name="eventVar"/> はイベント変数名)。
    /// 解析できるジェスチャが1つも無ければ <c>"false"</c>。
    /// </summary>
    public static string MatchExpression(IEnumerable<string> gestures, string eventVar)
    {
        var terms = new List<string>();
        foreach (var g in gestures)
            if (KeyChord.TryParse(g, out var chord))
                terms.Add(MatchOne(chord, eventVar));
        return terms.Count == 0 ? "false" : string.Join(" || ", terms);
    }

    /// <summary>1つのジェスチャに一致する条件式((修飾の有無)+(キー判定)を && で結合)。</summary>
    private static string MatchOne(KeyChord c, string ev)
    {
        var parts = new List<string>(4)
        {
            (c.Ctrl ? "" : "!") + ev + ".ctrlKey",
            (c.Shift ? "" : "!") + ev + ".shiftKey",
            (c.Alt ? "" : "!") + ev + ".altKey",
            KeyTest(c.KeyName, ev),
        };
        return "(" + string.Join(" && ", parts) + ")";
    }

    /// <summary>キー名を DOM の <c>KeyboardEvent.key</c> 比較式へ変換する。</summary>
    private static string KeyTest(string keyName, string ev)
    {
        if (keyName.Length == 1 && char.IsAsciiLetter(keyName[0]))
            return $"{ev}.key.toLowerCase() === '{char.ToLowerInvariant(keyName[0])}'";
        if (keyName.Length == 2 && keyName[0] == 'D' && char.IsAsciiDigit(keyName[1]))
            return $"{ev}.key === '{keyName[1]}'";
        return $"{ev}.key === '{JsKeyName(keyName)}'";
    }

    /// <summary>WPF キー名 → DOM の <c>KeyboardEvent.key</c> 値(F1-12 等はそのまま一致する)。</summary>
    private static string JsKeyName(string keyName) => keyName switch
    {
        "Space" => " ",
        "Up" => "ArrowUp",
        "Down" => "ArrowDown",
        "Left" => "ArrowLeft",
        "Right" => "ArrowRight",
        "Back" => "Backspace",
        _ => keyName,
    };
}
