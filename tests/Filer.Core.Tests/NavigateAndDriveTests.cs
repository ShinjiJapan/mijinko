using System.Runtime.InteropServices;
using Filer.Core;

namespace Filer.Core.Tests;

public sealed class NavigateAndDriveTests
{
    [Fact]
    public void NavigateTo_LoadsArbitraryPath_AndCursorAtTop()
    {
        var reader = new FakeDirectoryReader();
        reader.AddDirectory(@"C:\work", new FileEntry("a.txt", @"C:\work\a.txt", false, 1, default));
        reader.AddDirectory(@"D:\data", new FileEntry("b.txt", @"D:\data\b.txt", false, 1, default));

        var pane = new PaneState(reader, @"C:\work");
        pane.NavigateTo(@"D:\data");

        Assert.Equal(@"D:\data", pane.CurrentPath);
        Assert.Equal(0, pane.CursorIndex);
        Assert.Contains(pane.Entries, e => e.Name == "b.txt");
    }

    [Fact]
    public void DriveLister_ReturnsReadyDrives_IncludingSystemDrive()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var drives = new DriveLister().GetDrives();

        Assert.NotEmpty(drives);
        Assert.Contains(drives, d => d.RootPath.StartsWith("C:", StringComparison.OrdinalIgnoreCase));
        Assert.All(drives, d => Assert.EndsWith("\\", d.RootPath)); // ルートは "C:\" 形式
    }
}
