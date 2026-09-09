using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackupExplorer.Core.Models;
using SharpCompress.Common;
using SharpCompress.Writers.SevenZip;
using SharpCompress.Writers.Tar;
using SharpCompress.Writers.Zip;

namespace BackupExplorer.Core.Services;

public class BackupEngine : IBackupEngine
{
    private const int BufferSize = 64 * 1024; // 64 KB chunks

    public static string GetDrivePrefix(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return "Root";

        string root = Path.GetPathRoot(fullPath) ?? string.Empty;
        if (string.IsNullOrEmpty(root)) return "Root";

        // Standard Windows drive letter: "C:\", "D:\", "C:"
        if (root.Length >= 2 && char.IsLetter(root[0]) && root[1] == ':')
        {
            return $"Drive_{char.ToUpperInvariant(root[0])}";
        }

        // UNC Share: "\\server\share\" or "//server/share"
        string trimmed = root.Trim('\\', '/');
        if (!string.IsNullOrEmpty(trimmed))
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = trimmed.Select(c => c == '\\' || c == '/' ? '_' : (invalid.Contains(c) ? '_' : c)).ToArray();
            return "Share_" + new string(chars);
        }

        return "Drive";
    }

    public async Task<List<BackupItem>> ScanItemsAsync(IEnumerable<string> paths, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var result = new List<BackupItem>();
            var validPaths = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();

            var driveRoots = validPaths
                .Select(p =>
                {
                    try { return Path.GetPathRoot(Path.GetFullPath(p)); }
                    catch { return null; }
                })
                .Where(r => !string.IsNullOrEmpty(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool isMultiDrive = driveRoots.Count > 1;

            foreach (var path in validPaths)
            {
                ct.ThrowIfCancellationRequested();

                string? drivePrefix = null;
                if (isMultiDrive)
                {
                    try
                    {
                        string full = Path.GetFullPath(path);
                        drivePrefix = GetDrivePrefix(full);
                    }
                    catch { }
                }

                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    string relative = !string.IsNullOrEmpty(drivePrefix)
                        ? Path.Combine(drivePrefix, fi.Name)
                        : fi.Name;

                    result.Add(new BackupItem
                    {
                        FullPath = fi.FullName,
                        RelativePath = relative,
                        IsDirectory = false,
                        Size = fi.Length,
                        LastModified = fi.LastWriteTime
                    });
                }
                else if (Directory.Exists(path))
                {
                    var di = new DirectoryInfo(path);
                    string rootParent = di.Parent?.FullName ?? di.FullName;

                    ScanDirectoryRecursive(di, rootParent, result, ct, drivePrefix);
                }
            }

            return result;
        }, ct);
    }

    private void ScanDirectoryRecursive(DirectoryInfo dir, string rootPath, List<BackupItem> result, CancellationToken ct, string? drivePrefix = null)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            foreach (var file in dir.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    string relative = Path.GetRelativePath(rootPath, file.FullName);
                    if (!string.IsNullOrEmpty(drivePrefix))
                    {
                        relative = Path.Combine(drivePrefix, relative);
                    }

                    result.Add(new BackupItem
                    {
                        FullPath = file.FullName,
                        RelativePath = relative,
                        IsDirectory = false,
                        Size = file.Length,
                        LastModified = file.LastWriteTime
                    });
                }
                catch { }
            }

            foreach (var subDir in dir.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                if ((subDir.Attributes & FileAttributes.Hidden) != 0) continue;
                ScanDirectoryRecursive(subDir, rootPath, result, ct, drivePrefix);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (Exception) { }
    }

    public async Task<BackupResult> ExecuteBackupAsync(
        BackupConfig config,
        IProgress<BackupProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new BackupResult();

        if (string.IsNullOrWhiteSpace(config.DestinationDirectory))
        {
            result.Success = false;
            result.ErrorMessage = "Destination directory is not specified.";
            return result;
        }

        string outputName = DetermineOutputName(config);
        string finalOutputPath = Path.Combine(config.DestinationDirectory, outputName);
        result.OutputPath = finalOutputPath;

        try
        {
            Directory.CreateDirectory(config.DestinationDirectory);

            // 1. Scan files
            var items = await ScanItemsAsync(config.SourcePaths, ct);
            if (items.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No files found to backup.";
                return result;
            }

            long totalBytes = items.Sum(i => i.Size);
            int totalFiles = items.Count;

            if (config.Format == BackupFormat.Copy)
            {
                await ExecuteDirectCopyAsync(items, config, totalBytes, totalFiles, progress, ct);
                result.OutputSizeBytes = totalBytes;
            }
            else
            {
                await ExecuteArchiveAsync(items, config, finalOutputPath, totalBytes, totalFiles, progress, ct);
                if (File.Exists(finalOutputPath))
                {
                    result.OutputSizeBytes = new FileInfo(finalOutputPath).Length;
                }
            }

            sw.Stop();
            result.Success = true;
            result.TotalFilesProcessed = totalFiles;
            result.TotalBytesProcessed = totalBytes;
            result.Duration = sw.Elapsed;
            return result;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            result.Success = false;
            result.ErrorMessage = "Backup was cancelled by user.";
            result.Duration = sw.Elapsed;
            TryDeleteFile(finalOutputPath);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Duration = sw.Elapsed;
            if (config.Format != BackupFormat.Copy)
            {
                TryDeleteFile(finalOutputPath);
            }
            return result;
        }
    }

    private async Task ExecuteDirectCopyAsync(
        List<BackupItem> items,
        BackupConfig config,
        long totalBytes,
        int totalFiles,
        IProgress<BackupProgressReport>? progress,
        CancellationToken ct)
    {
        long processedBytes = 0;
        var sw = Stopwatch.StartNew();
        long lastReportMs = 0;

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = items[i];
            string fileName = Path.GetFileName(item.FullPath);

            // Report start of processing this file immediately
            ReportProgress(progress, processedBytes, totalBytes, fileName, i + 1, totalFiles, sw.Elapsed);

            string targetRelative = config.PreserveDirectoryStructure ? item.RelativePath : fileName;
            string destFile = Path.Combine(config.DestinationDirectory, targetRelative);

            string? parentDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            long fileBytesBefore = processedBytes;
            long fileBytesRead = 0;

            await using (var sourceStream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, true))
            await using (var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
            {
                byte[] buffer = new byte[BufferSize];
                int read;
                while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await destStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    fileBytesRead += read;

                    long current = fileBytesBefore + fileBytesRead;
                    if (sw.ElapsedMilliseconds - lastReportMs >= 100)
                    {
                        lastReportMs = sw.ElapsedMilliseconds;
                        ReportProgress(progress, current, totalBytes, fileName, i + 1, totalFiles, sw.Elapsed);
                    }
                }
            }

            processedBytes = fileBytesBefore + item.Size;
            ReportProgress(progress, processedBytes, totalBytes, fileName, i + 1, totalFiles, sw.Elapsed);

            File.SetLastWriteTime(destFile, item.LastModified);
        }
    }

    private async Task ExecuteArchiveAsync(
        List<BackupItem> items,
        BackupConfig config,
        string outputPath,
        long totalBytes,
        int totalFiles,
        IProgress<BackupProgressReport>? progress,
        CancellationToken ct)
    {
        await Task.Run(() =>
        {
            long processedBytes = 0;
            var sw = Stopwatch.StartNew();

            using var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize);

            switch (config.Format)
            {
                case BackupFormat.Zip:
                    var zipOptions = new ZipWriterOptions(GetSharpCompressZipType(config.Level))
                    {
                        CompressionLevel = GetZipCompressionLevelInt(config.Level)
                    };
                    using (var zipWriter = new ZipWriter(outStream, zipOptions))
                    {
                        WriteItemsToWriter(zipWriter, items, config, totalBytes, totalFiles, ref processedBytes, sw, progress, ct);
                    }
                    break;

                case BackupFormat.SevenZip:
                    var szOptions = new SevenZipWriterOptions(GetSharpCompress7zType(config.Level));
                    using (var szWriter = new SevenZipWriter(outStream, szOptions))
                    {
                        WriteItemsToWriter(szWriter, items, config, totalBytes, totalFiles, ref processedBytes, sw, progress, ct);
                    }
                    break;

                case BackupFormat.Tar:
                    var tarOptions = new TarWriterOptions(CompressionType.None, true);
                    using (var tarWriter = new TarWriter(outStream, tarOptions))
                    {
                        WriteItemsToWriter(tarWriter, items, config, totalBytes, totalFiles, ref processedBytes, sw, progress, ct);
                    }
                    break;

                case BackupFormat.TarGz:
                    var tarGzOptions = new TarWriterOptions(CompressionType.GZip, true);
                    using (var tarGzWriter = new TarWriter(outStream, tarGzOptions))
                    {
                        WriteItemsToWriter(tarGzWriter, items, config, totalBytes, totalFiles, ref processedBytes, sw, progress, ct);
                    }
                    break;
            }
        }, ct);
    }

    private void WriteItemsToWriter(
        dynamic writer,
        List<BackupItem> items,
        BackupConfig config,
        long totalBytes,
        int totalFiles,
        ref long processedBytes,
        Stopwatch sw,
        IProgress<BackupProgressReport>? progress,
        CancellationToken ct)
    {
        long lastReportMs = 0;

        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = items[i];
            string fileName = Path.GetFileName(item.FullPath);
            string entryPath = config.PreserveDirectoryStructure ? item.RelativePath.Replace('\\', '/') : fileName;

            // Immediately notify that this file is being compressed
            ReportProgress(progress, processedBytes, totalBytes, fileName, i + 1, totalFiles, sw.Elapsed);

            long fileBytesBefore = processedBytes;
            long fileBytesRead = 0;

            using (var fs = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize))
            using (var reportingStream = new ProgressReportingStream(fs, bytesRead =>
            {
                ct.ThrowIfCancellationRequested();
                fileBytesRead += bytesRead;
                long current = fileBytesBefore + fileBytesRead;

                if (sw.ElapsedMilliseconds - lastReportMs >= 100)
                {
                    lastReportMs = sw.ElapsedMilliseconds;
                    ReportProgress(progress, current, totalBytes, fileName, i + 1, totalFiles, sw.Elapsed);
                }
            }))
            {
                writer.Write(entryPath, reportingStream, (DateTime?)item.LastModified);
            }

            processedBytes = fileBytesBefore + item.Size;
            ReportProgress(progress, processedBytes, totalBytes, fileName, i + 1, totalFiles, sw.Elapsed);
        }
    }

    private void ReportProgress(
        IProgress<BackupProgressReport>? progress,
        long processedBytes,
        long totalBytes,
        string fileName,
        int fileIndex,
        int totalFiles,
        TimeSpan elapsed)
    {
        if (progress == null) return;

        double percent = totalBytes > 0 ? ((double)processedBytes / totalBytes) * 100.0 : 100.0;
        double speed = elapsed.TotalSeconds > 0 ? processedBytes / elapsed.TotalSeconds : 0;
        TimeSpan? remaining = null;
        if (speed > 0 && totalBytes > processedBytes)
        {
            remaining = TimeSpan.FromSeconds((totalBytes - processedBytes) / speed);
        }

        progress.Report(new BackupProgressReport
        {
            ProcessedBytes = processedBytes,
            TotalBytes = totalBytes,
            CurrentFileName = fileName,
            CurrentFileIndex = fileIndex,
            TotalFiles = totalFiles,
            Percentage = Math.Min(100.0, percent),
            SpeedBytesPerSec = speed,
            EstimatedRemaining = remaining,
            StatusMessage = $"Processing {fileIndex} of {totalFiles}: {fileName}"
        });
    }

    private string DetermineOutputName(BackupConfig config)
    {
        if (config.Format == BackupFormat.Copy)
        {
            return string.Empty;
        }

        string baseName = string.IsNullOrWhiteSpace(config.ArchiveBaseName) ? "Backup" : config.ArchiveBaseName.Trim();
        string timestamp = config.AppendTimestamp ? $"_{DateTime.Now:yyyy-MM-dd_HHmmss}" : "";
        string ext = config.Format switch
        {
            BackupFormat.Zip => ".zip",
            BackupFormat.SevenZip => ".7z",
            BackupFormat.Tar => ".tar",
            BackupFormat.TarGz => ".tar.gz",
            _ => ".zip"
        };

        return $"{baseName}{timestamp}{ext}";
    }

    private CompressionType GetSharpCompressZipType(BackupCompressionLevel level)
    {
        return level switch
        {
            BackupCompressionLevel.Store => CompressionType.None,
            _ => CompressionType.Deflate
        };
    }

    private int GetZipCompressionLevelInt(BackupCompressionLevel level)
    {
        return level switch
        {
            BackupCompressionLevel.Store => 0,
            BackupCompressionLevel.Fast => 1,
            BackupCompressionLevel.Normal => 5,
            BackupCompressionLevel.Maximum => 7,
            BackupCompressionLevel.Ultra => 9,
            _ => 5
        };
    }

    private CompressionType GetSharpCompress7zType(BackupCompressionLevel level)
    {
        return level switch
        {
            BackupCompressionLevel.Store => CompressionType.None,
            _ => CompressionType.LZMA
        };
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }
}

internal class ProgressReportingStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<int> _onBytesRead;

    public ProgressReportingStream(Stream inner, Action<int> onBytesRead)
    {
        _inner = inner;
        _onBytesRead = onBytesRead;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = _inner.Read(buffer, offset, count);
        if (bytesRead > 0)
        {
            _onBytesRead(bytesRead);
        }
        return bytesRead;
    }

    public override int Read(Span<byte> buffer)
    {
        int bytesRead = _inner.Read(buffer);
        if (bytesRead > 0)
        {
            _onBytesRead(bytesRead);
        }
        return bytesRead;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int bytesRead = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
        if (bytesRead > 0)
        {
            _onBytesRead(bytesRead);
        }
        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int bytesRead = await _inner.ReadAsync(buffer, cancellationToken);
        if (bytesRead > 0)
        {
            _onBytesRead(bytesRead);
        }
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}

