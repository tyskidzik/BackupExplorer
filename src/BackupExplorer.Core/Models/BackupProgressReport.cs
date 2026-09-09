using System;

namespace BackupExplorer.Core.Models;

public class BackupProgressReport
{
    public long ProcessedBytes { get; set; }
    public long TotalBytes { get; set; }
    public string CurrentFileName { get; set; } = string.Empty;
    public int CurrentFileIndex { get; set; }
    public int TotalFiles { get; set; }
    public double Percentage { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public double SpeedBytesPerSec { get; set; }
    public TimeSpan? EstimatedRemaining { get; set; }
}
