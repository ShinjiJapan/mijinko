using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// 非同期コピー/移動の進捗ダイアログ。進捗バー・転送速度・残り時間・現在ファイルを表示し、
/// キャンセルに対応する。背景処理は <see cref="Window.Loaded"/> で開始し完了で自動的に閉じるため、
/// 表示前完了による ShowDialog 競合を避ける。
/// </summary>
public partial class TransferProgressDialog : Window
{
    private readonly Action<IProgress<FileTransferProgress>, CancellationToken> _work;
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _clock = new();

    /// <summary>処理が例外で終わった場合の例外(キャンセルを除く)。</summary>
    public Exception? Error { get; private set; }

    /// <summary>キャンセルで終わったかどうか。</summary>
    public bool Canceled { get; private set; }

    public TransferProgressDialog(string title, Action<IProgress<FileTransferProgress>, CancellationToken> work)
    {
        InitializeComponent();
        Title = title;
        CurrentText.Text = $"{title}を準備しています…";
        _work = work;
        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var progress = new Progress<FileTransferProgress>(Update);   // UIスレッドのSyncContextを捕捉
        _clock.Start();
        try
        {
            await Task.Run(() => _work(progress, _cts.Token));
        }
        catch (OperationCanceledException)
        {
            Canceled = true;
        }
        catch (Exception ex)
        {
            Error = ex;
        }
        finally
        {
            Close();
        }
    }

    private void Update(FileTransferProgress p)
    {
        var ratio = p.TotalBytes > 0
            ? (double)p.DoneBytes / p.TotalBytes
            : (p.TotalFiles > 0 ? (double)p.DoneFiles / p.TotalFiles : 0);
        Bar.Value = Math.Clamp(ratio * 100, 0, 100);

        CurrentText.Text = string.IsNullOrEmpty(p.CurrentName) ? Title : p.CurrentName;

        // バイト量が無い操作(削除)は件数のみ。速度/残り時間は出さない。
        if (p.TotalBytes <= 0)
        {
            LeftStat.Text = $"{p.DoneFiles}/{p.TotalFiles} 件";
            return;
        }

        LeftStat.Text = $"{p.DoneFiles}/{p.TotalFiles} 件   " +
                        $"{TransferFormat.Size(p.DoneBytes)} / {TransferFormat.Size(p.TotalBytes)}";

        var elapsed = _clock.Elapsed.TotalSeconds;
        if (elapsed > 0.5 && p.DoneBytes > 0)
        {
            var speed = p.DoneBytes / elapsed;
            RightStat.Text = TransferFormat.Rate(speed);
            var remaining = p.TotalBytes - p.DoneBytes;
            if (speed > 0 && remaining > 0)
                RightStat.Text += "   " + TransferFormat.Eta(TimeSpan.FromSeconds(remaining / speed));
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => RequestCancel();

    /// <summary>Esc はウィンドウを閉じずに転送をキャンセルする(背景処理を孤立させない)。</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RequestCancel();
            e.Handled = true;
        }
    }

    /// <summary>X ボタン等での閉じる操作でも、処理中ならキャンセルを要求する。</summary>
    private void OnClosing(object? sender, CancelEventArgs e) => _cts.Cancel();

    private void RequestCancel()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        CancelButton.IsEnabled = false;
        CancelButton.Content = "キャンセル中…";
    }
}
