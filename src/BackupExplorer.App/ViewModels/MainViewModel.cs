using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using BackupExplorer.Core.Models;
using BackupExplorer.Core.Services;
using Wpf.Ui.Appearance;

namespace BackupExplorer.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IBackupEngine _engine;
    private bool _isProgressModalOpen;
    private bool _isDarkMode = true;

    public ExplorerViewModel Explorer { get; }
    public BackupConfigViewModel Config { get; }
    public BackupExecutionViewModel Execution { get; }

    public bool IsProgressModalOpen
    {
        get => _isProgressModalOpen;
        set => SetProperty(ref _isProgressModalOpen, value);
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                ApplyTheme();
            }
        }
    }

    public ICommand AddSelectedToStageCommand { get; }
    public ICommand StartBackupCommand { get; }
    public ICommand ToggleThemeCommand { get; }

    public MainViewModel()
    {
        _engine = new BackupEngine();
        Explorer = new ExplorerViewModel();
        Config = new BackupConfigViewModel();
        Execution = new BackupExecutionViewModel();

        Execution.Closed += () => IsProgressModalOpen = false;

        AddSelectedToStageCommand = new RelayCommand(AddSelectedToStage);
        StartBackupCommand = new RelayCommand(async () => await StartBackupAsync(), () => Config.CanStartBackup && !Execution.IsExecuting);
        ToggleThemeCommand = new RelayCommand(() => IsDarkMode = !IsDarkMode);
    }

    private void AddSelectedToStage()
    {
        var selected = Explorer.GetSelectedItems();
        if (selected.Count > 0)
        {
            Config.AddItems(selected);
            Explorer.UnselectAll();
        }
    }

    private async Task StartBackupAsync()
    {
        if (!Config.CanStartBackup) return;

        var coreFormat = Config.SelectedFormat switch
        {
            AppBackupFormat.Copy => BackupFormat.Copy,
            AppBackupFormat.Zip => BackupFormat.Zip,
            AppBackupFormat.SevenZip => BackupFormat.SevenZip,
            AppBackupFormat.Tar => BackupFormat.Tar,
            AppBackupFormat.TarGz => BackupFormat.TarGz,
            _ => BackupFormat.Zip
        };

        var coreLevel = Config.SelectedLevel switch
        {
            AppCompressionLevel.Store => BackupCompressionLevel.Store,
            AppCompressionLevel.Fast => BackupCompressionLevel.Fast,
            AppCompressionLevel.Normal => BackupCompressionLevel.Normal,
            AppCompressionLevel.Maximum => BackupCompressionLevel.Maximum,
            AppCompressionLevel.Ultra => BackupCompressionLevel.Ultra,
            _ => BackupCompressionLevel.Normal
        };

        var coreConfig = new BackupConfig
        {
            SourcePaths = Config.StagedItems.Select(s => s.FullPath).ToList(),
            DestinationDirectory = Config.DestinationDirectory,
            ArchiveBaseName = Config.ArchiveName,
            AppendTimestamp = Config.AppendTimestamp,
            Format = coreFormat,
            Level = coreLevel,
            PreserveDirectoryStructure = Config.PreserveDirectoryStructure
        };

        var cts = new CancellationTokenSource();
        Execution.StartExecution(cts);
        IsProgressModalOpen = true;

        var progress = new Progress<BackupProgressReport>(report =>
        {
            Execution.ReportProgress(report);
        });

        try
        {
            var result = await _engine.ExecuteBackupAsync(coreConfig, progress, cts.Token);
            Execution.Complete(result);
        }
        catch (Exception ex)
        {
            Execution.Complete(new BackupResult
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    private void ApplyTheme()
    {
        try
        {
            ApplicationThemeManager.Apply(IsDarkMode ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }
        catch { }
    }
}
