using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Input;
using BackupExplorer.Core.Models;

namespace BackupExplorer.App.ViewModels;

public class BackupExecutionViewModel : ViewModelBase
{
    private bool _isExecuting;
    private bool _isComplete;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _statusMessage = "Ready";
    private string _currentFileName = string.Empty;
    private int _currentFileIndex;
    private int _totalFiles;
    private double _overallProgress;
    private string _speedFormatted = "-- MB/s";
    private string _processedBytesFormatted = "0 B";
    private string _totalBytesFormatted = "0 B";
    private string _elapsedFormatted = "00:00";
    private string _remainingFormatted = "--:--";
    private BackupResult? _result;

    private CancellationTokenSource? _cts;

    public bool IsExecuting
    {
        get => _isExecuting;
        set => SetProperty(ref _isExecuting, value);
    }

    public bool IsComplete
    {
        get => _isComplete;
        set => SetProperty(ref _isComplete, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string CurrentFileName
    {
        get => _currentFileName;
        set => SetProperty(ref _currentFileName, value);
    }

    public int CurrentFileIndex
    {
        get => _currentFileIndex;
        set => SetProperty(ref _currentFileIndex, value);
    }

    public int TotalFiles
    {
        get => _totalFiles;
        set => SetProperty(ref _totalFiles, value);
    }

    public double OverallProgress
    {
        get => _overallProgress;
        set => SetProperty(ref _overallProgress, value);
    }

    public string SpeedFormatted
    {
        get => _speedFormatted;
        set => SetProperty(ref _speedFormatted, value);
    }

    public string ProcessedBytesFormatted
    {
        get => _processedBytesFormatted;
        set => SetProperty(ref _processedBytesFormatted, value);
    }

    public string TotalBytesFormatted
    {
        get => _totalBytesFormatted;
        set => SetProperty(ref _totalBytesFormatted, value);
    }

    public string ElapsedFormatted
    {
        get => _elapsedFormatted;
        set => SetProperty(ref _elapsedFormatted, value);
    }

    public string RemainingFormatted
    {
        get => _remainingFormatted;
        set => SetProperty(ref _remainingFormatted, value);
    }

    public BackupResult? Result
    {
        get => _result;
        set => SetProperty(ref _result, value);
    }

    public string CompletionSummary
    {
        get
        {
            if (Result == null || !Result.Success) return string.Empty;
            return $"Processed {Result.TotalFilesProcessed} files ({ExplorerViewModel.FormatBytes(Result.TotalBytesProcessed)}) in {Result.Duration.TotalSeconds:F1}s.\nOutput: {Result.OutputPath} ({ExplorerViewModel.FormatBytes(Result.OutputSizeBytes)})";
        }
    }

    public ICommand CancelCommand { get; }
    public ICommand OpenDestinationCommand { get; }
    public ICommand CloseCommand { get; }

    public event Action? Closed;

    public BackupExecutionViewModel()
    {
        CancelCommand = new RelayCommand(CancelExecution, () => IsExecuting);
        OpenDestinationCommand = new RelayCommand(OpenDestinationFolder);
        CloseCommand = new RelayCommand(Close);
    }

    public void StartExecution(CancellationTokenSource cts)
    {
        _cts = cts;
        IsExecuting = true;
        IsComplete = false;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusMessage = "Starting backup...";
        CurrentFileName = string.Empty;
        CurrentFileIndex = 0;
        TotalFiles = 0;
        OverallProgress = 0;
        SpeedFormatted = "Calculating...";
        ElapsedFormatted = "00:00";
        RemainingFormatted = "--:--";
        Result = null;
        OnPropertyChanged(nameof(CompletionSummary));
    }

    public void ReportProgress(BackupProgressReport report)
    {
        OverallProgress = report.Percentage;
        CurrentFileName = report.CurrentFileName;
        CurrentFileIndex = report.CurrentFileIndex;
        TotalFiles = report.TotalFiles;
        ProcessedBytesFormatted = ExplorerViewModel.FormatBytes(report.ProcessedBytes);
        TotalBytesFormatted = ExplorerViewModel.FormatBytes(report.TotalBytes);
        SpeedFormatted = $"{ExplorerViewModel.FormatBytes((long)report.SpeedBytesPerSec)}/s";
        StatusMessage = report.StatusMessage;

        if (report.EstimatedRemaining.HasValue)
        {
            RemainingFormatted = report.EstimatedRemaining.Value.ToString(@"mm\:ss");
        }
        else
        {
            RemainingFormatted = "--:--";
        }
    }

    public void Complete(BackupResult result)
    {
        IsExecuting = false;
        IsComplete = true;
        Result = result;

        if (!result.Success)
        {
            HasError = true;
            ErrorMessage = result.ErrorMessage ?? "Backup failed or was cancelled.";
            StatusMessage = ErrorMessage;
        }
        else
        {
            HasError = false;
            OverallProgress = 100;
            StatusMessage = "Backup completed successfully!";
        }

        OnPropertyChanged(nameof(CompletionSummary));
    }

    private void CancelExecution()
    {
        _cts?.Cancel();
        StatusMessage = "Cancelling backup...";
    }

    private void OpenDestinationFolder()
    {
        try
        {
            string? target = Result?.OutputPath;
            if (File.Exists(target))
            {
                Process.Start("explorer.exe", $"/select,\"{target}\"");
            }
            else if (Directory.Exists(target))
            {
                Process.Start("explorer.exe", $"\"{target}\"");
            }
        }
        catch { }
    }

    private void Close()
    {
        IsExecuting = false;
        IsComplete = false;
        HasError = false;
        Closed?.Invoke();
    }
}
