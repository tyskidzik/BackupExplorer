using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using BackupExplorer.App.Models;
using Microsoft.Win32;

namespace BackupExplorer.App.ViewModels;

public enum AppBackupFormat
{
    Copy,
    Zip,
    SevenZip,
    Tar,
    TarGz
}

public enum AppCompressionLevel
{
    Store,
    Fast,
    Normal,
    Maximum,
    Ultra
}

public class BackupConfigViewModel : ViewModelBase
{
    private string _destinationDirectory = string.Empty;
    private string _archiveName = "Backup";
    private bool _appendTimestamp = true;
    private AppBackupFormat _selectedFormat = AppBackupFormat.Zip;
    private AppCompressionLevel _selectedLevel = AppCompressionLevel.Normal;
    private bool _preserveDirectoryStructure = true;
    private long _destinationFreeSpace;

    public ObservableCollection<StagedItem> StagedItems { get; } = new();

    public string DestinationDirectory
    {
        get => _destinationDirectory;
        set
        {
            if (SetProperty(ref _destinationDirectory, value))
            {
                UpdateDestinationDiskSpace();
                OnPropertyChanged(nameof(CanStartBackup));
                OnPropertyChanged(nameof(IsDestinationValid));
            }
        }
    }

    public string ArchiveName
    {
        get => _archiveName;
        set
        {
            if (SetProperty(ref _archiveName, value))
            {
                OnPropertyChanged(nameof(PreviewOutputName));
            }
        }
    }

    public bool AppendTimestamp
    {
        get => _appendTimestamp;
        set
        {
            if (SetProperty(ref _appendTimestamp, value))
            {
                OnPropertyChanged(nameof(PreviewOutputName));
            }
        }
    }

    public AppBackupFormat SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
            {
                OnPropertyChanged(nameof(IsArchiveFormat));
                OnPropertyChanged(nameof(PreviewOutputName));
            }
        }
    }

    public AppCompressionLevel SelectedLevel
    {
        get => _selectedLevel;
        set => SetProperty(ref _selectedLevel, value);
    }

    public bool PreserveDirectoryStructure
    {
        get => _preserveDirectoryStructure;
        set => SetProperty(ref _preserveDirectoryStructure, value);
    }

    public bool IsArchiveFormat => SelectedFormat != AppBackupFormat.Copy;

    public int TotalStagedCount => StagedItems.Count;

    public long TotalStagedSizeBytes => StagedItems.Sum(i => i.Size);

    public string TotalStagedSizeFormatted => ExplorerViewModel.FormatBytes(TotalStagedSizeBytes);

    public string StagedSummary => $"{TotalStagedCount} item(s) queued ({TotalStagedSizeFormatted})";

    public long DestinationFreeSpace
    {
        get => _destinationFreeSpace;
        private set
        {
            if (SetProperty(ref _destinationFreeSpace, value))
            {
                OnPropertyChanged(nameof(DestinationFreeSpaceFormatted));
                OnPropertyChanged(nameof(HasEnoughDiskSpace));
            }
        }
    }

    public string DestinationFreeSpaceFormatted => ExplorerViewModel.FormatBytes(DestinationFreeSpace);

    public bool HasEnoughDiskSpace => DestinationFreeSpace >= TotalStagedSizeBytes;

    public bool IsDestinationValid => !string.IsNullOrWhiteSpace(DestinationDirectory) && Directory.Exists(DestinationDirectory);

    public bool CanStartBackup => StagedItems.Count > 0 && IsDestinationValid;
    public bool IsEmpty => StagedItems.Count == 0;
    public bool HasStagedItems => StagedItems.Count > 0;

    public string PreviewOutputName
    {
        get
        {
            if (SelectedFormat == AppBackupFormat.Copy)
            {
                return "[Direct Directory Mirror]";
            }

            string baseName = string.IsNullOrWhiteSpace(ArchiveName) ? "Backup" : ArchiveName.Trim();
            string timestamp = AppendTimestamp ? $"_{DateTime.Now:yyyy-MM-dd_HHmmss}" : "";
            string ext = SelectedFormat switch
            {
                AppBackupFormat.Zip => ".zip",
                AppBackupFormat.SevenZip => ".7z",
                AppBackupFormat.Tar => ".tar",
                AppBackupFormat.TarGz => ".tar.gz",
                _ => ".zip"
            };

            return $"{baseName}{timestamp}{ext}";
        }
    }

    public ICommand BrowseDestinationCommand { get; }
    public ICommand RemoveStagedItemCommand { get; }
    public ICommand ClearStagedCommand { get; }


    public BackupConfigViewModel()
    {
        BrowseDestinationCommand = new RelayCommand(BrowseDestination);
        RemoveStagedItemCommand = new RelayCommand(param =>
        {
            if (param is StagedItem item)
            {
                RemoveStagedItem(item);
            }
        });
        ClearStagedCommand = new RelayCommand(ClearStaged);

        // Default destination: User's backup folder or Documents
        string defaultDest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Backups");
        try
        {
            if (!Directory.Exists(defaultDest))
            {
                Directory.CreateDirectory(defaultDest);
            }
            DestinationDirectory = defaultDest;
        }
        catch
        {
            DestinationDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }

    public void AddItems(IEnumerable<ExplorerItem> items)
    {
        foreach (var item in items)
        {
            if (StagedItems.Any(s => s.FullPath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            long size = item.Size;
            if (item.IsDirectory && size == 0)
            {
                // Calculate folder size asynchronously or quick estimate
                size = CalculateFolderSizeSafe(item.FullPath);
            }

            StagedItems.Add(new StagedItem
            {
                FullPath = item.FullPath,
                Name = item.Name,
                IsDirectory = item.IsDirectory,
                Size = size,
                SizeFormatted = ExplorerViewModel.FormatBytes(size),
                Icon = item.Icon
            });
        }

        UpdateStagedStats();
    }

    public void RemoveStagedItem(StagedItem item)
    {
        StagedItems.Remove(item);
        UpdateStagedStats();
    }

    public void ClearStaged()
    {
        StagedItems.Clear();
        UpdateStagedStats();
    }

    public void UpdateStagedStats()
    {
        OnPropertyChanged(nameof(TotalStagedCount));
        OnPropertyChanged(nameof(TotalStagedSizeBytes));
        OnPropertyChanged(nameof(TotalStagedSizeFormatted));
        OnPropertyChanged(nameof(StagedSummary));
        OnPropertyChanged(nameof(HasEnoughDiskSpace));
        OnPropertyChanged(nameof(CanStartBackup));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasStagedItems));
    }

    private void BrowseDestination()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Destination Folder for Backup",
            InitialDirectory = Directory.Exists(DestinationDirectory) ? DestinationDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() == true)
        {
            DestinationDirectory = dialog.FolderName;
        }
    }

    private void UpdateDestinationDiskSpace()
    {
        if (string.IsNullOrWhiteSpace(DestinationDirectory) || !Directory.Exists(DestinationDirectory))
        {
            DestinationFreeSpace = 0;
            return;
        }

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(DestinationDirectory)) ?? "C:\\");
            if (drive.IsReady)
            {
                DestinationFreeSpace = drive.AvailableFreeSpace;
            }
        }
        catch
        {
            DestinationFreeSpace = 0;
        }
    }

    private long CalculateFolderSizeSafe(string dirPath)
    {
        try
        {
            var di = new DirectoryInfo(dirPath);
            return di.EnumerateFiles("*", SearchOption.AllDirectories)
                     .Where(f => (f.Attributes & FileAttributes.Hidden) == 0)
                     .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }
}
