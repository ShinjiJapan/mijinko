using Filer.Core;

namespace Filer.Core.Tests;

public sealed class KeyBindingMapTests
{
    [Fact]
    public void BuiltInActions_DoNotContainToolActions()
    {
        // 外部ツールは動的アクション(tool:<id>)。組み込み定義にツール用 Id を残さない
        // (残すと孤児バインドや設定画面の二重行になる)。
        Assert.DoesNotContain(KeyBindingActions.All, a =>
            a.Id.StartsWith("tool:") || a.Id.StartsWith("tool."));
    }

    [Fact]
    public void Actions_HaveUniqueIds_AndParsableDefaultGestures()
    {
        var ids = new HashSet<string>();
        foreach (var action in KeyBindingActions.All)
        {
            Assert.True(ids.Add(action.Id), $"Id 重複: {action.Id}");
            Assert.NotEmpty(action.DefaultGestures);
            foreach (var gesture in action.DefaultGestures)
                Assert.True(KeyChord.TryParse(gesture, out _), $"既定ジェスチャが不正: {action.Id} = {gesture}");
        }
    }

    [Fact]
    public void Actions_DefaultGestures_HaveNoConflicts()
    {
        var seen = new Dictionary<string, string>();
        foreach (var action in KeyBindingActions.All)
        {
            foreach (var gesture in action.DefaultGestures)
            {
                KeyChord.TryParse(gesture, out var chord);
                Assert.False(seen.TryGetValue(chord.Normalized, out var owner),
                    $"既定ジェスチャ衝突: {gesture} が {owner} と {action.Id} の両方にある");
                seen[chord.Normalized] = action.Id;
            }
        }
    }

    [Fact]
    public void Build_NoOverrides_ResolvesDefaults()
    {
        var map = KeyBindingMap.Build(null);
        Assert.Equal("file.copy", map.TryResolve("C"));
        Assert.Equal("tab.new", map.TryResolve("Ctrl+T"));
        Assert.Equal("terminal.open", map.TryResolve("T"));
        Assert.Equal("file.delete", map.TryResolve("Delete"));
        Assert.Equal("file.delete", map.TryResolve("D"));
        Assert.Equal("file.deletePermanent", map.TryResolve("Shift+D"));
    }

    [Fact]
    public void Build_NoOverrides_ResolvesTerminalContextActions()
    {
        // ターミナルフォーカス中の専用キー。空きキー(F4/F6)に割り当て、既存の F5(更新)は不変。
        var map = KeyBindingMap.Build(null);
        Assert.Equal("terminal.collapse", map.TryResolve("F4"));
        Assert.Equal("terminal.focusBack", map.TryResolve("F6"));
        Assert.Equal("view.reload", map.TryResolve("F5"));
    }

    [Fact]
    public void TryResolve_IsCaseInsensitive()
    {
        var map = KeyBindingMap.Build(null);
        Assert.Equal("tab.new", map.TryResolve("ctrl+t"));
    }

    [Fact]
    public void TryResolve_Unknown_ReturnsNull()
    {
        var map = KeyBindingMap.Build(null);
        Assert.Null(map.TryResolve("Ctrl+Alt+F12"));
        Assert.Null(map.TryResolve("not a gesture"));
    }

    [Fact]
    public void Build_Override_ReplacesGestures()
    {
        var map = KeyBindingMap.Build(new Dictionary<string, string[]>
        {
            ["file.copy"] = new[] { "X" },
        });
        Assert.Equal("file.copy", map.TryResolve("X"));
        Assert.Null(map.TryResolve("C"));   // 旧既定は外れる
        Assert.Equal(new[] { "X" }, map.GesturesFor("file.copy"));
    }

    [Fact]
    public void Build_Override_StealsGestureFromDefaultOwner()
    {
        // terminal.open(既定 T)へ C を割り当てたら、file.copy(既定 C)からは C が外れる。
        var map = KeyBindingMap.Build(new Dictionary<string, string[]>
        {
            ["terminal.open"] = new[] { "C" },
        });
        Assert.Equal("terminal.open", map.TryResolve("C"));
        Assert.DoesNotContain("C", map.GesturesFor("file.copy"));
    }

