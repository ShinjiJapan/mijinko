using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Filer.App.ExternalTools;
using Filer.Core;

namespace Filer.App.ViewModels;

/// <summary>
/// 2ペイン全体の状態。アクティブ/非アクティブの切替と、ペイン間ファイル操作を司る。
/// 確認ダイアログ等のユーザー対話は View 側で行い、ここは操作の実行のみを担う。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly FileOperations _ops = new();
    private readonly FavoritesStore _favorites;
    private readonly HistoryStore _history;
    private readonly IDriveProvider _drives = new DriveLister();
    private readonly ExternalToolLauncher _tools = new();
    private readonly AppSettingsStore _settingsStore;
    private readonly SearchResultsReader _searchResults;

    public PaneViewModel Left { get; }
    public PaneViewModel Right { get; }

    /// <summary>現在のアプリ設定(キー割り当ての上書き+外部ツールパス)。</summary>
    public AppSettings Settings { get; private set; }

    [ObservableProperty]
    private bool _isLeftActive = true;

    public MainViewModel(SessionPane left, SessionPane right, bool isLeftActive,
        string favoritesFilePath, string historyFilePath, string settingsFilePath)
    {
        _favorites = new FavoritesStore(favoritesFilePath);
        _history = new HistoryStore(historyFilePath);
        _settingsStore = new AppSettingsStore(settingsFilePath);
        Settings = _settingsStore.Load();
        var reader = new SearchResultsReader(new ArchiveAwareReader(new DirectoryLister()));
        _searchResults = reader;
        Left = new PaneViewModel(reader, left.TabPaths, left.ActiveTabIndex, left.ViewMode, left.GridSize);
        Right = new PaneViewModel(reader, right.TabPaths, right.ActiveTabIndex, right.ViewMode, right.GridSize);

        IsLeftActive = isLeftActive;
        Left.IsActive = isLeftActive;
        Right.IsActive = !isLeftActive;

        Left.PropertyChanged += OnPanePropertyChanged;
        Right.PropertyChanged += OnPanePropertyChanged;

        // 起動時の各ペイン初期フォルダも履歴へ(アクティブ側を後に記録して先頭に置く)。
        _history.Add(Inactive.DirectoryPath);
        _history.Add(Active.DirectoryPath);
    }

    public PaneViewModel Active => IsLeftActive ? Left : Right;
    public PaneViewModel Inactive => IsLeftActive ? Right : Left;

    /// <summary>現在の2ペイン状態を取得する(終了時のセッション保存用)。ウィンドウ位置は呼び出し側(UI)が渡す。</summary>
    public SessionState CaptureSession(WindowBounds? window = null) =>
        new(new SessionPane(Left.TabPaths, Left.ActiveTabIndex, Left.ViewMode, Left.GridSize),
            new SessionPane(Right.TabPaths, Right.ActiveTabIndex, Right.ViewMode, Right.GridSize),
            IsLeftActive,
            window);

    /// <summary>開いたフォルダーの履歴(新しい順)。</summary>
    public IReadOnlyList<string> GetHistory() => _history.GetAll();

    /// <summary>設定を保存し、外部ツール起動へ即時反映する。</summary>
    public void UpdateSettings(AppSettings settings)
    {
        _settingsStore.Save(settings);
        Settings = _settingsStore.Load();   // 保存時の差分化(既定と同じ上書きの除去)込みで読み直す
    }

    /// <summary>ウィンドウタイトル兼ステータス。アクティブ側のパスとカーソル位置を表示する。</summary>
    public string StatusText
    {
        get
        {
            var p = Active;
            // 件数は表示用コレクション(2段階表示で一時的に部分集合)ではなくモデルの全件数を使う。
            var pos = p.EntryCount == 0 ? 0 : p.SelectedIndex + 1;
            return $"みじんこFiler — {p.CurrentPath}  [{pos}/{p.EntryCount}]";
        }
    }

    public void SwitchPane()
    {
        IsLeftActive = !IsLeftActive;
        Left.IsActive = IsLeftActive;
        Right.IsActive = !IsLeftActive;
    }

    partial void OnIsLeftActiveChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var pane = (PaneViewModel)sender!;

        // フォルダー移動(タブ切替含む)はどちらのペインでも履歴へ記録する(検索結果の仮想一覧は除く)。
        if (e.PropertyName == nameof(PaneViewModel.CurrentPath) &&
            !SearchResultsReader.IsVirtual(pane.DirectoryPath))
            _history.Add(pane.DirectoryPath);

        if (ReferenceEquals(pane, Active) &&
            e.PropertyName is nameof(PaneViewModel.SelectedIndex) or nameof(PaneViewModel.CurrentPath))
            OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// アクティブ側の対象を非アクティブ側のディレクトリへコピーする。
    /// 書庫(.zip)内の項目は実フォルダーへ抽出する。コピー先が書庫内なら拒否する。
    /// </summary>
    public void CopyToOther()
    {
        EnsureNotInArchive(Inactive.DirectoryPath, "コピー先のフォルダー");
        foreach (var entry in Active.Targets.ToArray())
        {
            if (ArchivePath.TrySplit(entry.FullPath, out _, out _))
                ArchiveExtractor.ExtractTo(entry.FullPath, Inactive.DirectoryPath);
            else
                _ops.Copy(entry.FullPath, Inactive.DirectoryPath);
        }
        Inactive.Reload();
    }

    /// <summary>
    /// アクティブ側の対象を非アクティブ側へ転送する計画を作る(非同期コピー/移動用)。
    /// 対象は UI スレッドで確定し、実行(<see cref="ExecuteTransfer"/>)は背景スレッドで行う。
    /// 移動先が書庫内、または移動対象が書庫内項目なら例外で拒否する。
    /// </summary>
    public FileTransferPlan BuildTransferPlan(FileTransferKind kind)
    {
        var label = kind == FileTransferKind.Copy ? "コピー先のフォルダー" : "移動先のフォルダー";
        EnsureNotInArchive(Inactive.DirectoryPath, label);

        var sources = new List<string>();
        foreach (var entry in Active.Targets.ToArray())
        {
            if (kind == FileTransferKind.Move)
                EnsureNotInsideArchive(entry.FullPath, "書庫内の項目（移動。コピーを使用してください）");
            sources.Add(entry.FullPath);
        }
        return FileTransferService.BuildPlan(sources, Inactive.DirectoryPath, kind);
    }

    /// <summary>転送計画を実行する(背景スレッドから呼ぶ)。進捗通知・キャンセルに対応。</summary>
    public void ExecuteTransfer(
        FileTransferPlan plan, FileTransferKind kind,
        IProgress<FileTransferProgress> progress, CancellationToken token)
        => FileTransferService.Execute(plan, kind, progress, token);

    /// <summary>アクティブ側の対象を非アクティブ側のディレクトリへ移動する。書庫内項目は不可。</summary>
    public void MoveToOther()
    {
        EnsureNotInArchive(Inactive.DirectoryPath, "移動先のフォルダー");
        foreach (var entry in Active.Targets.ToArray())
        {
            EnsureNotInsideArchive(entry.FullPath, "書庫内の項目（移動。コピーを使用してください）");
            _ops.Move(entry.FullPath, Inactive.DirectoryPath);
        }
        Active.Reload();
        Inactive.Reload();
    }

    /// <summary>
    /// アクティブ側の削除対象の計画を作る(非同期削除用)。対象は UI スレッドで確定する。
    /// 書庫内項目は削除不可のため例外で拒否する。
    /// </summary>
    public FileDeletePlan BuildDeletePlan(DeleteKind kind)
    {
        var sources = new List<string>();
        foreach (var entry in Active.Targets.ToArray())
        {
            EnsureNotInsideArchive(entry.FullPath, "書庫内の項目（削除）");
            sources.Add(entry.FullPath);
        }
        return FileDeleteService.BuildPlan(sources, kind);
    }

    /// <summary>削除計画を実行する(背景スレッドから呼ぶ)。進捗通知・キャンセルに対応。</summary>
    public void ExecuteDelete(
        FileDeletePlan plan, DeleteKind kind,
        IProgress<FileTransferProgress> progress, CancellationToken token)
        => FileDeleteService.Execute(plan, kind, progress, token);

    /// <summary>アクティブ側の対象をごみ箱へ送る(復元可能)。書庫内項目は不可。</summary>
    public void DeleteTargets()
    {
        foreach (var entry in Active.Targets.ToArray())
        {
            EnsureNotInsideArchive(entry.FullPath, "書庫内の項目（削除）");
            _ops.DeleteToRecycleBin(entry.FullPath);
        }
        Active.Reload();
    }

    /// <summary>アクティブ側の対象を完全削除する(ごみ箱を経由しない)。書庫内項目は不可。</summary>
    public void DeleteTargetsPermanently()
    {
        foreach (var entry in Active.Targets.ToArray())
        {
            EnsureNotInsideArchive(entry.FullPath, "書庫内の項目（削除）");
            _ops.Delete(entry.FullPath);
        }
        Active.Reload();
    }

    /// <summary>
    /// アクティブ側の対象(マーク or カーソル)を現在フォルダー直下の ZIP へ圧縮する。
    /// 書庫(.zip)内のフォルダー・項目は不可。拡張子が無ければ <c>.zip</c> を補う。
    /// </summary>
    public void CompressTargets(string zipFileName)
    {
        EnsureNotInArchive(Active.DirectoryPath, "圧縮先のフォルダー");
        var name = EnsureZipExtension(zipFileName);

        var sources = new List<string>();
        foreach (var entry in Active.Targets.ToArray())
        {
            EnsureNotInsideArchive(entry.FullPath, "書庫内の項目（圧縮）");
            sources.Add(entry.FullPath);
        }

        ZipArchiver.Create(sources, Path.Combine(Active.DirectoryPath, name));
        Active.Reload();
    }

    /// <summary>拡張子が <c>.zip</c> でなければ補う。</summary>
    private static string EnsureZipExtension(string name)
    {
        name = name.Trim();
        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name : name + ".zip";
    }

    /// <summary>アクティブ側カーソル位置をリネームする。書庫内項目は不可。</summary>
    public void RenameCurrent(string newName)
    {
        EnsureNotInsideArchive(Active.Current.FullPath, "書庫内の項目（リネーム）");
        _ops.Rename(Active.Current.FullPath, newName);
        Active.Reload();
    }

    /// <summary>
    /// アクティブ側で複数項目を一括リネームする。書庫内項目は不可。
    /// 名前の入れ替えや連番のずれで途中衝突しないよう、いったん一時名へ変えてから最終名へ付け替える(2フェーズ)。
    /// </summary>
    public void BulkRename(IReadOnlyList<(string FullPath, string NewName)> renames)
    {
        if (renames.Count == 0) return;
        foreach (var (fullPath, _) in renames)
            EnsureNotInsideArchive(fullPath, "書庫内の項目（一括リネーム）");

        var temps = new List<(string TempPath, string NewName)>(renames.Count);
        var n = 0;
        foreach (var (fullPath, newName) in renames)
        {
            var dir = Path.GetDirectoryName(fullPath)!;
            var tempName = $".__filer_rename_{n++}__";
            _ops.Rename(fullPath, tempName);
            temps.Add((Path.Combine(dir, tempName), newName));
        }
        foreach (var (tempPath, newName) in temps)
            _ops.Rename(tempPath, newName);

        Active.Reload();
    }

    /// <summary>アクティブ側の現在フォルダー直下に新規フォルダーを作成し、作成したフォルダーへ移動する。書庫内は不可。</summary>
    public void CreateDirectoryInActive(string name)
    {
        EnsureNotInArchive(Active.DirectoryPath, "フォルダー作成");
        var created = Path.Combine(Active.DirectoryPath, name);
        _ops.CreateDirectory(Active.DirectoryPath, name);
        Active.NavigateTo(created);
    }

    /// <summary>
    /// 書庫(.zip)の境界に掛かるパスなら例外で拒否する(書庫の書き換えは未対応)。
    /// 書庫ファイル自体も「書庫」とみなすため、コピー先・移動先などの格納先判定に使う。
    /// </summary>
    private static void EnsureNotInArchive(string path, string label)
    {
        if (ArchivePath.TrySplit(path, out _, out _))
            throw new IOException($"{label}は書庫(.zip)内のため操作できません。");
    }

    /// <summary>
    /// 書庫(.zip)の内部を指すパスなら例外で拒否する。
    /// 書庫ファイル自体は実ファイルとして許可するため、削除・移動など対象アイテムの判定に使う。
    /// </summary>
    private static void EnsureNotInsideArchive(string path, string label)
    {
        if (ArchivePath.IsInsideArchive(path))
            throw new IOException($"{label}は書庫(.zip)内のため操作できません。");
    }

    /// <summary>利用可能なドライブ一覧。</summary>
    public IReadOnlyList<DriveItem> GetDrives() => _drives.GetDrives();

    /// <summary>登録済みお気に入りツリー(項目+グループ)。</summary>
    public IReadOnlyList<FavoriteNode> GetFavoritesTree() => _favorites.GetTree();

    /// <summary>お気に入りの全グループパス一覧(「仕事/CLI」形式・深さ優先)。</summary>
    public IReadOnlyList<string> GetFavoriteGroups() => _favorites.GetGroupPaths();

    /// <summary>
    /// お気に入り登録の対象パス。
    /// カーソルがサブフォルダー上ならそのフォルダー、そうでなければ現在のフォルダー。
    /// </summary>
    public string FavoriteTargetPath => Active.TargetFolderPath;

    /// <summary>お気に入りを指定グループへ登録する(重複パスは登録せず false)。</summary>
    public bool AddFavorite(string path, string label, string group)
        => _favorites.Add(path, label, group);

    /// <summary>お気に入りのパス・ラベル・所属グループを更新する。</summary>
    public void UpdateFavorite(string oldPath, string newPath, string label, string group)
        => _favorites.Update(oldPath, newPath, label, group);

    /// <summary>お気に入りを削除する。</summary>
    public void RemoveFavorite(string path) => _favorites.Remove(path);

    /// <summary>お気に入りグループの名前を変更する(不正な名前・同名衝突は false)。</summary>
    public bool RenameFavoriteGroup(string groupPath, string newName)
        => _favorites.RenameGroup(groupPath, newName);

    /// <summary>お気に入りグループを中身ごと削除する。</summary>
    public void RemoveFavoriteGroup(string groupPath) => _favorites.RemoveGroup(groupPath);

    /// <summary>お気に入り項目を同じ階層内で上下に移動する(ショートカット番号の変更)。動いたら true。</summary>
    public bool MoveFavorite(string path, int delta) => _favorites.MoveItem(path, delta);

    /// <summary>お気に入りグループを同じ階層内で上下に移動する。動いたら true。</summary>
    public bool MoveFavoriteGroup(string groupPath, int delta) => _favorites.MoveGroup(groupPath, delta);

    /// <summary>アクティブ側を指定パスへ移動する。</summary>
    public void NavigateActiveTo(string path) => Active.NavigateTo(path);

    /// <summary>
    /// 差分表示の対象2ファイルを解決する。アクティブペインで2件マーク時はその2件、
    /// マーク無し時は左ペインのカーソル項目 vs 右ペインのカーソル項目を対象とする。
    /// </summary>
    public DiffResolution ResolveDiffTargets()
    {
        var marked = Active.Marked.Select(e => e.FullPath).ToList();
        return DiffTargetResolver.Resolve(marked, Left.SelectedItemPath, Right.SelectedItemPath);
    }

    /// <summary>
    /// フォルダー(ツリー)比較の対象2フォルダーを解決する。左ペインの現在フォルダー vs 右ペインの現在フォルダー。
    /// </summary>
    public FolderCompareResolution ResolveFolderCompareTargets() =>
        FolderCompareTargetResolver.Resolve(Left.DirectoryPath, Right.DirectoryPath);

    /// <summary>ファイル検索の結果を仮想一覧として登録し、表示用パスを返す(「転送して閉じる」用)。</summary>
    public string RegisterSearchResults(string label, string baseDirectory, IReadOnlyList<FileEntry> results)
        => _searchResults.Register(label, baseDirectory, results);

    /// <summary>
    /// フォルダー比較の結果を、指定した側(左/右)のペインへ仮想一覧として転送・表示する。
    /// </summary>
    public void TransferComparisonToPane(
        FolderCompareSide side, string label, string baseDirectory, IReadOnlyList<FileEntry> entries)
    {
        var path = _searchResults.Register(label, baseDirectory, entries);
        (side == FolderCompareSide.Left ? Left : Right).NavigateTo(path);
    }

    /// <summary>アクティブ側のカーソル項目を Windows の関連付けで開く。</summary>
    public void OpenSelectedWithAssociation()
    {
        var path = Active.SelectedItemPath;
        EnsureNotInsideArchive(path, "書庫内のファイル(関連付け起動)");
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
        });
    }

    /// <summary>指定 Id の外部ツールを起動する。引数テンプレートは現在のペイン状態で展開する。</summary>
    public void LaunchTool(string toolId)
    {
        var tool = Settings.Tools.FirstOrDefault(t => t.Id == toolId);
        if (tool is null) return;
        // 外部ツールは実パスを必要とするため、アクティブ側が書庫内なら拒否する。
        EnsureNotInArchive(Active.DirectoryPath, $"書庫内({tool.Label})");
        _tools.Launch(tool, BuildMacroContext());
    }

    /// <summary>引数テンプレート(マクロ)展開用の文脈を現在のペイン状態から作る。</summary>
    private ToolMacroContext BuildMacroContext()
    {
        var active = Active;
        var cursor = active.Current;
        var cursorName = cursor.IsParent ? "" : cursor.Name;

        return new ToolMacroContext(
            cursorName,
            TrimDir(active.DirectoryPath),
            TrimDir(Inactive.DirectoryPath),
            TrimDir(Left.DirectoryPath),
            TrimDir(Right.DirectoryPath),
            MarkedOrCurrentNames(active),
            MarkedOrCurrentFullPaths(active),
            // $MO/$mO は他方ペインの「マークのみ」(カーソルへのフォールバックなし)。
            Inactive.Marked.Where(e => !e.IsParent).Select(e => e.FullPath).ToList());
    }

    /// <summary>$MS 用: マーク or カーソルのファイル名。カーソルが ".." ならフォルダー名。</summary>
    private static IReadOnlyList<string> MarkedOrCurrentNames(PaneViewModel pane)
    {
        var targets = pane.Targets;   // MarkedOrCurrent(".." は含まない)
        if (targets.Count > 0) return targets.Select(e => e.Name).ToList();
        return new[] { Path.GetFileName(TrimDir(pane.DirectoryPath)) };
    }

    /// <summary>$MF 用: マーク or カーソルのフルパス。カーソルが ".." なら現在フォルダー。</summary>
    private static IReadOnlyList<string> MarkedOrCurrentFullPaths(PaneViewModel pane)
    {
        var targets = pane.Targets;
        if (targets.Count > 0) return targets.Select(e => e.FullPath).ToList();
        return new[] { TrimDir(pane.DirectoryPath) };
    }

    /// <summary>パス末尾の <c>\</c> を除く(ルート <c>C:\</c> は <c>C:</c> になる。マクロ仕様どおり)。</summary>
    private static string TrimDir(string path) => path.TrimEnd('\\');
}
