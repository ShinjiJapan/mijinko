using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Filer.App;

/// <summary>
/// ペインのファイル一覧用 ListView。
/// 設定「UIA軽量化」(AppSettings.LightweightListAutomation)が ON のとき、
/// 行を UI Automation の子として公開しない。既定の ListViewAutomationPeer は
/// UIA クライアント(常駐のアクセシビリティ系ツール等)が居るだけで全項目分の
/// 子ピア生成・更新・通知を行い、大量件数フォルダーの移動で UI スレッド時間と
/// DWrite フォントハンドルを消費するため、その回避用。
/// OFF(既定)は従来どおり全行を公開する(スクリーンリーダー・UIA自動化対応)。
/// </summary>
public class PaneListView : ListView
{
    /// <summary>true で行をUIAへ公開しない(実行時切替可。再起動不要)。</summary>
    public static bool LightweightAutomation { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new PaneListViewAutomationPeer(this);

    /// <summary>軽量化 ON のときだけ子(行)の列挙を止めるピア。SelectionPattern 等は維持する。</summary>
    private sealed class PaneListViewAutomationPeer : ListViewAutomationPeer
    {
        public PaneListViewAutomationPeer(ListView owner) : base(owner) { }

        protected override List<AutomationPeer>? GetChildrenCore() =>
            LightweightAutomation ? null : base.GetChildrenCore();
    }
}
