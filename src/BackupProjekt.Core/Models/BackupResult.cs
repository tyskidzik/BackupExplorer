using System;
using System.Collections.Generic;

namespace BackupProjekt.Core.Models;

public class BackupResult
{
    public bool Success { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public int TotalFilesProcessed { get; set; }
    public long TotalBytesProcessed { get; set; }
    public long OutputSizeBytes { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Warnings { get; set; } = new();
}
