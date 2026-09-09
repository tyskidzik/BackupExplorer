using System.Windows.Media;

namespace BackupExplorer.App.Models;

public class DriveOrFolderItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDrive { get; set; }
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public string FreeSpaceFormatted { get; set; } = string.Empty;
    public double UsedPercent { get; set; }
    public ImageSource? Icon { get; set; }
}
