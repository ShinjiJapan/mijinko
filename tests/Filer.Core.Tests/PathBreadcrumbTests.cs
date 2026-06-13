using Filer.Core;
using Xunit;

namespace Filer.Core.Tests;

public class PathBreadcrumbTests
{
    [Fact]
    public void Build_DrivePath_SplitsIntoCumulativeSegments()
    {
        var segments = PathBreadcrumb.Build(@"G:\data\download");

        Assert.Collection(segments,
            s => { Assert.Equal(@"G:\", s.Name); Assert.Equal(@"G:\", s.Path); },
            s => { Assert.Equal("data", s.Name); Assert.Equal(@"G:\data", s.Path); },
            s => { Assert.Equal("download", s.Name); Assert.Equal(@"G:\data\download", s.Path); });
    }

    [Fact]
    public void Build_DriveRootOnly_ReturnsSingleSegment()
    {
        var segments = PathBreadcrumb.Build(@"C:\");

        Assert.Single(segments);
        Assert.Equal(@"C:\", segments[0].Name);
        Assert.Equal(@"C:\", segments[0].Path);
    }

    [Fact]
    public void Build_TrailingSeparator_IsIgnored()
    {
        var segments = PathBreadcrumb.Build(@"C:\Users\");

        Assert.Collection(segments,
            s => Assert.Equal(@"C:\", s.Path),
            s => { Assert.Equal("Users", s.Name); Assert.Equal(@"C:\Users", s.Path); });
    }

    [Fact]
    public void Build_ForwardSlashes_AreNormalized()
    {
        var segments = PathBreadcrumb.Build("C:/foo/bar");

        Assert.Collection(segments,
            s => Assert.Equal(@"C:\", s.Path),
            s => Assert.Equal(@"C:\foo", s.Path),
            s => Assert.Equal(@"C:\foo\bar", s.Path));
    }

    [Fact]
    public void Build_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(PathBreadcrumb.Build(""));
        Assert.Empty(PathBreadcrumb.Build("   "));
        Assert.Empty(PathBreadcrumb.Build(null!));
    }

    [Fact]
    public void Build_ArchiveVirtualPath_SplitsLikeNormalPath()
    {
        var segments = PathBreadcrumb.Build(@"C:\a\x.zip\inner\dir");

        Assert.Collection(segments,
            s => Assert.Equal(@"C:\", s.Path),
            s => { Assert.Equal("a", s.Name); Assert.Equal(@"C:\a", s.Path); },
            s => { Assert.Equal("x.zip", s.Name); Assert.Equal(@"C:\a\x.zip", s.Path); },
            s => { Assert.Equal("inner", s.Name); Assert.Equal(@"C:\a\x.zip\inner", s.Path); },
            s => { Assert.Equal("dir", s.Name); Assert.Equal(@"C:\a\x.zip\inner\dir", s.Path); });
    }

    [Fact]
    public void Build_UncPath_KeepsServerShareAsRoot()
    {
        var segments = PathBreadcrumb.Build(@"\\server\share\dir");

        Assert.Collection(segments,
            s => { Assert.Equal(@"\\server\share", s.Name); Assert.Equal(@"\\server\share", s.Path); },
            s => { Assert.Equal("dir", s.Name); Assert.Equal(@"\\server\share\dir", s.Path); });
    }
}
