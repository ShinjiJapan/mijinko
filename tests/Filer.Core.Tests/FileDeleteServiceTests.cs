using Filer.Core;

namespace Filer.Core.Tests;

/// <summary>
/// 非同期削除エンジン。ごみ箱送り(Recycle)と完全削除(Permanent)の計画と実行を検証する。
/// 進捗(件数)とキャンセルの挙動も対象。
/// </summary>
public sealed class FileDeleteServiceTests : IDisposable
{
    private readonly string _root;

    public FileDeleteServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilerDel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string MakeFile(string rel, string content = "x")
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

    [Fact]
    public void BuildPlan_Permanent_SingleFile_CountsOne()
    {
        var f = MakeFile("a.txt");
        var plan = FileDeleteService.BuildPlan(new[] { f }, DeleteKind.Permanent);
        Assert.Equal(1, plan.TotalItems);
    }

    [Fact]
    public void BuildPlan_Permanent_Directory_CountsAllFilesAndDirs()
    {
        MakeFile("d/a.txt");
        MakeFile("d/sub/b.txt");
        var d = Path.Combine(_root, "d");

        var plan = FileDeleteService.BuildPlan(new[] { d }, DeleteKind.Permanent);

        // ファイル2 + ディレクトリ2(d, d/sub) = 4
        Assert.Equal(4, plan.TotalItems);
    }

    [Fact]
    public void Execute_Permanent_File_Removes()
    {
        var f = MakeFile("a.txt");
        var plan = FileDeleteService.BuildPlan(new[] { f }, DeleteKind.Permanent);

        FileDeleteService.Execute(plan, DeleteKind.Permanent, null, CancellationToken.None);

        Assert.False(File.Exists(f));
    }

    [Fact]
    public void Execute_Permanent_Directory_RemovesRecursively_ReportsProgress()
    {
        MakeFile("d/a.txt");
        MakeFile("d/sub/b.txt");
        var d = Path.Combine(_root, "d");
        var plan = FileDeleteService.BuildPlan(new[] { d }, DeleteKind.Permanent);

        var reports = new List<FileTransferProgress>();
        FileDeleteService.Execute(plan, DeleteKind.Permanent, new SyncProgress(reports.Add), CancellationToken.None);

        Assert.False(Directory.Exists(d));
        Assert.NotEmpty(reports);
        Assert.Equal(plan.TotalItems, reports[^1].DoneFiles);
        Assert.Equal(plan.TotalItems, reports[^1].TotalFiles);
    }

    [Fact]
    public void BuildPlan_Recycle_CountsTopLevelTargets()
    {
        var f1 = MakeFile("a.txt");
        var f2 = MakeFile("b.txt");
        var plan = FileDeleteService.BuildPlan(new[] { f1, f2 }, DeleteKind.Recycle);
        Assert.Equal(2, plan.TotalItems);
    }

    [Fact]
    public void Execute_Recycle_File_RemovesFromOriginalLocation()
    {
        var f = MakeFile("recycle.txt");
        var plan = FileDeleteService.BuildPlan(new[] { f }, DeleteKind.Recycle);

        FileDeleteService.Execute(plan, DeleteKind.Recycle, null, CancellationToken.None);

        Assert.False(File.Exists(f));   // ごみ箱へ送られ元の場所からは消える
    }

    [Fact]
    public void BuildPlan_MissingSource_Throws()
    {
        var missing = Path.Combine(_root, "nope.txt");
        Assert.Throws<FileNotFoundException>(() => FileDeleteService.BuildPlan(new[] { missing }, DeleteKind.Permanent));
    }

    [Fact]
    public void Execute_Permanent_PreCancelled_ThrowsAndKeepsFile()
    {
        var f = MakeFile("a.txt");
        var plan = FileDeleteService.BuildPlan(new[] { f }, DeleteKind.Permanent);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => FileDeleteService.Execute(plan, DeleteKind.Permanent, null, cts.Token));
        Assert.True(File.Exists(f));
    }

    private sealed class SyncProgress : IProgress<FileTransferProgress>
    {
        private readonly Action<FileTransferProgress> _on;
        public SyncProgress(Action<FileTransferProgress> on) => _on = on;
        public void Report(FileTransferProgress value) => _on(value);
    }
}
