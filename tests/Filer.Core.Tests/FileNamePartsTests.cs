using Filer.Core;

namespace Filer.Core.Tests;

public sealed class FileNamePartsTests
{
    [Theory]
    [InlineData("file.txt", "file", "txt")]
    [InlineData("a.tar.gz", "a.tar", "gz")]
    [InlineData("Program.cs", "Program", "cs")]
    public void Split_NormalFile_SplitsAtLastDot(string name, string expectedBase, string expectedExt)
    {
        var (baseName, ext) = FileNameParts.Split(name);
        Assert.Equal(expectedBase, baseName);
        Assert.Equal(expectedExt, ext);
    }

    [Theory]
    [InlineData(".gitignore")]
    [InlineData(".editorconfig")]
    [InlineData(".npmrc")]
    public void Split_DotFile_IsAllNameNoExtension(string name)
    {
        // 先頭ドットは拡張子の開始とみなさない(全体が名前)
        var (baseName, ext) = FileNameParts.Split(name);
        Assert.Equal(name, baseName);
        Assert.Equal("", ext);
    }

    [Fact]
    public void Split_DotFileWithExtension_KeepsLeadingDotInName()
    {
        var (baseName, ext) = FileNameParts.Split(".env.local");
        Assert.Equal(".env", baseName);
        Assert.Equal("local", ext);
    }

    [Theory]
    [InlineData("noext")]
    [InlineData("")]
    public void Split_NoDot_IsAllName(string name)
    {
        var (baseName, ext) = FileNameParts.Split(name);
        Assert.Equal(name, baseName);
        Assert.Equal("", ext);
    }
}
