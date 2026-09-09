using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BackupExplorer.Core.Models;

namespace BackupExplorer.Core.Services;

public interface IBackupEngine
{
    Task<List<BackupItem>> ScanItemsAsync(IEnumerable<string> paths, CancellationToken ct = default);
    Task<BackupResult> ExecuteBackupAsync(BackupConfig config, IProgress<BackupProgressReport>? progress = null, CancellationToken ct = default);
}