    [Fact]
    public void Build_EmptyOverride_RemovesAllGestures()
    {
        var map = KeyBindingMap.Build(new Dictionary<string, string[]>
        {
            ["terminal.open"] = Array.Empty<string>(),
        });
        Assert.Null(map.TryResolve("T"));
        Assert.Empty(map.GesturesFor("terminal.open"));
    }

    [Fact]
    public void Build_UnknownActionOrInvalidGesture_IsIgnored()
    {
        var map = KeyBindingMap.Build(new Dictionary<string, string[]>
        {
            ["no.such.action"] = new[] { "B" },
            ["file.copy"] = new[] { "not+a+key+chord" },   // 不正ジェスチャは無視 → 既定維持
        });
        Assert.Null(map.TryResolve("B"));
        Assert.Equal("file.copy", map.TryResolve("C"));
    }

    [Fact]
    public void ToOverrides_ReturnsOnlyChangedActions()
    {
        var map = KeyBindingMap.Build(new Dictionary<string, string[]>
        {
            ["file.copy"] = new[] { "X" },
            ["file.move"] = new[] { "M" },   // 既定と同じ → 差分に含めない
        });
        var overrides = map.ToOverrides();
        Assert.True(overrides.ContainsKey("file.copy"));
        Assert.False(overrides.ContainsKey("file.move"));
    }

    [Fact]
    public void ToOverrides_IncludesStolenDefaults()
    {
        // 既定ジェスチャを奪われたアクションも差分として残す(再構築時に既定へ戻らないように)。
        var map = KeyBindingMap.Build(new Dictionary<string, string[]>
        {
            ["terminal.open"] = new[] { "C" },
        });
        var overrides = map.ToOverrides();
        var rebuilt = KeyBindingMap.Build(overrides);
        Assert.Equal("terminal.open", rebuilt.TryResolve("C"));
        Assert.DoesNotContain("C", rebuilt.GesturesFor("file.copy"));
    }

    [Fact]
    public void Replace_AssignsGesture_AndRemovesFromPreviousOwner()
    {
        var map = KeyBindingMap.Build(null);
        map.Replace("file.copy", new[] { "T" });   // terminal.open から T を奪う
        Assert.Equal("file.copy", map.TryResolve("T"));
        Assert.Empty(map.GesturesFor("terminal.open"));
        Assert.Null(map.TryResolve("C"));
    }

    [Fact]
    public void OwnerOf_ReturnsCurrentOwner()
    {
        var map = KeyBindingMap.Build(null);
        Assert.Equal("file.copy", map.OwnerOf("C"));
        Assert.Null(map.OwnerOf("Ctrl+Alt+F12"));
    }

    [Fact]
    public void ResetToDefault_RestoresDefaultGestures()
    {
        var map = KeyBindingMap.Build(new Dictionary<string, string[]>
        {
            ["file.copy"] = new[] { "X" },
        });
        map.ResetToDefault("file.copy");
        Assert.Equal("file.copy", map.TryResolve("C"));
        Assert.Null(map.TryResolve("X"));
    }

    [Fact]
    public void ResetToDefault_StealsBackFromCurrentOwner()
    {
        var map = KeyBindingMap.Build(null);
        map.Replace("terminal.open", new[] { "C" });   // file.copy の C を奪う
        map.ResetToDefault("file.copy");                // 既定 C へ戻す → terminal.open から外れる
        Assert.Equal("file.copy", map.TryResolve("C"));
        Assert.Empty(map.GesturesFor("terminal.open"));
    }

    [Fact]
    public void KeyHelp_Normal_ListsUnmodifiedGesturesInDefinitionOrder()
    {
        var map = KeyBindingMap.Build(null);
        var help = KeyHelp.BuildNormal(map);
        Assert.StartsWith("Tab:切替", help);
        Assert.Contains("C:コピー", help);
        Assert.Contains("Z:設定", help);
        Assert.DoesNotContain("Ctrl", help);
    }

    [Fact]
    public void KeyHelp_ReflectsOverrides()
    {
        var map = KeyBindingMap.Build(new Dictionary<string, string[]>
        {
            ["file.copy"] = new[] { "X" },
        });
        var help = KeyHelp.BuildNormal(map);
        Assert.Contains("X:コピー", help);
        Assert.DoesNotContain("C:コピー", help);
    }

