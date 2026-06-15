using System.IO;
using Filer.Core;

namespace Filer.Core.Tests;

public class FolderCompareTargetResolverTests : IDisposable
{
    private readonly string _root;
    private readonly string _dirA;
    private readonly string _dirB;
    private readonly string _file;

    public FolderCompareTargetResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderCmpTarget_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _dirA = Path.Combine(_root, "a"); Directory.CreateDirectory(_dirA);
        _dirB = Path.Combine(_root, "b"); Directory.CreateDirectory(_dirB);
        _file = Path.Combine(_root, "f.txt"); File.WriteAllText(_file, "f");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TwoDirectories_Resolved()
    {
        var r = FolderCompareTargetResolver.Resolve(_dirA, _dirB);

        Assert.Null(r.Error);
        Assert.Equal(_dirA, r.Targets!.LeftPath);
        Assert.Equal(_dirB, r.Targets.RightPath);
    }

    [Fact]
    public void SamePath_ReturnsError()
    {
        var r = FolderCompareTargetResolver.Resolve(_dirA, _dirA);

        Assert.Null(r.Targets);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void File_ReturnsError()
    {
        var r = FolderCompareTargetResolver.Resolve(_file, _dirB);

        Assert.Null(r.Targets);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Missing_ReturnsError()
    {
        var r = FolderCompareTargetResolver.Resolve(Path.Combine(_root, "nope"), _dirB);

        Assert.Null(r.Targets);
        Assert.NotNull(r.Error);
    }
}
