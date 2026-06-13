namespace Filer.Core;

/// <summary>ファイル名を「名前」と「拡張子」に分割する(UI 非依存)。</summary>
public static class FileNameParts
{
    /// <summary>
    /// 拡張子は「先頭以外の最後のドット」以降と定義する。
    /// 先頭ドットのみのドットファイル(<c>.gitignore</c> 等)は拡張子なし(全体が名前)。
    /// </summary>
    public static (string Base, string Extension) Split(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return (fileName ?? "", "");

        var dot = fileName.LastIndexOf('.');
        if (dot <= 0) return (fileName, "");   // ドット無し、または先頭ドットのみ
        return (fileName[..dot], fileName[(dot + 1)..]);
    }
}
