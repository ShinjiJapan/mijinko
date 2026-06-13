using System;
using System.Collections.Generic;
using System.Linq;

namespace Filer.App.ExternalTools;

/// <summary>インストール済みアプリ1件(表示名+AUMID)。</summary>
public sealed record InstalledApp(string Name, string Aumid);

/// <summary>
/// インストール済みアプリ(ストアアプリ+スタートメニュー登録アプリ)を列挙する。
/// シェルの仮想フォルダー <c>shell:AppsFolder</c> を <c>Shell.Application</c>(COM)で読み、
/// 各項目の表示名と解析名(=AUMID)を取り出す。<c>Get-StartApps</c> と同等の一覧。
/// </summary>
public static class InstalledAppLister
{
    /// <summary>全インストール済みアプリを名前順で返す。失敗時は空。</summary>
    public static IReadOnlyList<InstalledApp> List()
    {
        var result = new List<InstalledApp>();
        object? shell = null, folder = null, items = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return result;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return result;

            dynamic shellDyn = shell;
            folder = shellDyn.NameSpace("shell:AppsFolder");
            if (folder is null) return result;

            items = ((dynamic)folder).Items();
            foreach (dynamic item in (dynamic)items)
            {
                string name = item.Name;
                string aumid = item.Path;   // AppsFolder の解析名 = AUMID
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(aumid))
                    result.Add(new InstalledApp(name, aumid));
                Release(item);
            }
        }
        catch
        {
            // COM/シェルが使えない環境では空一覧(呼び出し側で手入力にフォールバック)。
        }
        finally
        {
            Release(items);
            Release(folder);
            Release(shell);
        }

        return result
            .GroupBy(a => a.Aumid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && System.Runtime.InteropServices.Marshal.IsComObject(comObject))
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(comObject);
    }
}
