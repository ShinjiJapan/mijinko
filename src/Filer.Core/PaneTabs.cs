namespace Filer.Core;

/// <summary>
/// 1ペイン内の複数タブを管理する。各タブは独立した <see cref="PaneState"/>(1フォルダのナビ状態)。
/// 追加・削除・切り替えを担い、常に最低1枚のタブを保持する。UI 非依存でテスト可能。
/// </summary>
public sealed class PaneTabs
{
    private readonly IDirectoryReader _reader;
    private readonly List<PaneState> _tabs = new();

    public PaneTabs(IDirectoryReader reader, string initialPath)
        : this(reader, new[] { initialPath }, 0)
    {
    }

    /// <summary>
    /// 複数パスからタブを復元する(セッション復元用)。各パスにつき1タブを開き、
    /// <paramref name="activeIndex"/> をアクティブにする(範囲はクランプ)。paths が空なら例外。
    /// </summary>
    public PaneTabs(IDirectoryReader reader, IReadOnlyList<string> paths, int activeIndex)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        if (paths is null || paths.Count == 0)
            throw new ArgumentException("タブのパスが空です。", nameof(paths));
        foreach (var path in paths)
            _tabs.Add(new PaneState(reader, path));
        ActiveIndex = Math.Clamp(activeIndex, 0, _tabs.Count - 1);
    }

    /// <summary>開いているタブ一覧。</summary>
    public IReadOnlyList<PaneState> Tabs => _tabs;

    /// <summary>全タブの現在パス(セッション保存用)。</summary>
    public IReadOnlyList<string> TabPaths => _tabs.Select(t => t.CurrentPath).ToList();

    /// <summary>アクティブなタブのインデックス。</summary>
    public int ActiveIndex { get; private set; }

    /// <summary>アクティブなタブ。</summary>
    public PaneState Active => _tabs[ActiveIndex];

    /// <summary>
    /// 新しいタブをアクティブタブの現在パスで開き、アクティブタブの直後に挿入してアクティブ化する。
    /// </summary>
    public PaneState AddTab()
    {
        var tab = new PaneState(_reader, Active.CurrentPath);
        ActiveIndex++;
        _tabs.Insert(ActiveIndex, tab);
        return tab;
    }

    /// <summary>アクティブタブを閉じる。</summary>
    public void CloseActive() => CloseTab(ActiveIndex);

    /// <summary>
    /// 指定インデックスのタブを閉じる。最後の1枚は閉じない。
    /// アクティブより前を閉じた場合はアクティブ位置を1つ繰り上げ、同じタブを選択し続ける。
    /// </summary>
    public void CloseTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        if (_tabs.Count == 1) return;   // 最後の1枚は常に残す

        _tabs.RemoveAt(index);
        if (index < ActiveIndex)
            ActiveIndex--;                              // 前を閉じた → 同じタブを追従
        else if (index == ActiveIndex)
            ActiveIndex = Math.Min(ActiveIndex, _tabs.Count - 1);   // アクティブを閉じた → 同位置か末尾へ
    }

    /// <summary>指定インデックスのタブをアクティブにする(範囲外は無視)。</summary>
    public void Activate(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        ActiveIndex = index;
    }

    /// <summary>次のタブへ(末尾の次は先頭へループ)。</summary>
    public void ActivateNext() => ActiveIndex = (ActiveIndex + 1) % _tabs.Count;

    /// <summary>前のタブへ(先頭の前は末尾へループ)。</summary>
    public void ActivatePrev() => ActiveIndex = (ActiveIndex - 1 + _tabs.Count) % _tabs.Count;
}
