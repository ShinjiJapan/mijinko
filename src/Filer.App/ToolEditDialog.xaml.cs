using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Filer.Core;
using Microsoft.Win32;

namespace Filer.App;

/// <summary>
/// 外部ツール1件の追加・編集ダイアログ。ラベル・種類(実行ファイル/ストアアプリ)・
/// 対象(パス/AUMID)・引数テンプレート(マクロ)・キー割り当てを1画面で設定する。
/// OK 時に <see cref="Result"/> に編集結果が入る(Id は呼び出し側が決める)。
/// </summary>
public partial class ToolEditDialog : Window
{
    /// <summary>編集結果(ラベル・種類・対象・引数・キー)。Id は含まない。</summary>
    public sealed record ToolDraft(
        string Label, ExternalToolKind Kind, string Target, string Arguments, IReadOnlyList<string> Gestures);

    /// <summary>キー重複時に所有者の表示名を返す(自分が持っている/未割り当てなら null)。</summary>
    private readonly System.Func<string, string?>? _conflictLookup;

    private List<string> _gestures = new();
    private bool _capturing;

    public ToolDraft? Result { get; private set; }

    /// <param name="tool">編集対象(新規なら null)。</param>
    /// <param name="conflictLookup">ジェスチャ→競合する操作の表示名(なければ null)。</param>
    public ToolEditDialog(ExternalTool? tool, System.Func<string, string?>? conflictLookup = null)
    {
        InitializeComponent();
        _conflictLookup = conflictLookup;

        if (tool is not null)
        {
            Title = "外部ツールの編集";
            LabelBox.Text = tool.Label;
            KindStore.IsChecked = tool.Kind == ExternalToolKind.StoreApp;
            KindExe.IsChecked = tool.Kind != ExternalToolKind.StoreApp;
            TargetBox.Text = tool.Target;
            ArgsBox.Text = tool.Arguments;
            _gestures = tool.Gestures.ToList();
        }
        else
        {
            Title = "外部ツールの追加";
            ArgsBox.Text = "$MF";   // 既定: マーク or カーソルのフルパス
        }

        ApplyKind();
        UpdateKeyText();
        Loaded += (_, _) => LabelBox.Focus();
    }

    private bool IsStore => KindStore.IsChecked == true;

    private void Kind_Changed(object sender, RoutedEventArgs e) => ApplyKind();

    /// <summary>種類に応じて対象欄のラベル・ボタン・引数欄の有効/無効を切り替える。</summary>
    private void ApplyKind()
    {
        if (!IsLoadedControls()) return;
        if (IsStore)
        {
            TargetLabel.Text = "AUMID(アプリの識別子)";
            TargetHint.Text = "「アプリを選択...」でインストール済みアプリから選べます。AUMID を直接入力もできます。";
            BrowseButton.Visibility = Visibility.Collapsed;
            PickAppButton.Visibility = Visibility.Visible;
            ArgsHint.Text = "ストアアプリは引数の展開結果から最初の1パスを開きます。例: $MF / $P\\$F(カーソル項目)。";
        }
        else
        {
            TargetLabel.Text = "実行ファイルのパス";
            TargetHint.Text = "フルパス、または PATH 上の名前(例: Code.exe)。Code.exe / wt.exe / git-bash.exe は既知の場所も自動探索します。";
            BrowseButton.Visibility = Visibility.Visible;
            PickAppButton.Visibility = Visibility.Collapsed;
            ArgsHint.Text = "例: -d \"$C\"(カーソルのフォルダー)/ $MF(マーク or カーソルのフルパス)。「マクロ一覧 ?」で全マクロを表示。";
        }
    }

    private bool IsLoadedControls() => TargetLabel is not null && BrowseButton is not null;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "実行ファイルを選択",
            Filter = "実行ファイル (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|すべてのファイル (*.*)|*.*",
        };
        if (File.Exists(TargetBox.Text))
            dialog.InitialDirectory = Path.GetDirectoryName(TargetBox.Text);
        if (dialog.ShowDialog(this) == true)
        {
            TargetBox.Text = dialog.FileName;
            if (LabelBox.Text.Trim().Length == 0)
                LabelBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void PickApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AppPickerDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedApp is { } app)
        {
            TargetBox.Text = app.Aumid;
            if (LabelBox.Text.Trim().Length == 0)
                LabelBox.Text = app.Name;
        }
    }

    private void MacroHelp_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, MacroHelpText, "引数マクロ一覧", MessageBoxButton.OK, MessageBoxImage.Information);

    private const string MacroHelpText =
        "【ファイル名】\n" +
        "  $F  カーソル位置のファイル名\n" +
        "  $W  カーソル位置の拡張子を除いたファイル名\n" +
        "  $E  カーソル位置のファイル名の拡張子\n\n" +
        "【パス名】(末尾に \\ は付きません)\n" +
        "  $P  自ファイル窓のパス名\n" +
        "  $C  カーソル位置のフォルダー(フォルダー以外なら自ファイル窓のパス名)\n" +
        "  $O  他ファイル窓のパス名\n" +
        "  $L  左ファイル窓のパス名\n" +
        "  $R  右ファイル窓のパス名\n\n" +
        "【マークしたファイル】(各項目は \"\" で囲まれ空白区切り)\n" +
        "  $MS  マークされたファイル名(マークが無ければカーソル1つ)\n" +
        "  $MF  マークされたファイルのフルパス(  〃  )\n" +
        "  $MO  他方のマークされたファイル(フルパス)。無ければコマンド自体をキャンセル\n" +
        "  $mO  同上。無ければ空文字に置換(コマンドは継続)\n\n" +
        "  $$  … リテラルの $";

    // ---- キーのキャプチャ ----

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        CaptureBar.Visibility = Visibility.Visible;
    }

    private void ClearKey_Click(object sender, RoutedEventArgs e)
    {
        _gestures.Clear();
        UpdateKeyText();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturing)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        if (KeyChordWpf.IsModifier(key)) return;
        if (key == Key.Escape) { EndCapture(); return; }

        var modifiers = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
        if (KeyChordWpf.FromKeyEvent(key, modifiers) is not { } gesture) return;

        // 他の操作と重複していたら確認する。
        if (_conflictLookup?.Invoke(gesture) is { } ownerLabel)
        {
            var answer = MessageBox.Show(this,
                $"「{DisplayGesture(gesture)}」は「{ownerLabel}」に割り当てられています。\nこのツールへ割り当て直しますか?",
                "キーの重複", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) { EndCapture(); return; }
        }

        _gestures = new List<string> { gesture };
        EndCapture();
        UpdateKeyText();
    }

    private void EndCapture()
    {
        _capturing = false;
        CaptureBar.Visibility = Visibility.Collapsed;
    }

    private void UpdateKeyText() =>
        KeyText.Text = _gestures.Count == 0
            ? "(未割り当て)"
            : string.Join(", ", _gestures.Select(DisplayGesture));

    private static string DisplayGesture(string gesture) =>
        KeyChord.TryParse(gesture, out var chord) ? chord.DisplayText : gesture;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var label = LabelBox.Text.Trim();
        if (label.Length == 0)
        {
            MessageBox.Show(this, "ラベルを入力してください。", "外部ツール",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var target = TargetBox.Text.Trim();
        if (target.Length == 0)
        {
            MessageBox.Show(this, IsStore ? "AUMID を入力してください。" : "実行ファイルのパスを入力してください。",
                "外部ツール", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new ToolDraft(
            label,
            IsStore ? ExternalToolKind.StoreApp : ExternalToolKind.Executable,
            target,
            ArgsBox.Text,
            _gestures);
        DialogResult = true;
    }
}
