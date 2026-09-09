using System.Windows.Media;

namespace BackupProjekt.App.Models;

public class StagedItem
{
    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public string SizeFormatted { get; set; } = string.Empty;
    public ImageSource? Icon { get; set; }
}
