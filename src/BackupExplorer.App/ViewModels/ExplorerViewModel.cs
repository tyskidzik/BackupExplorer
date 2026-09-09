using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using BackupExplorer.App.Models;
using BackupExplorer.App.Services;

namespace BackupExplorer.App.ViewModels;

public class ExplorerViewModel : ViewModelBase
{
    private string _currentPath = string.Empty;
    private bool _isLoading;
    private string _searchText = string.Empty;
    private string _selectedCategory = "All";
    private string _sortColumn = "Name";
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ExplorerItem> _selectedItemCache = new(StringComparer.OrdinalIgnoreCase);
    private List<ExplorerItem> _allItems = new();

    public ObservableCollection<DriveOrFolderItem> QuickAccessItems { get; } = new();
    public ObservableCollection<DriveOrFolderItem> DriveItems { get; } = new();
    public ObservableCollection<ExplorerItem> DisplayedItems { get; } = new();
    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = new();
    public ObservableCollection<string> Categories { get; } = new() { "All", "Documents", "Media", "Code", "Archives" };

    public string CurrentPath
    {
        get => _currentPath;
        set
        {
            if (SetProperty(ref _currentPath, value))
            {
                UpdateBreadcrumbs();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SortColumn
    {
        get => _sortColumn;
        set => SetProperty(ref _sortColumn, value);
    }

    public ListSortDirection SortDirection
    {
        get => _sortDirection;
        set => SetProperty(ref _sortDirection, value);
    }

    public string NameHeader => "Name" + GetSortArrow("Name");
    public string TypeHeader => "Type" + GetSortArrow("Type");
    public string SizeHeader => "Size" + GetSortArrow("Size");
    public string DateModifiedHeader => "Date Modified" + GetSortArrow("DateModified");

    private string GetSortArrow(string column)
    {
        if (!string.Equals(_sortColumn, column, StringComparison.OrdinalIgnoreCase)) return string.Empty;
        return _sortDirection == ListSortDirection.Ascending ? "  ▲" : "  ▼";
    }

    public int SelectedCount => _allItems.Count(i => i.IsSelected);
    public long SelectedSizeBytes => _allItems.Where(i => i.IsSelected).Sum(i => i.Size);
    public string SelectedSizeFormatted => FormatBytes(SelectedSizeBytes);
    public string SelectedSummary => $"{SelectedCount} selected ({SelectedSizeFormatted})";

    public int TotalItemsCount => _allItems.Count;
    public int FolderCount => _allItems.Count(i => i.IsDirectory);
    public int FileCount => _allItems.Count(i => !i.IsDirectory);
    public long TotalFolderSizeBytes => _allItems.Where(i => !i.IsDirectory).Sum(i => i.Size);

    public string StatusSummary => $"{TotalItemsCount} item(s) ({FolderCount} folders, {FileCount} files) | {SelectedSummary}";

    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;
    public bool CanGoUp
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CurrentPath)) return false;
            try
            {
                return Directory.GetParent(CurrentPath) != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public ICommand GoBackCommand { get; }
    public ICommand GoForwardCommand { get; }
    public ICommand GoUpCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand UnselectAllCommand { get; }
    public ICommand InvertSelectionCommand { get; }
    public ICommand CheckFilesOnlyCommand { get; }
    public ICommand CheckFoldersOnlyCommand { get; }
    public ICommand SortCommand { get; }

    public event Action? SelectionChanged;

    public ExplorerViewModel(bool autoLoad = true)
    {
        GoBackCommand = new RelayCommand(GoBack, () => CanGoBack);
        GoForwardCommand = new RelayCommand(GoForward, () => CanGoForward);
        GoUpCommand = new RelayCommand(GoUp, () => CanGoUp);
        RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        SelectAllCommand = new RelayCommand(SelectAll);
        UnselectAllCommand = new RelayCommand(UnselectAll);
        InvertSelectionCommand = new RelayCommand(InvertSelection);
        CheckFilesOnlyCommand = new RelayCommand(CheckFilesOnly);
        CheckFoldersOnlyCommand = new RelayCommand(CheckFoldersOnly);
        SortCommand = new RelayCommand(param =>
        {
            if (param is string col)
            {
                Sort(col);
            }
        });

        if (autoLoad)
        {
            LoadSidebars();

            string initialPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!Directory.Exists(initialPath))
            {
                initialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            _ = NavigateToAsync(initialPath, false);
        }
    }

    private void LoadSidebars()
    {
        AddSpecialFolder("Desktop", Environment.SpecialFolder.Desktop);
        AddSpecialFolder("Documents", Environment.SpecialFolder.MyDocuments);
        AddSpecialFolder("Downloads", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        AddSpecialFolder("Pictures", Environment.SpecialFolder.MyPictures);
        AddSpecialFolder("Music", Environment.SpecialFolder.MyMusic);
        AddSpecialFolder("Videos", Environment.SpecialFolder.MyVideos);

        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                long total = drive.TotalSize;
                long free = drive.AvailableFreeSpace;
                double usedPercent = total > 0 ? ((double)(total - free) / total) * 100.0 : 0;
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;

                DriveItems.Add(new DriveOrFolderItem
                {
                    Name = $"{label} ({drive.Name.TrimEnd('\\')})",
                    FullPath = drive.RootDirectory.FullName,
                    IsDrive = true,
                    TotalSize = total,
                    FreeSpace = free,
                    FreeSpaceFormatted = $"{FormatBytes(free)} free of {FormatBytes(total)}",
                    UsedPercent = usedPercent,
                    Icon = ShellIconService.GetDriveIcon(drive.RootDirectory.FullName)
                });
            }
        }
        catch { }
    }

    private void AddSpecialFolder(string name, Environment.SpecialFolder folder)
    {
        try
        {
            string path = Environment.GetFolderPath(folder);
            if (Directory.Exists(path))
            {
                QuickAccessItems.Add(new DriveOrFolderItem
                {
                    Name = name,
                    FullPath = path,
                    IsDrive = false,
                    Icon = ShellIconService.GetFolderIcon()
                });
            }
        }
        catch { }
    }

    private void AddSpecialFolder(string name, string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                QuickAccessItems.Add(new DriveOrFolderItem
                {
                    Name = name,
                    FullPath = path,
                    IsDrive = false,
                    Icon = ShellIconService.GetFolderIcon()
                });
            }
        }
        catch { }
    }

    public async Task NavigateToAsync(string path, bool recordHistory = true)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        if (recordHistory && !string.IsNullOrWhiteSpace(CurrentPath) && !string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            _backStack.Push(CurrentPath);
            _forwardStack.Clear();
        }

        CurrentPath = path;
        await LoadDirectoryAsync(path);

        CommandManager.InvalidateRequerySuggested();
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    public async void GoBack()
    {
        if (_backStack.Count > 0)
        {
            _forwardStack.Push(CurrentPath);
            string prev = _backStack.Pop();
            await NavigateToAsync(prev, false);
        }
    }

    public async void GoForward()
    {
        if (_forwardStack.Count > 0)
        {
            _backStack.Push(CurrentPath);
            string next = _forwardStack.Pop();
            await NavigateToAsync(next, false);
        }
    }

    public async void GoUp()
    {
        if (!string.IsNullOrWhiteSpace(CurrentPath))
        {
            var parent = Directory.GetParent(CurrentPath);
            if (parent != null && parent.Exists)
            {
                await NavigateToAsync(parent.FullName, true);
            }
        }
    }

    public async Task RefreshAsync()
    {
        if (!string.IsNullOrWhiteSpace(CurrentPath))
        {
            await LoadDirectoryAsync(CurrentPath);
        }
    }

    private async Task LoadDirectoryAsync(string path)
    {
        IsLoading = true;
        try
        {
            var items = await Task.Run(() =>
            {
                var list = new List<ExplorerItem>();
                var di = new DirectoryInfo(path);

                try
                {
                    foreach (var dir in di.EnumerateDirectories())
                    {
                        if ((dir.Attributes & FileAttributes.Hidden) != 0) continue;
                        list.Add(new ExplorerItem
                        {
                            Name = dir.Name,
                            FullPath = dir.FullName,
                            IsDirectory = true,
                            Size = 0,
                            SizeFormatted = "--",
                            ModifiedDate = dir.LastWriteTime,
                            ModifiedDateFormatted = dir.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                            Extension = "Folder",
                            Icon = ShellIconService.GetFolderIcon()
                        });
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (Exception) { }

                try
                {
                    foreach (var file in di.EnumerateFiles())
                    {
                        if ((file.Attributes & FileAttributes.Hidden) != 0) continue;
                        list.Add(new ExplorerItem
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            IsDirectory = false,
                            Size = file.Length,
                            SizeFormatted = FormatBytes(file.Length),
                            ModifiedDate = file.LastWriteTime,
                            ModifiedDateFormatted = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                            Extension = file.Extension.TrimStart('.').ToUpperInvariant(),
                            Icon = ShellIconService.GetFileIcon(file.FullName)
                        });
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (Exception) { }

                return list;
            });

            foreach (var item in _allItems)
            {
                item.PropertyChanged -= Item_PropertyChanged;
            }

            bool isCurrentFolderSelected = _selectedPaths.Contains(path) || IsAncestorFolderSelected(path);

            foreach (var item in items)
            {
                if (isCurrentFolderSelected)
                {
                    item.IsSelected = true;
                    _selectedPaths.Add(item.FullPath);
                    _selectedItemCache[item.FullPath] = item;
                }
                else if (_selectedPaths.Contains(item.FullPath))
                {
                    item.IsSelected = true;
                    _selectedItemCache[item.FullPath] = item;
                }
                item.PropertyChanged += Item_PropertyChanged;
            }

            _allItems = items;

            ApplySortInternal();
            ApplyFilter();
            UpdateStats();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool IsAncestorFolderSelected(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string? parent = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(parent))
        {
            if (_selectedPaths.Contains(parent))
            {
                return true;
            }
            parent = Path.GetDirectoryName(parent);
        }
        return false;
    }

    private void RemoveSelectedAncestors(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        string? parent = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(parent))
        {
            if (_selectedPaths.Contains(parent))
            {
                _selectedPaths.Remove(parent);
                _selectedItemCache.Remove(parent);
            }
            parent = Path.GetDirectoryName(parent);
        }
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerItem.IsSelected) && sender is ExplorerItem item)
        {
            if (item.IsSelected)
            {
                _selectedPaths.Add(item.FullPath);
                _selectedItemCache[item.FullPath] = item;
            }
            else
            {
                _selectedPaths.Remove(item.FullPath);
                _selectedItemCache.Remove(item.FullPath);

                // If unchecking a folder, recursively remove any selected sub-items inside it
                if (item.IsDirectory)
                {
                    string folderPrefix = item.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    var subPaths = _selectedPaths.Where(p => p.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var sub in subPaths)
                    {
                        _selectedPaths.Remove(sub);
                        _selectedItemCache.Remove(sub);
                    }
                }

                // If unchecking an item inside a folder, remove parent folders from selection
                RemoveSelectedAncestors(item.FullPath);
            }

            UpdateStats();
            SelectionChanged?.Invoke();
        }
    }

    public void SetItemsForDisplay(IEnumerable<ExplorerItem> items)
    {
        _allItems = items.ToList();
        DisplayedItems.Clear();
        foreach (var item in _allItems)
        {
            if (item.IsSelected)
            {
                _selectedPaths.Add(item.FullPath);
                _selectedItemCache[item.FullPath] = item;
            }
            DisplayedItems.Add(item);
        }
        UpdateStats();
    }

    public void UpdateStats()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSizeBytes));
        OnPropertyChanged(nameof(SelectedSizeFormatted));
        OnPropertyChanged(nameof(SelectedSummary));
        OnPropertyChanged(nameof(TotalItemsCount));
        OnPropertyChanged(nameof(FolderCount));
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(TotalFolderSizeBytes));
        OnPropertyChanged(nameof(StatusSummary));
    }

    public void Sort(string column)
    {
        if (string.Equals(_sortColumn, column, StringComparison.OrdinalIgnoreCase))
        {
            SortDirection = _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            SortColumn = column;
            SortDirection = ListSortDirection.Ascending;
        }

        ApplySortInternal();
        ApplyFilter();

        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(TypeHeader));
        OnPropertyChanged(nameof(SizeHeader));
        OnPropertyChanged(nameof(DateModifiedHeader));
    }

    private void ApplySortInternal()
    {
        Func<ExplorerItem, object> keySelector = _sortColumn.ToLowerInvariant() switch
        {
            "type" => i => i.Extension,
            "size" => i => i.Size,
            "datemodified" => i => i.ModifiedDate,
            _ => i => i.Name
        };

        if (_sortDirection == ListSortDirection.Ascending)
        {
            _allItems = _allItems.OrderByDescending(i => i.IsDirectory).ThenBy(keySelector).ToList();
        }
        else
        {
            _allItems = _allItems.OrderByDescending(i => i.IsDirectory).ThenByDescending(keySelector).ToList();
        }
    }

    private void ApplyFilter()
    {
        DisplayedItems.Clear();
        var query = SearchText?.Trim() ?? string.Empty;

        IEnumerable<ExplorerItem> filtered = _allItems;

        // Search text filter
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           i.Extension.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        // Category filter
        if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "All")
        {
            filtered = filtered.Where(i => i.IsDirectory || MatchesCategory(i.Extension, SelectedCategory));
        }

        foreach (var item in filtered)
        {
            DisplayedItems.Add(item);
        }

        UpdateStats();
    }

    private static bool MatchesCategory(string ext, string category)
    {
        string e = ext.ToLowerInvariant();
        return category switch
        {
            "Documents" => e is "pdf" or "docx" or "doc" or "txt" or "xlsx" or "xls" or "pptx" or "ppt" or "csv" or "odt" or "rtf" or "md",
            "Media" => e is "jpg" or "jpeg" or "png" or "gif" or "bmp" or "webp" or "mp4" or "mkv" or "avi" or "mov" or "mp3" or "wav" or "flac",
            "Code" => e is "cs" or "cpp" or "c" or "h" or "hpp" or "js" or "ts" or "py" or "json" or "xml" or "html" or "css" or "xaml" or "sh" or "ps1" or "sql",
            "Archives" => e is "zip" or "7z" or "rar" or "tar" or "gz" or "bz2" or "xz" or "zst",
            _ => true
        };
    }

    private void UpdateBreadcrumbs()
    {
        Breadcrumbs.Clear();
        if (string.IsNullOrWhiteSpace(CurrentPath)) return;

        try
        {
            var parts = new List<BreadcrumbItem>();
            var di = new DirectoryInfo(CurrentPath);
            DirectoryInfo? curr = di;

            while (curr != null)
            {
                string name = curr.Parent == null ? curr.FullName : curr.Name;
                parts.Insert(0, new BreadcrumbItem { Name = name, FullPath = curr.FullName });
                curr = curr.Parent;
            }

            foreach (var part in parts)
            {
                Breadcrumbs.Add(part);
            }
        }
        catch { }
    }

    public void SelectAll()
    {
        foreach (var item in DisplayedItems)
        {
            item.IsSelected = true;
        }
    }

    public void UnselectAll()
    {
        _selectedPaths.Clear();
        _selectedItemCache.Clear();
        foreach (var item in _allItems)
        {
            item.IsSelected = false;
        }
        UpdateStats();
        SelectionChanged?.Invoke();
    }

    public void InvertSelection()
    {
        foreach (var item in DisplayedItems)
        {
            item.IsSelected = !item.IsSelected;
        }
    }

    public void CheckFilesOnly()
    {
        foreach (var item in DisplayedItems)
        {
            item.IsSelected = !item.IsDirectory;
        }
    }

    public void CheckFoldersOnly()
    {
        foreach (var item in DisplayedItems)
        {
            item.IsSelected = item.IsDirectory;
        }
    }

    public void ToggleItemsChecked(IEnumerable<ExplorerItem> items)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0) return;

        bool hasUnchecked = itemList.Any(i => !i.IsSelected);
        foreach (var item in itemList)
        {
            item.IsSelected = hasUnchecked;
        }
    }

    public void SetItemsChecked(IEnumerable<ExplorerItem> items, bool isChecked)
    {
        foreach (var item in items)
        {
            item.IsSelected = isChecked;
        }
    }

    public List<ExplorerItem> GetSelectedItems()
    {
        var all = _selectedItemCache.Values.ToList();
        if (all.Count == 0) return all;

        var dirPaths = all.Where(i => i.IsDirectory)
                          .Select(i => i.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
                          .ToList();

        if (dirPaths.Count == 0) return all;

        var topLevel = new List<ExplorerItem>();
        foreach (var item in all)
        {
            bool hasParentDirInSelection = dirPaths.Any(dp =>
                !item.FullPath.Equals(dp.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                item.FullPath.StartsWith(dp, StringComparison.OrdinalIgnoreCase));

            if (!hasParentDirInSelection)
            {
                topLevel.Add(item);
            }
        }

        return topLevel;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.##} MB";
        double gb = mb / 1024.0;
        return $"{gb:0.##} GB";
    }
}
