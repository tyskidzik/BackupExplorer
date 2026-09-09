using System.Collections.Generic;

namespace BackupExplorer.Core.Models;

public class BackupConfig
{
    public List<string> SourcePaths { get; set; } = new();
    public string DestinationDirectory { get; set; } = string.Empty;
    public string ArchiveBaseName { get; set; } = "Backup";
    public BackupFormat Format { get; set; } = BackupFormat.Zip;
    public BackupCompressionLevel Level { get; set; } = BackupCompressionLevel.Normal;
    public bool PreserveDirectoryStructure { get; set; } = true;
    public bool AppendTimestamp { get; set; } = true;
}
