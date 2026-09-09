using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BackupExplorer.Core.Models;
using BackupExplorer.Core.Services;
using Xunit;

namespace BackupExplorer.Tests;

public class BackupEngineTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _sourceDir;
    private readonly string _destDir;
    private readonly BackupEngine _engine;

    public BackupEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "BackupExplorerTests_" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_testDir, "Source");
        _destDir = Path.Combine(_testDir, "Dest");

        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);

        // Populate source with dummy files and folders
        File.WriteAllText(Path.Combine(_sourceDir, "file1.txt"), "Hello world test 1");
        File.WriteAllText(Path.Combine(_sourceDir, "file2.log"), "Log file content test 2");

        string sub = Path.Combine(_sourceDir, "SubFolder");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "nested.txt"), "Nested content test 3");

        _engine = new BackupEngine();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task ScanItemsAsync_FindsAllFilesAndSubfolders()
    {
        var items = await _engine.ScanItemsAsync(new[] { _sourceDir });

        Assert.Equal(3, items.Count);
        Assert.Contains(items, i => i.RelativePath.EndsWith("file1.txt"));
        Assert.Contains(items, i => i.RelativePath.EndsWith("file2.log"));
        Assert.Contains(items, i => i.RelativePath.Contains("SubFolder") && i.RelativePath.EndsWith("nested.txt"));
    }

    [Fact]
    public async Task ExecuteBackupAsync_DirectCopy_CopiesAllFilesWithStructure()
    {
        var config = new BackupConfig
        {
            SourcePaths = new List<string> { _sourceDir },
            DestinationDirectory = _destDir,
            Format = BackupFormat.Copy,
            PreserveDirectoryStructure = true
        };

        var result = await _engine.ExecuteBackupAsync(config);

        Assert.True(result.Success);
        Assert.Equal(3, result.TotalFilesProcessed);

        // Verify copied files
        var copiedFiles = Directory.GetFiles(_destDir, "*.*", SearchOption.AllDirectories);
        Assert.Equal(3, copiedFiles.Length);
    }

    [Fact]
    public async Task ExecuteBackupAsync_Zip_CreatesValidZipFile()
    {
        var config = new BackupConfig
        {
            SourcePaths = new List<string> { _sourceDir },
            DestinationDirectory = _destDir,
            ArchiveBaseName = "TestArchive",
            AppendTimestamp = false,
            Format = BackupFormat.Zip,
            Level = BackupCompressionLevel.Normal
        };

        var reports = new List<BackupProgressReport>();
        var progress = new Progress<BackupProgressReport>(r => reports.Add(r));

        var result = await _engine.ExecuteBackupAsync(config, progress);

        Assert.True(result.Success);
        Assert.True(File.Exists(result.OutputPath));
        Assert.EndsWith(".zip", result.OutputPath);
        Assert.True(result.OutputSizeBytes > 0);
    }

    [Fact]
    public async Task ExecuteBackupAsync_SevenZip_CreatesValid7zFile()
    {
        var config = new BackupConfig
        {
            SourcePaths = new List<string> { _sourceDir },
            DestinationDirectory = _destDir,
            ArchiveBaseName = "Test7z",
            AppendTimestamp = false,
            Format = BackupFormat.SevenZip,
            Level = BackupCompressionLevel.Normal
        };

        var result = await _engine.ExecuteBackupAsync(config);

        Assert.True(result.Success);
        Assert.True(File.Exists(result.OutputPath));
        Assert.EndsWith(".7z", result.OutputPath);
        Assert.True(result.OutputSizeBytes > 0);
    }

    [Fact]
    public async Task ExecuteBackupAsync_TarGz_CreatesValidTarGzFile()
    {
        var config = new BackupConfig
        {
            SourcePaths = new List<string> { _sourceDir },
            DestinationDirectory = _destDir,
            ArchiveBaseName = "TestTarGz",
            AppendTimestamp = false,
            Format = BackupFormat.TarGz
        };

        var result = await _engine.ExecuteBackupAsync(config);

        Assert.True(result.Success);
        Assert.True(File.Exists(result.OutputPath));
        Assert.EndsWith(".tar.gz", result.OutputPath);
        Assert.True(result.OutputSizeBytes > 0);
    }

    [Fact]
    public async Task ExecuteBackupAsync_Cancellation_AbortsAndCleansUp()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var config = new BackupConfig
        {
            SourcePaths = new List<string> { _sourceDir },
            DestinationDirectory = _destDir,
            ArchiveBaseName = "CancelTest",
            Format = BackupFormat.Zip
        };

        var result = await _engine.ExecuteBackupAsync(config, null, cts.Token);

        Assert.False(result.Success);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteBackupAsync_Zip_ReportsProgressForFiles()
    {
        var config = new BackupConfig
        {
            SourcePaths = new List<string> { _sourceDir },
            DestinationDirectory = _destDir,
            ArchiveBaseName = "ProgressTest",
            AppendTimestamp = false,
            Format = BackupFormat.Zip,
            Level = BackupCompressionLevel.Normal
        };

        var fileNamesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var progress = new Progress<BackupProgressReport>(report =>
        {
            if (!string.IsNullOrEmpty(report.CurrentFileName))
            {
                fileNamesSeen.Add(report.CurrentFileName);
            }
        });

        var result = await _engine.ExecuteBackupAsync(config, progress);

        Assert.True(result.Success);
        Assert.Contains("file1.txt", fileNamesSeen);
        Assert.Contains("file2.log", fileNamesSeen);
        Assert.Contains("nested.txt", fileNamesSeen);
    }

    [Theory]
    [InlineData(@"C:\Folder\file.txt", "Drive_C")]
    [InlineData(@"d:\Data\Sub", "Drive_D")]
    [InlineData(@"E:\", "Drive_E")]
    [InlineData(@"\\NasServer\Backups\file.txt", "Share_NasServer_Backups")]
    [InlineData(@"\\Server\Share", "Share_Server_Share")]
    public void GetDrivePrefix_FormatsCorrectly(string path, string expected)
    {
        var prefix = BackupEngine.GetDrivePrefix(path);
        Assert.Equal(expected, prefix);
    }

    [Fact]
    public async Task ScanItemsAsync_SingleDrive_DoesNotAddDrivePrefix()
    {
        var items = await _engine.ScanItemsAsync(new[] { _sourceDir });
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.False(item.RelativePath.StartsWith("Drive_")));
    }

    [Fact]
    public async Task ScanItemsAsync_MultiDrive_NamespacesByDrive()
    {
        string? secondDrive = null;
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady && !string.Equals(drive.RootDirectory.FullName, Path.GetPathRoot(_sourceDir), StringComparison.OrdinalIgnoreCase))
            {
                secondDrive = drive.RootDirectory.FullName;
                break;
            }
        }

        if (secondDrive == null) return;

        string tempSecondFile = Path.Combine(secondDrive, "test_backup_multidrive_" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(tempSecondFile, "multi-drive test content");
            var items = await _engine.ScanItemsAsync(new[] { _sourceDir, tempSecondFile });

            string expectedSourceDrive = BackupEngine.GetDrivePrefix(_sourceDir);
            string expectedSecondDrive = BackupEngine.GetDrivePrefix(tempSecondFile);

            Assert.Contains(items, i => i.RelativePath.StartsWith(expectedSourceDrive));
            Assert.Contains(items, i => i.RelativePath.StartsWith(expectedSecondDrive));
        }
        catch (UnauthorizedAccessException)
        {
            // If access to second drive root is restricted, pass
        }
        finally
        {
            try { if (File.Exists(tempSecondFile)) File.Delete(tempSecondFile); } catch { }
        }
    }
}
