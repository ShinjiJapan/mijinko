namespace Filer.Core;

/// <summary>
/// 「Ctrl+Shift+T」のようなキージェスチャ文字列の解析・正規化。
/// キー名は WPF の Key enum 名を標準とし、よく使うエイリアス(Return→Enter 等)を吸収する。
/// UI 非依存のため、実際のキー入力との対応付けは App 層が行う。
/// </summary>
public readonly struct KeyChord
{
    public bool Ctrl { get; }
    public bool Shift { get; }
    public bool Alt { get; }

    /// <summary>修飾を除いたキー名(標準形。例: "T", "F5", "D1", "Enter")。</summary>
    public string KeyName { get; }

    private KeyChord(bool ctrl, bool shift, bool alt, string keyName)
    {
        Ctrl = ctrl;
        Shift = shift;
        Alt = alt;
        KeyName = keyName;
    }

    /// <summary>「Ctrl+T」等の文字列を解析する。修飾のみ・キー名2個以上・空は失敗。</summary>
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        bool ctrl = false, shift = false, alt = false;
        string? keyName = null;
        foreach (var raw in text.Split('+'))
        {
            var token = raw.Trim();
            if (token.Length == 0)
                return false;

            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Control", StringComparison.OrdinalIgnoreCase))
                ctrl = true;
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                shift = true;
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                alt = true;
            else if (keyName is null)
                keyName = CanonicalKeyName(token);
            else
                return false;   // キー名が2個以上
        }

        if (keyName is null)
            return false;       // 修飾のみ

        chord = new KeyChord(ctrl, shift, alt, keyName);
        return true;
    }

    /// <summary>正規文字列(Ctrl→Shift→Alt→キー名)。設定ファイルへの保存形。</summary>
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        parts.Add(KeyName);
        return string.Join("+", parts);
    }

    /// <summary>大文字小文字を無視した比較・辞書キー用の正規化文字列。</summary>
    public string Normalized => ToString().ToUpperInvariant();

    /// <summary>フッターや設定画面で見せる表示文字列(D1→1、Left→← 等)。</summary>
    public string DisplayText
    {
        get
        {
            var parts = new List<string>(4);
            if (Ctrl) parts.Add("Ctrl");
            if (Shift) parts.Add("Shift");
            if (Alt) parts.Add("Alt");
            parts.Add(DisplayKeyName(KeyName));
            return string.Join("+", parts);
        }
    }

    /// <summary>キー名をエイリアス込みで標準形へ(enter→Enter, prior→PageUp, t→T 等)。未知の名前はそのまま。</summary>
    private static string CanonicalKeyName(string token)
    {
        if (token.Length == 1 && char.IsAsciiLetter(token[0]))
            return token.ToUpperInvariant();
        if (token.Length == 1 && char.IsAsciiDigit(token[0]))
            return "D" + token;   // 上段数字は "1" でも受ける

        foreach (var name in CanonicalNames)
            if (name.Equals(token, StringComparison.OrdinalIgnoreCase))
                return name;

        return Aliases.TryGetValue(token, out var canonical) ? canonical : token;
    }

    private static readonly string[] CanonicalNames =
    {
        "Enter", "Space", "Tab", "Back", "Delete", "Insert", "Escape",
        "Home", "End", "PageUp", "PageDown", "Up", "Down", "Left", "Right",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
        "NumPad0", "NumPad1", "NumPad2", "NumPad3", "NumPad4",
        "NumPad5", "NumPad6", "NumPad7", "NumPad8", "NumPad9",
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Return"] = "Enter",
        ["Esc"] = "Escape",
        ["BS"] = "Back",
        ["Backspace"] = "Back",
        ["Del"] = "Delete",
        ["Prior"] = "PageUp",
        ["PgUp"] = "PageUp",
        ["Next"] = "PageDown",
        ["PgDn"] = "PageDown",
    };

    private static string DisplayKeyName(string name) => name switch
    {
        "Back" => "BS",
        "Delete" => "Del",
        "Escape" => "Esc",
        "Left" => "←",
        "Right" => "→",
        "Up" => "↑",
        "Down" => "↓",
        "PageUp" => "PgUp",
        "PageDown" => "PgDn",
        _ when name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]) => name[1..],
        _ when name.StartsWith("NumPad", StringComparison.Ordinal) => "Num" + name["NumPad".Length..],
        _ => name,
    };
}
