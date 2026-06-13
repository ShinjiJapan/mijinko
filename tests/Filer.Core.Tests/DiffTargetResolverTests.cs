using System.IO;
using Filer.Core;

namespace Filer.Core.Tests;

public class DiffTargetResolverTests : IDisposable
{
    private readonly string _dir;
    private readonly string _fileA;
    private readonly string _fileB;
    private readonly string _fileC;
    private readonly string _subDir;

    public DiffTargetResolverTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "DiffTargetTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _fileA = Path.Combine(_dir, "a.txt"); File.WriteAllText(_fileA, "a");
        _fileB = Path.Combine(_dir, "b.txt"); File.WriteAllText(_fileB, "b");
        _fileC = Path.Combine(_dir, "c.txt"); File.WriteAllText(_fileC, "c");
        _subDir = Path.Combine(_dir, "sub"); Directory.CreateDirectory(_subDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static IReadOnlyList<string> Marks(params string[] p) => p;

    [Fact]
    public void TwoMarks_UsesThemInListOrder()
    {
        var r = DiffTargetResolver.Resolve(Marks(_fileA, _fileB), _fileC, _fileC);

        Assert.Null(r.Error);
        Assert.Equal(_fileA, r.Targets!.LeftPath);
        Assert.Equal(_fileB, r.Targets.RightPath);
    }

    [Fact]
    public void NoMarks_UsesLeftAndRightCursor()
    {
        var r = DiffTargetResolver.Resolve(Marks(), _fileA, _fileB);

        Assert.Null(r.Error);
        Assert.Equal(_fileA, r.Targets!.LeftPath);
        Assert.Equal(_fileB, r.Targets.RightPath);
    }

    [Fact]
    public void OneMark_ReturnsError()
    {
        var r = DiffTargetResolver.Resolve(Marks(_fileA), _fileA, _fileB);

        Assert.Null(r.Targets);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void ThreeMarks_ReturnsError()
    {
        var r = DiffTargetResolver.Resolve(Marks(_fileA, _fileB, _fileC), _fileA, _fileB);

        Assert.Null(r.Targets);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Directory_ReturnsError()
    {
        var r = DiffTargetResolver.Resolve(Marks(), _subDir, _fileB);

        Assert.Null(r.Targets);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void MissingFile_ReturnsError()
    {
        var r = DiffTargetResolver.Resolve(Marks(), Path.Combine(_dir, "nope.txt"), _fileB);

        Assert.Null(r.Targets);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void SamePathOnBothSides_ReturnsError()
    {
        var r = DiffTargetResolver.Resolve(Marks(), _fileA, _fileA);

        Assert.Null(r.Targets);
        Assert.NotNull(r.Error);
    }
}
