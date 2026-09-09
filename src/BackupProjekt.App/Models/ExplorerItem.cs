using System;
using System.Windows.Media;
using BackupProjekt.App.ViewModels;

namespace BackupProjekt.App.Models;

public class ExplorerItem : ViewModelBase
{
    private bool _isSelected;

    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public string SizeFormatted { get; set; } = string.Empty;
    public DateTime ModifiedDate { get; set; }
    public string ModifiedDateFormatted { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public ImageSource? Icon { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
