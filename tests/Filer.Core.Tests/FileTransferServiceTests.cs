using Filer.Core;

namespace Filer.Core.Tests;

/// <summary>
/// 非同期コピー/移動エンジン。計画(BuildPlan)と実行(Execute)を一時ディレクトリで検証する。
/// 進捗(IProgress)とキャンセル(CancellationToken)の挙動も対象。
/// </summary>
public sealed class FileTransferServiceTests : IDisposable
{
    private readonly string _root;

    public FileTransferServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilerXfer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string MakeFile(string rel, string content)
    {
        var path = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string MakeDir(string rel)
    {
        var path = Path.Combine(_root, rel);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Run(FileTransferPlan plan, FileTransferKind kind, IProgress<FileTransferProgress>? progress = null)
        => FileTransferService.Execute(plan, kind, progress, CancellationToken.None);

    [Fact]
    public void BuildPlan_SingleFile_CountsBytesAndFiles()
    {
        var src = MakeFile("a.txt", "hello");
        var dest = MakeDir("dest");

        var plan = FileTransferService.BuildPlan(new[] { src }, dest, FileTransferKind.Copy);

        Assert.Equal(1, plan.TotalFiles);
        Assert.Equal(5, plan.TotalBytes);
        Assert.False(plan.IsEmpty);
    }

    [Fact]
    public void Execute_CopyFile_DuplicatesContentAndKeepsSource()
    {
        var src = MakeFile("a.txt", "hello");
        var dest = MakeDir("dest");

        var plan = FileTransferService.BuildPlan(new[] { src }, dest, FileTransferKind.Copy);
        Run(plan, FileTransferKind.Copy);

        Assert.True(File.Exists(src));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public void Execute_CopyFile_PreservesSourceTimestamps()
    {
        var src = MakeFile("a.txt", "hello");
        var dest = MakeDir("dest");
        // コピー元を過去日時にしておき、コピー後も同じ日時(現在日時ではない)であることを確認する。
        var created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var modified = new DateTime(2021, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        File.SetCreationTimeUtc(src, created);
        File.SetLastWriteTimeUtc(src, modified);

        var plan = FileTransferService.BuildPlan(new[] { src }, dest, FileTransferKind.Copy);
        Run(plan, FileTransferKind.Copy);

        var copied = Path.Combine(dest, "a.txt");
        Assert.Equal(created, File.GetCreationTimeUtc(copied));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(copied));
    }

    [Fact]
    public void Execute_CopyDirectory_RecreatesTreeIncludingEmptyDir()
    {
        MakeFile("srcdir/inner.txt", "deep");
        MakeFile("srcdir/sub/leaf.txt", "leaf");
        MakeDir("srcdir/empty");
        var src = Path.Combine(_root, "srcdir");
        var dest = MakeDir("dest");

        var plan = FileTransferService.BuildPlan(new[] { src }, dest, FileTransferKind.Copy);
        Run(plan, FileTransferKind.Copy);

        Assert.True(File.Exists(Path.Combine(dest, "srcdir", "inner.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "srcdir", "sub", "leaf.txt")));
        Assert.True(Directory.Exists(Path.Combine(dest, "srcdir", "empty")));
    }

    [Fact]
    public void BuildPlan_CopyToSameDirectory_Throws()
    {
        var src = MakeFile("a.txt", "x");
        Assert.Throws<IOException>(() => FileTransferService.BuildPlan(new[] { src }, _root, FileTransferKind.Copy));
    }

    [Fact]
    public void BuildPlan_ExistingTarget_Throws()
    {
        var src = MakeFile("a.txt", "x");
        var dest = MakeDir("dest");
        File.WriteAllText(Path.Combine(dest, "a.txt"), "old");

        Assert.Throws<IOException>(() => FileTransferService.BuildPlan(new[] { src }, dest, FileTransferKind.Copy));
    }

    [Fact]
    public void BuildPlan_CopyDirectoryOntoExisting_MergesAndDetectsFileConflict()
    {
        MakeFile("srcdir/a.txt", "x");
        MakeFile("srcdir/b.txt", "y");
        var src = Path.Combine(_root, "srcdir");
        var dest = MakeDir("dest");
        // 既存のマージ先に衝突ファイルを置く。
        Directory.CreateDirectory(Path.Combine(dest, "srcdir"));
        File.WriteAllText(Path.Combine(dest, "srcdir", "a.txt"), "old");

        Assert.Throws<IOException>(() => FileTransferService.BuildPlan(new[] { src }, dest, FileTransferKind.Copy));
    }

    [Fact]
    public void Execute_MoveFile_SameVolume_RemovesSource()
    {
        var src = MakeFile("a.txt", "hello");
        var dest = MakeDir("dest");

        var plan = FileTransferService.BuildPlan(new[] { src }, dest, FileTransferKind.Move);
        Run(plan, FileTransferKind.Move);

        Assert.False(File.Exists(src));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public void Execute_MoveDirectory_SameVolume_RemovesSource()
    {
        MakeFile("srcdir/inner.txt", "deep");
        var src = Path.Combine(_root, "srcdir");
        var dest = MakeDir("dest");

        var plan = FileTransferService.BuildPlan(new[] { src }, dest, FileTransferKind.Move);
        Run(plan, FileTransferKind.Move);

        Assert.False(Directory.Exists(src));
        Assert.True(File.Exists(Path.Combine(dest, "srcdir", "inner.txt")));
    }

    [Fact]
    public void BuildPlan_MoveOntoExistingTarget_Throws()
    {
        var src = MakeFile("a.txt", "x");
        var dest = MakeDir("dest");
        File.WriteAllText(Path.Combine(dest, "a.txt"), "old");

        Assert.Throws<IOException>(() => FileTransferService.BuildPlan(new[] { src }, dest, FileTransferKind.Move));
    }

    [Fact]
    public void Execute_ReportsProgress_FinalEqualsTotal()
    {
        MakeFile("d/a.txt", new string('a', 100));
        MakeFile("d/b.txt", new string('b', 200));
        var src = Path.Combine(_root, "d");
        var dest = MakeDir("dest");

        var plan = FileTransferService.BuildPlan(new[] { src }, dest, FileTransferKind.Copy);
        var reports = new List<FileTransferProgress>();
        FileTransferService.Execute(plan, FileTransferKind.Copy,
            new SyncProgress(reports.Add), CancellationToken.None);

        Assert.NotEmpty(reports);
        var last = reports[^1];
        Assert.Equal(plan.TotalBytes, last.DoneBytes);
        Assert.Equal(plan.TotalFiles, last.DoneFiles);
        Assert.Equal(300, plan.TotalBytes);
    }

    [Fact]
    public void Execute_AlreadyCancelled_ThrowsAndCopiesNothing()
    {
        var src = MakeFile("a.txt", "hello");
        var dest = MakeDir("dest");
        var plan = FileTransferService.BuildPlan(new[] { src }, dest, FileTransferKind.Copy);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => FileTransferService.Execute(plan, FileTransferKind.Copy, null, cts.Token));
        Assert.False(File.Exists(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public void Execute_CancelMidCopy_RemovesPartialFile()
    {
        // 4MB間隔の進捗通知が複数回出る大きさにし、最初の途中通知でキャンセルする。
        var src = Path.Combine(_root, "big.bin");
        File.WriteAllBytes(src, new byte[12 * 1024 * 1024]);
        var dest = MakeDir("dest");
        var plan = FileTransferService.BuildPlan(new[] { src }, dest, FileTransferKind.Copy);

        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p.DoneBytes > 0 && p.DoneBytes < p.TotalBytes) cts.Cancel(); });

        Assert.Throws<OperationCanceledException>(
            () => FileTransferService.Execute(plan, FileTransferKind.Copy, progress, cts.Token));
        // コピー途中で中断した半端ファイルは残さない。
        Assert.False(File.Exists(Path.Combine(dest, "big.bin")));
    }

    /// <summary>IProgress をその場で同期的に呼ぶテスト用実装(Progress&lt;T&gt; はSyncContext依存のため)。</summary>
    private sealed class SyncProgress : IProgress<FileTransferProgress>
    {
        private readonly Action<FileTransferProgress> _on;
        public SyncProgress(Action<FileTransferProgress> on) => _on = on;
        public void Report(FileTransferProgress value) => _on(value);
    }
}
