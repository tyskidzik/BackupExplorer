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
    private Point _dragStartPoint;
    private bool _isMouseDown;
    private bool _isDragging;
    private bool _isCtrlDrag;
    private readonly HashSet<ExplorerItem> _preDragSelection = new();

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

    private void OnExplorerListViewItemPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListViewItem lvi && lvi.DataContext is ExplorerItem item)
        {
            if (!ExplorerListView.SelectedItems.Contains(item))
            {
                ExplorerListView.SelectedItems.Clear();
                ExplorerListView.SelectedItem = item;
            }
            lvi.Focus();
        }
    }

    private void OnCheckBoxClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is ExplorerItem clickedItem)
        {
            var highlighted = ExplorerListView.SelectedItems.OfType<ExplorerItem>().ToList();
            if (highlighted.Count > 1 && highlighted.Contains(clickedItem))
            {
                bool newState = clickedItem.IsSelected;
                foreach (var item in highlighted)
                {
                    item.IsSelected = newState;
                }
            }
        }
    }

    private void OnExplorerListViewPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            if (FindVisualParent<GridViewColumnHeader>(dep) != null ||
                FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(dep) != null ||
                FindVisualParent<CheckBox>(dep) != null)
            {
                return;
            }
        }

        _isMouseDown = true;
        _isDragging = false;
        _dragStartPoint = e.GetPosition(ExplorerListContainer);
        _isCtrlDrag = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        _preDragSelection.Clear();
        if (_isCtrlDrag)
        {
            foreach (var item in ExplorerListView.SelectedItems.OfType<ExplorerItem>())
            {
                _preDragSelection.Add(item);
            }
        }
    }

    private void OnExplorerListViewPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMouseDown) return;

        Point currentPoint = e.GetPosition(ExplorerListContainer);

        if (!_isDragging)
        {
            double dx = Math.Abs(currentPoint.X - _dragStartPoint.X);
            double dy = Math.Abs(currentPoint.Y - _dragStartPoint.Y);
            if (dx > SystemParameters.MinimumHorizontalDragDistance || dy > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDragging = true;
                ExplorerListView.CaptureMouse();
                SelectionBox.Visibility = Visibility.Visible;
            }
        }

        if (_isDragging)
        {
            double x = Math.Min(_dragStartPoint.X, currentPoint.X);
            double y = Math.Min(_dragStartPoint.Y, currentPoint.Y);
            double w = Math.Abs(currentPoint.X - _dragStartPoint.X);
            double h = Math.Abs(currentPoint.Y - _dragStartPoint.Y);

            Canvas.SetLeft(SelectionBox, x);
            Canvas.SetTop(SelectionBox, y);
            SelectionBox.Width = w;
            SelectionBox.Height = h;

            Point lvPoint = e.GetPosition(ExplorerListView);
            var sv = FindVisualChild<ScrollViewer>(ExplorerListView);
            if (sv != null)
            {
                if (lvPoint.Y < 30)
                {
                    sv.ScrollToVerticalOffset(sv.VerticalOffset - 3);
                }
                else if (lvPoint.Y > ExplorerListView.ActualHeight - 30)
                {
                    sv.ScrollToVerticalOffset(sv.VerticalOffset + 3);
                }
            }

            Rect dragRect = new Rect(x, y, w, h);
            UpdateMarqueeSelection(dragRect);
        }
    }

    private void UpdateMarqueeSelection(Rect dragRect)
    {
        var intersectingItems = new HashSet<ExplorerItem>();

        foreach (var item in ExplorerListView.Items.OfType<ExplorerItem>())
        {
            if (ExplorerListView.ItemContainerGenerator.ContainerFromItem(item) is System.Windows.Controls.ListViewItem container && container.IsVisible)
            {
                try
                {
                    Point itemPos = container.TranslatePoint(new Point(0, 0), ExplorerListContainer);
                    Rect itemRect = new Rect(itemPos.X, itemPos.Y, container.ActualWidth, container.ActualHeight);

                    if (dragRect.IntersectsWith(itemRect))
                    {
                        intersectingItems.Add(item);
                    }
                }
                catch { }
            }
        }

        foreach (var item in ExplorerListView.Items.OfType<ExplorerItem>())
        {
            bool shouldSelect = _isCtrlDrag
                ? (_preDragSelection.Contains(item) ^ intersectingItems.Contains(item))
                : intersectingItems.Contains(item);

            bool isCurrentlySelected = ExplorerListView.SelectedItems.Contains(item);

            if (shouldSelect && !isCurrentlySelected)
            {
                ExplorerListView.SelectedItems.Add(item);
            }
            else if (!shouldSelect && isCurrentlySelected)
            {
                ExplorerListView.SelectedItems.Remove(item);
            }
        }
    }

    private void OnExplorerListViewPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _isMouseDown = false;
            SelectionBox.Visibility = Visibility.Collapsed;
            ExplorerListView.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (_isMouseDown && !_isCtrlDrag && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
        {
            if (e.OriginalSource is DependencyObject dep &&
                FindVisualParent<System.Windows.Controls.ListViewItem>(dep) == null &&
                FindVisualParent<GridViewColumnHeader>(dep) == null &&
                FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(dep) == null)
            {
                ExplorerListView.SelectedItems.Clear();
            }
        }

        _isMouseDown = false;
    }

    private void OnExplorerListViewLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _isMouseDown = false;
            SelectionBox.Visibility = Visibility.Collapsed;
        }
    }

    private ExplorerItem? GetHoveredExplorerItem()
    {
        var dep = Mouse.DirectlyOver as DependencyObject;
        if (dep == null)
        {
            try
            {
                Point mousePos = Mouse.GetPosition(ExplorerListView);
                var hitResult = System.Windows.Media.VisualTreeHelper.HitTest(ExplorerListView, mousePos);
                dep = hitResult?.VisualHit;
            }
            catch { }
        }

        if (dep != null)
        {
            var lvi = FindVisualParent<System.Windows.Controls.ListViewItem>(dep);
            if (lvi?.DataContext is ExplorerItem item)
            {
                return item;
            }
        }
        return null;
    }

    private void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase ||
                Keyboard.FocusedElement is System.Windows.Controls.TextBox ||
                Keyboard.FocusedElement is System.Windows.Controls.PasswordBox ||
                Keyboard.FocusedElement is Wpf.Ui.Controls.TextBox)
            {
                return;
            }

            var hoveredItem = GetHoveredExplorerItem();

            // 1. If mouse is hovered over an item that is NOT in current multi-selection,
            // toggle that hovered item for backup directly!
            if (hoveredItem != null && !ExplorerListView.SelectedItems.Contains(hoveredItem))
            {
                hoveredItem.IsSelected = !hoveredItem.IsSelected;
                ExplorerListView.SelectedItem = hoveredItem;
                e.Handled = true;
                return;
            }

            // 2. If list has focus or mouse is over a highlighted item, toggle the selected batch
            if (ExplorerListView.IsKeyboardFocusWithin || (hoveredItem != null && ExplorerListView.SelectedItems.Contains(hoveredItem)))
            {
                var selectedItems = ExplorerListView.SelectedItems.OfType<ExplorerItem>().ToList();
                if (selectedItems.Count == 0 && ExplorerListView.SelectedItem is ExplorerItem single)
                {
                    selectedItems.Add(single);
                }

                if (selectedItems.Count > 0)
                {
                    _viewModel.Explorer.ToggleItemsChecked(selectedItems);
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    private void OnExplorerListViewPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            if (e.Handled) return;

            var hoveredItem = GetHoveredExplorerItem();
            if (hoveredItem != null && !ExplorerListView.SelectedItems.Contains(hoveredItem))
            {
                hoveredItem.IsSelected = !hoveredItem.IsSelected;
                ExplorerListView.SelectedItem = hoveredItem;
                e.Handled = true;
                return;
            }

            var selectedItems = ExplorerListView.SelectedItems.OfType<ExplorerItem>().ToList();
            if (selectedItems.Count == 0 && ExplorerListView.SelectedItem is ExplorerItem single)
            {
                selectedItems.Add(single);
            }

            if (selectedItems.Count > 0)
            {
                _viewModel.Explorer.ToggleItemsChecked(selectedItems);
                e.Handled = true;
                return;
            }
        }
        else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ExplorerListView.SelectAll();
            e.Handled = true;
            return;
        }
        else if (e.Key == Key.Enter)
        {
            if (ExplorerListView.SelectedItem is ExplorerItem item && item.IsDirectory)
            {
                _ = _viewModel.Explorer.NavigateToAsync(item.FullPath);
                e.Handled = true;
                return;
            }
        }
    }

    private void OnExplorerContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var targetItems = ExplorerListView.SelectedItems.OfType<ExplorerItem>().ToList();
        if (targetItems.Count == 0 && ExplorerListView.SelectedItem is ExplorerItem sel)
        {
            targetItems.Add(sel);
        }

        if (ToggleCheckMenuItem != null)
        {
            if (targetItems.Count == 0)
            {
                ToggleCheckMenuItem.Header = "☑️ Check Selected (Space)";
                ToggleCheckMenuItem.IsEnabled = false;
            }
            else
            {
                ToggleCheckMenuItem.IsEnabled = true;
                bool allChecked = targetItems.All(i => i.IsSelected);
                if (allChecked)
                {
                    ToggleCheckMenuItem.Header = targetItems.Count > 1
                        ? "⬜ Uncheck Selected (Space)"
                        : "⬜ Uncheck (Space)";
                }
                else
                {
                    ToggleCheckMenuItem.Header = targetItems.Count > 1
                        ? "☑️ Check Selected (Space)"
                        : "☑️ Check (Space)";
                }
            }
        }
    }

    private void OnToggleCheckSelectedClick(object sender, RoutedEventArgs e)
    {
        var targetItems = ExplorerListView.SelectedItems.OfType<ExplorerItem>().ToList();
        if (targetItems.Count == 0 && ExplorerListView.SelectedItem is ExplorerItem sel)
        {
            targetItems.Add(sel);
        }

        if (targetItems.Count > 0)
        {
            _viewModel.Explorer.ToggleItemsChecked(targetItems);
        }
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

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null) return descendant;
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