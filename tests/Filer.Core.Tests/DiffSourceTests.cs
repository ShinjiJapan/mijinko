using System.IO;
using System.Text;
using Filer.Core;

namespace Filer.Core.Tests;

public class DiffSourceTests : IDisposable
{
    private readonly string _dir;

    public DiffSourceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "DiffSourceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ReadLines_SplitsMixedNewlines()
    {
        var path = Write("mixed.txt", Encoding.UTF8.GetBytes("a\r\nb\nc\rd"));

        var (binary, lines) = DiffSource.ReadLines(path);

        Assert.False(binary);
        Assert.Equal(new[] { "a", "b", "c", "d" }, lines);
    }

    [Fact]
    public void ReadLines_TrailingNewline_DoesNotAddEmptyLine()
    {
        var path = Write("trail.txt", Encoding.UTF8.GetBytes("a\nb\n"));

        var (_, lines) = DiffSource.ReadLines(path);

        Assert.Equal(new[] { "a", "b" }, lines);
    }

    [Fact]
    public void ReadLines_Utf8Bom_IsStripped()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("あ\nい")).ToArray();
        var path = Write("bom.txt", bytes);

        var (_, lines) = DiffSource.ReadLines(path);

        Assert.Equal(new[] { "あ", "い" }, lines);
    }

    [Fact]
    public void ReadLines_NulBytes_DetectedAsBinary()
    {
        var path = Write("bin.dat", new byte[] { 0x41, 0x00, 0x42, 0x00 });

        var (binary, lines) = DiffSource.ReadLines(path);

        Assert.True(binary);
        Assert.Empty(lines);
    }

    [Fact]
    public void ReadLines_EmptyFile_NoLines()
    {
        var path = Write("empty.txt", Array.Empty<byte>());

        var (binary, lines) = DiffSource.ReadLines(path);

        Assert.False(binary);
        Assert.Empty(lines);
    }
}
