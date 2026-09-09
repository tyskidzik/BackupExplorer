using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using BackupExplorer.App.Models;
using BackupExplorer.App.ViewModels;
using Wpf.Ui.Controls;

namespace BackupExplorer.App;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public static readonly IValueConverter BoolToFontWeightConverter = new BoolToFontWeightConverterInternal();

    public MainWindow(MainViewModel? viewModel = null)
    {
        InitializeComponent();
        _viewModel = viewModel ?? new MainViewModel();
        DataContext = _viewModel;
    }

    private void OnSidebarItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DriveOrFolderItem item)
        {
            _ = _viewModel.Explorer.NavigateToAsync(item.FullPath);
        }
    }

    private void OnBreadcrumbClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BreadcrumbItem crumb)
        {
            _ = _viewModel.Explorer.NavigateToAsync(crumb.FullPath);
        }
    }

    private void OnCheckBoxCellDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void OnCheckBoxDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void OnExplorerItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Ignore double-clicks originating on or inside a CheckBox or the CheckBox column
        if (e.OriginalSource is DependencyObject dep)
        {
            if (dep is CheckBox || FindVisualParent<CheckBox>(dep) != null)
            {
                e.Handled = true;
                return;
            }
        }

        if (sender is System.Windows.Controls.ListView lv && lv.SelectedItem is ExplorerItem item)
        {
            if (item.IsDirectory)
            {
                _ = _viewModel.Explorer.NavigateToAsync(item.FullPath);
            }
        }
    }

    private void OnGridViewColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        GridViewColumnHeader? header = e.OriginalSource as GridViewColumnHeader;
        if (header == null && e.OriginalSource is DependencyObject dep)
        {
            header = FindVisualParent<GridViewColumnHeader>(dep);
        }
        if (header == null)
        {
            header = e.Source as GridViewColumnHeader;
        }

        if (header != null && header.Role != GridViewColumnHeaderRole.Padding && header.Column != null)
        {
            string? title = header.Column.Header?.ToString();
            if (!string.IsNullOrEmpty(title))
            {
                if (title.StartsWith("Name", StringComparison.OrdinalIgnoreCase)) _viewModel.Explorer.Sort("Name");
                else if (title.StartsWith("Type", StringComparison.OrdinalIgnoreCase)) _viewModel.Explorer.Sort("Type");
                else if (title.StartsWith("Size", StringComparison.OrdinalIgnoreCase)) _viewModel.Explorer.Sort("Size");
                else if (title.StartsWith("Date", StringComparison.OrdinalIgnoreCase)) _viewModel.Explorer.Sort("DateModified");
            }
        }
    }

    private void OnAddSelectedToQueueClick(object sender, RoutedEventArgs e)
    {
        var highlighted = ExplorerListView.SelectedItems.OfType<ExplorerItem>().ToList();
        // If the user highlighted rows in the list without toggling checkboxes, auto-check them
        if (highlighted.Count > 0 && !_viewModel.Explorer.GetSelectedItems().Any())
        {
            _viewModel.Explorer.SetItemsChecked(highlighted, true);
        }
        _viewModel.AddSelectedToStageCommand.Execute(null);
    }

    private void OnExplorerListViewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            var selectedItems = ExplorerListView.SelectedItems.OfType<ExplorerItem>().ToList();
            if (selectedItems.Count > 0)
            {
                _viewModel.Explorer.ToggleItemsChecked(selectedItems);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter)
        {
            if (ExplorerListView.SelectedItem is ExplorerItem item && item.IsDirectory)
            {
                _ = _viewModel.Explorer.NavigateToAsync(item.FullPath);
                e.Handled = true;
            }
        }
    }

    private void OnCheckSelectedClick(object sender, RoutedEventArgs e)
    {
        var selectedItems = ExplorerListView.SelectedItems.OfType<ExplorerItem>().ToList();
        _viewModel.Explorer.SetItemsChecked(selectedItems, true);
    }

    private void OnUncheckSelectedClick(object sender, RoutedEventArgs e)
    {
        var selectedItems = ExplorerListView.SelectedItems.OfType<ExplorerItem>().ToList();
        _viewModel.Explorer.SetItemsChecked(selectedItems, false);
    }

    private void OnOpenFolderContextClick(object sender, RoutedEventArgs e)
    {
        if (ExplorerListView.SelectedItem is ExplorerItem item && item.IsDirectory)
        {
            _ = _viewModel.Explorer.NavigateToAsync(item.FullPath);
        }
    }

    private void OnShowInExplorerContextClick(object sender, RoutedEventArgs e)
    {
        if (ExplorerListView.SelectedItem is ExplorerItem item)
        {
            try
            {
                if (File.Exists(item.FullPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
                }
                else if (Directory.Exists(item.FullPath))
                {
                    Process.Start("explorer.exe", $"\"{item.FullPath}\"");
                }
            }
            catch { }
        }
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parentObj = System.Windows.Media.VisualTreeHelper.GetParent(child);
        while (parentObj != null)
        {
            if (parentObj is T parent) return parent;
            parentObj = System.Windows.Media.VisualTreeHelper.GetParent(parentObj);
        }
        return null;
    }

    private class BoolToFontWeightConverterInternal : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? FontWeights.SemiBold : FontWeights.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}