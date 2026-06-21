using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// マークした複数項目を連番/置換/正規表現で一括リネームするダイアログ。
/// 入力に応じて結果一覧をライブプレビューし、状態が OK の項目だけを実行する。
/// </summary>
public partial class BulkRenameDialog : Window
{
    /// <summary>プレビュー1行の表示モデル。</summary>
    public sealed record Row(string OriginalName, string NewName, BulkRenameStatus Status)
    {
        public string StatusText => Status switch
        {
            BulkRenameStatus.Ok => "OK",
            BulkRenameStatus.Unchanged => "変更なし",
            BulkRenameStatus.Duplicate => "重複",
            BulkRenameStatus.Invalid => "不正",
            _ => "",
        };
        public bool IsProblem => Status is BulkRenameStatus.Duplicate or BulkRenameStatus.Invalid;
        public bool IsUnchanged => Status == BulkRenameStatus.Unchanged;
    }

    private readonly IReadOnlyList<FileEntry> _targets;
    private readonly IReadOnlyCollection<string> _existingNames;
    private bool _ready;

    /// <summary>実行する (フルパス, 新しい名前) の一覧(OK 状態のみ)。</summary>
    public IReadOnlyList<(string FullPath, string NewName)> Renames { get; private set; } =
        new List<(string, string)>();

    /// <param name="targets">リネーム対象(フルパス・現在名・サイズ・更新日時、表示順)。</param>
    /// <param name="existingNames">同フォルダー内の対象外ファイル名(衝突判定用)。</param>
    public BulkRenameDialog(
        IReadOnlyList<FileEntry> targets,
        IReadOnlyCollection<string> existingNames)
    {
        InitializeComponent();
        _targets = targets;
        _existingNames = existingNames;
        // 番号順の選択肢。並び順は SequenceOrder の宣言順と一致させる(SelectedIndex で対応付け)。
        OrderBox.ItemsSource = new[]
        {
            "表示順(現在のまま)",
            "名前 昇順",
            "名前 降順",
            "更新日時 古い順",
            "更新日時 新しい順",
            "サイズ 小さい順",
            "サイズ 大きい順",
        };
        OrderBox.SelectedIndex = 0;
        ModeReplace.IsChecked = true;
        // 連番の日付トークン例(先頭対象の更新日時で具体例を示す)。
        var sample = _targets.Count > 0 ? _targets[0].LastModified : DateTime.Now;
        DateExampleText.Text = $"例: $(yyyyMMddHHmmss) → {sample:yyyyMMddHHmmss}";
        _ready = true;
        FindBox.Focus();
        Refresh();
    }

    private BulkRenameMode SelectedMode =>
        ModeRegex.IsChecked == true ? BulkRenameMode.Regex :
        ModeSequence.IsChecked == true ? BulkRenameMode.Sequence :
        BulkRenameMode.Replace;

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var sequence = SelectedMode == BulkRenameMode.Sequence;
        ReplacePanel.Visibility = sequence ? Visibility.Collapsed : Visibility.Visible;
        SequencePanel.Visibility = sequence ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    private void Param_Changed(object sender, RoutedEventArgs e) => Refresh();

    private SequenceOrder SelectedOrder =>
        OrderBox.SelectedIndex >= 0 ? (SequenceOrder)OrderBox.SelectedIndex : SequenceOrder.Current;

    private BulkRenameOptions BuildOptions()
    {
        int.TryParse(StartBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var start);
        if (!int.TryParse(StepBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var step))
            step = 1;
        return new BulkRenameOptions
        {
            Mode = SelectedMode,
            Find = FindBox.Text,
            Replace = ReplaceBox.Text,
            CaseSensitive = CaseSensitiveBox.IsChecked == true,
            IncludeExtension = IncludeExtBox.IsChecked == true,
            Template = TemplateBox.Text,
            Start = start,
            Step = step,
            Order = SelectedOrder,
        };
    }

    private BulkRenameItem[] BuildItems() =>
        _targets.Select(t => new BulkRenameItem(t.Name, t.Size, t.LastModified)).ToArray();

    private void Refresh()
    {
        if (!_ready) return;

        var plan = BulkRenamer.Plan(BuildItems(), BuildOptions(), _existingNames);
        PreviewList.ItemsSource = plan.Select(p => new Row(p.OriginalName, p.NewName, p.Status)).ToArray();

        var ok = plan.Count(p => p.Status == BulkRenameStatus.Ok);
        var dup = plan.Count(p => p.Status == BulkRenameStatus.Duplicate);
        var invalid = plan.Count(p => p.Status == BulkRenameStatus.Invalid);
        var unchanged = plan.Count(p => p.Status == BulkRenameStatus.Unchanged);

        SummaryText.Text =
            $"{plan.Count} 件中 {ok} 件をリネーム" +
            (unchanged > 0 ? $" / 変更なし {unchanged}" : "") +
            (dup > 0 ? $" / 重複 {dup}" : "") +
            (invalid > 0 ? $" / 不正 {invalid}" : "");

        // 問題(重複・不正)が1件でもあれば実行不可。OK が1件以上必要。
        OkButton.IsEnabled = ok > 0 && dup == 0 && invalid == 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var plan = BulkRenamer.Plan(BuildItems(), BuildOptions(), _existingNames);
        var renames = new List<(string FullPath, string NewName)>();
        for (int i = 0; i < plan.Count; i++)
            if (plan[i].Status == BulkRenameStatus.Ok)
                renames.Add((_targets[i].FullPath, plan[i].NewName));

        if (renames.Count == 0) return;
        Renames = renames;
        DialogResult = true;
        Close();
    }
}
