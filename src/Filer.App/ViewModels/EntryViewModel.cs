using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Filer.Core;

namespace Filer.App.ViewModels;

/// <summary>
/// 一覧の1行。<see cref="FileEntry"/> を表示用に包む。マーク状態のみ可変。
/// </summary>
public sealed partial class EntryViewModel : ObservableObject
{
    public FileEntry Entry { get; }

    [ObservableProperty]
    private bool _isMarked;

    /// <summary>Git の変更状態(バッジ表示用)。ステータス取得完了後に非同期で設定される。</summary>
    [ObservableProperty]
    private GitEntryState _gitState;

    /// <summary>状態が変わったらバッジ関連の派生プロパティへ通知する。</summary>
    partial void OnGitStateChanged(GitEntryState value)
    {
        OnPropertyChanged(nameof(GitBadge));
        OnPropertyChanged(nameof(GitBadgeBrush));
        OnPropertyChanged(nameof(HasGitBadge));
    }

    // バッジ背景色(テーマ非依存の固定色。文字は白)。一度生成して凍結し共有する。
    private static readonly Brush ModifiedBrush = Freeze("#2F7BD6");   // M: 青
    private static readonly Brush AddedBrush = Freeze("#3FA34D");      // A: 緑
    private static readonly Brush IgnoredBrush = Freeze("#8A8886");    // !: 灰
    private static readonly Brush UntrackedBrush = Freeze("#7A7A7A");  // ?: 灰
    private static readonly Brush ConflictedBrush = Freeze("#D13438"); // !: 赤

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>Git 状態を表すバッジ1文字(M/A/?/!)。状態なしは空。</summary>
    public string GitBadge => GitState switch
    {
        GitEntryState.Modified => "M",
        GitEntryState.Added => "A",
        GitEntryState.Ignored => "!",
        GitEntryState.Untracked => "?",
        GitEntryState.Conflicted => "!",
        _ => string.Empty,
    };

    /// <summary>バッジの背景色(状態なしは透明)。</summary>
    public Brush GitBadgeBrush => GitState switch
    {
        GitEntryState.Modified => ModifiedBrush,
        GitEntryState.Added => AddedBrush,
        GitEntryState.Ignored => IgnoredBrush,
        GitEntryState.Untracked => UntrackedBrush,
        GitEntryState.Conflicted => ConflictedBrush,
        _ => Brushes.Transparent,
    };

    /// <summary>バッジを表示するか(状態ありのときのみ)。</summary>
    public bool HasGitBadge => GitState != GitEntryState.None;

    public EntryViewModel(FileEntry entry)
    {
        Entry = entry;
        // ".." とディレクトリは全体を名前とする。ファイルは先頭ドットを拡張子と見なさず分割。
        if (entry.IsParent || entry.IsDirectory)
        {
            DisplayName = entry.Name;
            DisplayExtension = "";
        }
        else
        {
            (DisplayName, DisplayExtension) = FileNameParts.Split(entry.Name);
        }
    }

    public string Name => Entry.Name;
    public bool IsDirectory => Entry.IsDirectory;
    public bool IsParent => Entry.IsParent;

    /// <summary>拡張子を除いた表示名(".."・ディレクトリ・拡張子なしは全体)。</summary>
    public string DisplayName { get; }

    /// <summary>拡張子(ドットなし)。なければ空。</summary>
    public string DisplayExtension { get; }

    /// <summary>Windows のシェルアイコン(エクスプローラーと同じ。種別ごとに表示)。</summary>
    public ImageSource? IconImage => ShellIconProvider.GetIcon(Entry.FullPath, Entry.IsDirectory);

    public string DisplaySize => Entry.IsDirectory ? "<DIR>" : FormatSize(Entry.Size);

    public string DisplayDate => Entry.LastModified == default
        ? string.Empty
        : Entry.LastModified.ToString("yyyy-MM-dd HH:mm");

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:N0} {units[unit]}" : $"{size:N1} {units[unit]}";
    }
}