    [Fact]
    public void KeyHelp_CtrlAndShift_ListModifiedGestures()
    {
        var map = KeyBindingMap.Build(null, ToolActions());
        var ctrl = KeyHelp.BuildCtrl(map);
        Assert.Contains("Ctrl+T:新タブ", ctrl);
        Assert.Contains("Ctrl+←:前タブ", ctrl);

        var shift = KeyHelp.BuildShift(map);
        Assert.Contains("Shift+Enter:関連付けで開く", shift);
        Assert.Contains("Shift+K:SkimDown", shift);   // SkimDown は既定ツール(動的アクション)
    }

    [Fact]
    public void KeyHelp_Context_ResolvesGesturesFromMapAndFixedKeys()
    {
        var map = KeyBindingMap.Build(null);
        var help = KeyHelp.BuildContext(map, new[]
        {
            new KeyHelp.ContextHelpEntry(null, "Escape", "閉じる"),               // 固定キー
            new KeyHelp.ContextHelpEntry("view.toggleFullscreen", null, "全画面切替"), // 設定から動的
        });
        Assert.Equal("Esc:閉じる  F1:全画面切替", help);
    }

    [Fact]
    public void KeyHelp_Context_FollowsOverrides()
    {
        var map = KeyBindingMap.Build(new Dictionary<string, string[]>
        {
            ["terminal.collapse"] = new[] { "F8" },
        });
        var help = KeyHelp.BuildContext(map, new[]
        {
            new KeyHelp.ContextHelpEntry("terminal.collapse", null, "たたむ"),
        });
        Assert.Equal("F8:たたむ", help);
    }

    [Fact]
    public void KeyHelp_Context_SkipsUnboundActions()
    {
        var map = KeyBindingMap.Build(new Dictionary<string, string[]>
        {
            ["terminal.collapse"] = Array.Empty<string>(),   // 割り当てなし
        });
        var help = KeyHelp.BuildContext(map, new[]
        {
            new KeyHelp.ContextHelpEntry(null, "Ctrl+T", "一覧へ"),
            new KeyHelp.ContextHelpEntry("terminal.collapse", null, "たたむ"),
        });
        Assert.Equal("Ctrl+T:一覧へ", help);
    }

    // ---- 外部ツール(動的アクション) ----

    private static IEnumerable<KeyBindingAction> ToolActions() =>
        ExternalTools.Defaults().Select(KeyBindingActions.ForTool);

    [Fact]
    public void Build_WithTools_ResolvesToolGestures()
    {
        var map = KeyBindingMap.Build(null, ToolActions());
        Assert.Equal("tool:vscode", map.TryResolve("V"));
        Assert.Equal("tool:git-bash", map.TryResolve("B"));
        Assert.Equal("tool:skimdown", map.TryResolve("Shift+K"));
        // 組み込みは引き続き解決できる。
        Assert.Equal("file.copy", map.TryResolve("C"));
    }

    [Fact]
    public void Build_ToolGesture_StealsFromBuiltinDefault()
    {
        var tool = new ExternalTool("custom", "カスタム", ExternalToolKind.Executable, "x.exe", "$MF", new[] { "C" });
        var map = KeyBindingMap.Build(null, new[] { KeyBindingActions.ForTool(tool) });
        Assert.Equal("tool:custom", map.TryResolve("C"));     // ツールが C を奪う
        Assert.DoesNotContain("C", map.GesturesFor("file.copy"));
    }

    [Fact]
    public void ToOverrides_ExcludesToolActions()
    {
        var map = KeyBindingMap.Build(null, ToolActions());
        // ツールのジェスチャは keyBindings 差分には出さない(ツール定義側に持つため)。
        Assert.DoesNotContain(map.ToOverrides().Keys, k => k.StartsWith("tool:"));
    }

    [Fact]
    public void Actions_IncludeBuiltInAndTools()
    {
        var map = KeyBindingMap.Build(null, ToolActions());
        Assert.Contains(map.Actions, a => a.Id == "file.copy");
        Assert.Contains(map.Actions, a => a.Id == "tool:vscode");
    }

    [Fact]
    public void Replace_ToolAction_Works()
    {
        var map = KeyBindingMap.Build(null, ToolActions());
        map.Replace("tool:vscode", new[] { "Ctrl+E" });
        Assert.Equal("tool:vscode", map.TryResolve("Ctrl+E"));
        Assert.Null(map.TryResolve("V"));
    }
}
