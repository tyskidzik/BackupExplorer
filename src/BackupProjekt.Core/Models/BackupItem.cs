using System;

namespace BackupProjekt.Core.Models;

public class BackupItem
{
    public string FullPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsSelected { get; set; } = true;
}
