namespace Filer.Core;

/// <summary>1つのドライブ情報。サイズは準備済みドライブのみ有効(未準備は 0)。</summary>
public sealed record DriveItem(string RootPath, string VolumeLabel, bool IsReady, long TotalSize, long FreeSpace);

/// <summary>利用可能なドライブを列挙する抽象。</summary>
public interface IDriveProvider
{
    IReadOnlyList<DriveItem> GetDrives();
}

/// <summary>実環境のドライブを列挙する <see cref="IDriveProvider"/> 実装。</summary>
public sealed class DriveLister : IDriveProvider
{
    public IReadOnlyList<DriveItem> GetDrives()
    {
        var list = new List<DriveItem>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (d.IsReady)
                list.Add(new DriveItem(d.RootDirectory.FullName, d.VolumeLabel, true, d.TotalSize, d.AvailableFreeSpace));
            else
                list.Add(new DriveItem(d.RootDirectory.FullName, string.Empty, false, 0, 0));
        }
        return list;
    }
}
