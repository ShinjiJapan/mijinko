using CommunityToolkit.Mvvm.ComponentModel;

namespace Filer.App.ViewModels;

/// <summary>
/// ペイン上部のタブ1枚分の表示状態。タブ見出し(フォルダー名)を保持する。
/// 実体のナビ状態は <see cref="Filer.Core.PaneTabs"/> 側の PaneState が持つ。
/// </summary>
public sealed partial class TabViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title;

    public TabViewModel(string title) => _title = title;
}
